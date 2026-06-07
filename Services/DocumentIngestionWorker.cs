using System.Text;
using System.Text.Json;
using InfinityAI.Document.Worker.Clients;
using InfinityAI.Document.Worker.Helpers;
using InfinityAI.Document.Worker.Models;
using InfinityAI.Document.Worker.Models.Api;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace InfinityAI.Document.Worker.Services;

public sealed class DocumentIngestionWorker(
    IServiceScopeFactory                  scopeFactory,
    IConfiguration                        configuration,
    IOptions<DocumentIngestionOptions>    options,
    ILogger<DocumentIngestionWorker>      logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("[DOC-WORKER] Async ingestion disabled — worker not starting.");
            return;
        }

        logger.LogInformation("[DOC-WORKER] Starting. Queue={Queue}", options.Value.QueueName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DOC-WORKER] Connection lost — reconnecting in 5 s");
                await Task.Delay(5_000, stoppingToken);
            }
        }

        logger.LogInformation("[DOC-WORKER] Stopped.");
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        var server = configuration["RabbitMQServer"] ?? "rabbitmq";
        var port   = int.TryParse(configuration["RabbitMQPort"], out var p) ? p : 5672;
        var queue  = options.Value.QueueName;

        var factory = new ConnectionFactory
        {
            HostName                 = server,
            Port                     = port,
            AutomaticRecoveryEnabled = true
        };

        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel    = await connection.CreateChannelAsync(cancellationToken: ct);

        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: ct);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.ReceivedAsync += async (_, ea) =>
        {
            var raw = Encoding.UTF8.GetString(ea.Body.Span);
            DocumentIngestionJob? job = null;

            try
            {
                job = JsonSerializer.Deserialize<DocumentIngestionJob>(raw);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DOC-WORKER] Failed to deserialize job — discarding");
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
                return;
            }

            if (job is null)
            {
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
                return;
            }

            try
            {
                await ProcessJobAsync(job, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[DOC-WORKER] Unhandled error for DocumentId={DocumentId}", job.DocumentId);
            }
            finally
            {
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
            }
        };

        await channel.BasicConsumeAsync(queue, autoAck: false, consumer: consumer, cancellationToken: ct);
        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task ProcessJobAsync(DocumentIngestionJob job, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp          = scope.ServiceProvider;
        var apiClient   = sp.GetRequiredService<DocumentIngestionApiClient>();
        var extraction  = sp.GetRequiredService<IDocumentExtractionService>();
        var embedding   = sp.GetRequiredService<IEmbeddingService>();
        var opts        = options.Value;

        // ── Step 1: Claim ───────────────────────────────────────────────────

        var lockId      = Guid.NewGuid().ToString();
        var machineName = Environment.MachineName;

        var claim = await apiClient.ClaimJobAsync(job.DocumentId, lockId, machineName, ct);

        if (!claim.Claimed)
        {
            logger.LogInformation(
                "[DOC-WORKER] DocumentId={DocumentId} not claimed — Reason={Reason}",
                job.DocumentId, claim.Reason);
            return;
        }

        logger.LogInformation(
            "[DOC-WORKER] DocumentId={DocumentId} claimed. FileName={FileName} DocumentType={Type} Attempt={Attempt} Worker={Worker}",
            job.DocumentId, claim.FileName, claim.DocumentType, claim.AttemptCount, machineName);

        var startTime = DateTime.UtcNow;

        try
        {
            // ── Step 2: Download file ────────────────────────────────────────

            await apiClient.ReportProgressAsync(job.DocumentId, "Downloading", 0, 0, 0, ct);

            var fileResult = await apiClient.DownloadFileAsync(
                job.DocumentId, claim.FileExtension, claim.FileName, claim.DocumentType, ct);

            // ── Step 3: Extract ──────────────────────────────────────────────

            await apiClient.ReportProgressAsync(job.DocumentId, "Extracting", 0, 0, 0, ct);

            var text = await extraction.ExtractTextAsync(fileResult.Content, fileResult.FileExtension, ct);

            if (string.IsNullOrWhiteSpace(text))
            {
                await apiClient.MarkFailedAsync(job.DocumentId, "Document contains no readable text.", ct);
                return;
            }

            // ── Step 4: Submit extracted text (API runs pipeline validation) ─

            var structuredDataReady = string.Equals(claim.DocumentType, "Excel", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(claim.DocumentType, "Csv",   StringComparison.OrdinalIgnoreCase);

            var extractResult = await apiClient.SubmitExtractedTextAsync(
                job.DocumentId, text, structuredDataReady, ct);

            if (extractResult.Blocked)
            {
                logger.LogWarning(
                    "[DOC-WORKER] DocumentId={DocumentId} blocked by pipeline — Reason={Reason}",
                    job.DocumentId, extractResult.Reason);
                return;
            }

            // ── Step 5: Get embedding model ──────────────────────────────────

            var embeddingModel = await apiClient.GetEmbeddingModelAsync(ct);

            if (string.IsNullOrWhiteSpace(embeddingModel.ModelId))
            {
                await apiClient.MarkFailedAsync(job.DocumentId, "No enabled embedding model configured.", ct);
                return;
            }

            // ── Step 6: Chunk ────────────────────────────────────────────────

            await apiClient.ReportProgressAsync(job.DocumentId, "Chunking", 0, 0, 0, ct);

            var chunks    = EndpointHelpers.CreateDocumentChunks(job.DocumentId, text);
            var chunkDtos = chunks.Select(c => new ChunkDto
            {
                Id         = c.Id,
                ChunkIndex = c.ChunkIndex,
                Content    = c.Content,
                PageNumber = c.PageNumber,
                Heading    = c.Heading
            }).ToList();

            await apiClient.SubmitChunksAsync(job.DocumentId, chunkDtos, ct);

            // ── Step 7: Embed ────────────────────────────────────────────────

            await apiClient.ReportProgressAsync(job.DocumentId, "Embedding", 0, chunks.Count, 0, ct);

            var embeddingDtos      = new List<ChunkEmbeddingDto>();
            var totalTokens        = 0;
            var embeddingStartTime = DateTime.UtcNow;

            for (var i = 0; i < chunks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var chunk = chunks[i];
                if (string.IsNullOrWhiteSpace(chunk.Content)) continue;

                var embResult = await embedding.CreateEmbeddingAsync(
                    chunk.Content, embeddingModel.ProviderType, embeddingModel.ModelId, ct);

                if (embResult.Embedding.Length > 0)
                {
                    embeddingDtos.Add(new ChunkEmbeddingDto
                    {
                        ChunkId    = chunk.Id,
                        Vector     = embResult.Embedding,
                        Dimensions = embResult.Embedding.Length
                    });
                    totalTokens += embResult.InputTokens;
                }

                var pct = chunks.Count > 0 ? Math.Round((i + 1.0) / chunks.Count * 100, 1) : 0;
                await apiClient.ReportProgressAsync(job.DocumentId, "Embedding", i + 1, chunks.Count, pct, ct);
            }

            var embeddingDurationMs = (long)(DateTime.UtcNow - embeddingStartTime).TotalMilliseconds;

            logger.LogInformation(
                "[DOC-WORKER] DocumentId={DocumentId} Embedding done — {Emb}/{Total} chunks embedded, DurationMs={DurationMs}",
                job.DocumentId, embeddingDtos.Count, chunks.Count, embeddingDurationMs);

            // ── Step 8: Submit embeddings to API (API writes DB + Qdrant) ────

            await apiClient.SubmitEmbeddingsAsync(
                job.DocumentId,
                embeddingModel.ProviderType,
                embeddingModel.ModelId,
                embeddingDtos,
                totalTokens,
                embeddingDurationMs,
                ct);

            // ── Step 9: Complete ─────────────────────────────────────────────

            var durationMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
            await apiClient.MarkCompleteAsync(job.DocumentId, chunks.Count, embeddingDtos.Count, durationMs, ct);

            logger.LogInformation(
                "[DOC-WORKER] DocumentId={DocumentId} Status=Complete Chunks={Chunks} Embeddings={Emb} DurationMs={Ms}",
                job.DocumentId, chunks.Count, embeddingDtos.Count, durationMs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[DOC-WORKER-FAILED] DocumentId={DocumentId} DurationMs={Ms}",
                job.DocumentId, (long)(DateTime.UtcNow - startTime).TotalMilliseconds);

            await apiClient.MarkFailedAsync(job.DocumentId, ex.Message, ct);
        }
    }
}
