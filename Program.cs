using InfinityAI.Document.Worker.Clients;
using InfinityAI.Document.Worker.Models;
using InfinityAI.Document.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

// ── Logging ───────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Logging.SetMinimumLevel(LogLevel.Information);

// ── Options ───────────────────────────────────────────────────────────────────
builder.Services.Configure<DocumentIngestionOptions>(
    builder.Configuration.GetSection("DocumentIngestion"));

// ── HTTP clients ──────────────────────────────────────────────────────────────
// API client — all internal document-ingestion endpoints
builder.Services.AddHttpClient<DocumentIngestionApiClient>();
// EmbeddingService — calls InfinityAI Gateway for vector creation
builder.Services.AddHttpClient<IEmbeddingService, EmbeddingService>();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IDocumentExtractionService, DocumentExtractionService>();

// ── Worker ────────────────────────────────────────────────────────────────────
builder.Services.AddHostedService<DocumentIngestionWorker>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("[DOCUMENT-WORKER] Starting. Queue={Queue}",
    builder.Configuration.GetValue<string>("DocumentIngestion:QueueName") ?? "document-ingestion");

host.Run();
