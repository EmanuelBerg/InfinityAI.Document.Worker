using InfinityAI.Document.Worker.Models.Api;
using System.Net.Http.Json;

namespace InfinityAI.Document.Worker.Clients;

public sealed class DocumentIngestionApiClient
{
    private readonly HttpClient _http;
    private readonly string _internalKey;
    private readonly ILogger<DocumentIngestionApiClient> _logger;

    public DocumentIngestionApiClient(
        HttpClient http,
        IConfiguration config,
        ILogger<DocumentIngestionApiClient> logger)
    {
        _http   = http;
        _logger = logger;

        var baseUrl = config["Workers:InternalApiBaseUrl"]
            ?? "http://infinityai-api:8080";

        _http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _http.Timeout     = TimeSpan.FromMinutes(5);

        _internalKey = config["Workers:InternalKey"] ?? "";
    }

    // ── Claim ─────────────────────────────────────────────────────────────────

    public async Task<ClaimJobResponse> ClaimJobAsync(
        Guid documentId, string lockId, string machineName, CancellationToken ct)
    {
        using var req = BuildPost("internal/document-ingestion/jobs/claim",
            new ClaimJobRequest { DocumentId = documentId, LockId = lockId, MachineName = machineName });

        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ClaimJobResponse>(ct)
            ?? throw new InvalidOperationException("Empty claim response from API.");
    }

    // ── File download ─────────────────────────────────────────────────────────

    public async Task<FileDownloadResult> DownloadFileAsync(
        Guid documentId, string fileExtension, string fileName, string documentType, CancellationToken ct)
    {
        using var req  = BuildGet($"internal/document-ingestion/documents/{documentId}/file");
        var resp = await _http.SendAsync(req, ct);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new FileNotFoundException($"File for document {documentId} not found on API server.");

        resp.EnsureSuccessStatusCode();

        var content = await resp.Content.ReadAsByteArrayAsync(ct);

        return new FileDownloadResult
        {
            Content       = content,
            FileExtension = fileExtension,
            FileName      = fileName,
            DocumentType  = documentType
        };
    }

    // ── Progress ──────────────────────────────────────────────────────────────

    public async Task ReportProgressAsync(
        Guid documentId, string stage, int current, int total, double percent, CancellationToken ct)
    {
        try
        {
            using var req = BuildPost($"internal/document-ingestion/documents/{documentId}/progress",
                new ReportProgressRequest { Stage = stage, Current = current, Total = total, Percent = percent });
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("[DOC-WORKER] Progress report returned {Status} for DocumentId={Id}", resp.StatusCode, documentId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DOC-WORKER] Failed to report progress for DocumentId={Id} — continuing", documentId);
        }
    }

    // ── Extracted text ────────��───────────────────────────────────────────────

    public async Task<ExtractedTextResponse> SubmitExtractedTextAsync(
        Guid documentId, string extractedText, bool structuredDataReady, CancellationToken ct)
    {
        using var req = BuildPost($"internal/document-ingestion/documents/{documentId}/extracted-text",
            new ExtractedTextRequest { ExtractedText = extractedText, StructuredDataReady = structuredDataReady });
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ExtractedTextResponse>(ct)
            ?? new ExtractedTextResponse { Blocked = false };
    }

    // ── Embedding model ───────────────────────────────────────────────────────

    public async Task<EmbeddingModelResponse> GetEmbeddingModelAsync(CancellationToken ct)
    {
        using var req  = BuildGet("internal/document-ingestion/embedding-model");
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<EmbeddingModelResponse>(ct)
            ?? throw new InvalidOperationException("Empty embedding-model response from API.");
    }

    // ── Chunks ────────────────────────────────────────────────────────────────

    public async Task SubmitChunksAsync(Guid documentId, List<ChunkDto> chunks, CancellationToken ct)
    {
        using var req = BuildPost($"internal/document-ingestion/documents/{documentId}/chunks",
            new SubmitChunksRequest { Chunks = chunks });
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    // ── Embeddings ────────────────────────────────────────────────────────────

    public async Task SubmitEmbeddingsAsync(
        Guid documentId,
        string providerType,
        string modelId,
        List<ChunkEmbeddingDto> embeddings,
        int totalInputTokens,
        long embeddingDurationMs,
        CancellationToken ct)
    {
        using var req = BuildPost($"internal/document-ingestion/documents/{documentId}/embeddings",
            new SubmitEmbeddingsRequest
            {
                ProviderType        = providerType,
                ModelId             = modelId,
                TotalInputTokens    = totalInputTokens,
                EmbeddingDurationMs = embeddingDurationMs,
                Embeddings          = embeddings
            });
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    // ── Complete ──────────────────────────────────────────────────────────────

    public async Task MarkCompleteAsync(Guid documentId, int totalChunks, int totalEmbeddings, long durationMs, CancellationToken ct)
    {
        using var req = BuildPost($"internal/document-ingestion/documents/{documentId}/complete",
            new CompleteRequest { TotalChunks = totalChunks, TotalEmbeddings = totalEmbeddings, DurationMs = durationMs });
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    // ── Failed ────────────────────────────────────────────────────────────────

    public async Task MarkFailedAsync(Guid documentId, string errorMessage, CancellationToken ct)
    {
        try
        {
            using var req = BuildPost($"internal/document-ingestion/documents/{documentId}/failed",
                new FailedRequest { ErrorMessage = errorMessage });
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                _logger.LogWarning("[DOC-WORKER] MarkFailed returned {Status} for DocumentId={Id}", resp.StatusCode, documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DOC-WORKER] Failed to mark document {DocumentId} as failed via API", documentId);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpRequestMessage BuildPost<T>(string path, T body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Content = JsonContent.Create(body);
        if (!string.IsNullOrWhiteSpace(_internalKey))
            req.Headers.Add("X-InfinityAI-Internal-Key", _internalKey);
        return req;
    }

    private HttpRequestMessage BuildGet(string path)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(_internalKey))
            req.Headers.Add("X-InfinityAI-Internal-Key", _internalKey);
        return req;
    }
}
