using InfinityAI.Data;
using InfinityAI.Api.Models.ImageGeneration;
using InfinityAI.Api.Models.Ingestion;
using InfinityAI.Api.Models.Qdrant;
using InfinityAI.Api.Services;
using InfinityAI.Api.Services.Llm;
using InfinityAI.Api.Pipeline;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// ── Logging ───────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore",                  LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient",                     LogLevel.Warning);
builder.Logging.SetMinimumLevel(LogLevel.Information);

// ── Database ──────────────────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

builder.Services.AddDbContext<ApplicationDbContext>(opts =>
    opts.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// ── Options ───────────────────────────────────────────────────────────────────
builder.Services.Configure<DocumentIngestionOptions>(
    builder.Configuration.GetSection("DocumentIngestion"));
builder.Services.Configure<QdrantOptions>(
    builder.Configuration.GetSection("Qdrant"));
builder.Services.Configure<ImageGenerationOptions>(
    builder.Configuration.GetSection("ImageGeneration"));

// ── HTTP clients ──────────────────────────────────────────────────────────────
// EmbeddingService calls the Gateway for vector creation.
builder.Services.AddHttpClient<IEmbeddingService, EmbeddingService>();
// QdrantVectorStore calls the Qdrant REST API.
builder.Services.AddHttpClient<IQdrantVectorStore, QdrantVectorStore>();
// SignalRNotificationClient sends progress updates to the SignalR hub.
builder.Services.AddHttpClient<SignalRNotificationClient>();
// IHttpClientFactory is used by PipelineOrchestrator to call pipeline component endpoints.
builder.Services.AddHttpClient();

// ── Stubs for services not used in document ingestion ────────────────────────
// LlmRouter requires IAIChatService and IImageGenerationService in its constructor.
// Neither is called during document ingestion — null stubs satisfy the dependency graph.
builder.Services.AddSingleton<IAIChatService, NullChatService>();
builder.Services.AddSingleton<IImageGenerationService, NullImageGenerationService>();

// ── Core document-ingestion services ─────────────────────────────────────────
builder.Services.AddScoped<IDocumentExtractionService, DocumentExtractionService>();
builder.Services.AddScoped<IAiRequestLogService, AiRequestLogService>();
builder.Services.AddScoped<IModelSelectionService, ModelSelectionService>();
builder.Services.AddScoped<ILlmRouter, LlmRouter>();
builder.Services.AddScoped<IEmbeddingBatchService, EmbeddingBatchService>();
builder.Services.AddScoped<PipelineOrchestrator>();

// ── Worker ────────────────────────────────────────────────────────────────────
builder.Services.AddHostedService<DocumentIngestionWorker>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("[DOCUMENT-WORKER] Starting. Queue={Queue} Mode=ExternalWorker",
    builder.Configuration.GetValue<string>("DocumentIngestion:QueueName") ?? "document-ingestion");

host.Run();
