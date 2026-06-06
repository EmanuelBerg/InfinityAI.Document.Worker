using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using InfinityAI.Api.Models.Llm;

namespace InfinityAI.Api.Models.Database;

public sealed class Document
{
    public Guid Id { get; set; }

    public Guid? FileId { get; set; }
    public ApplicationFile? File { get; set; }

    public Guid? StoredFileId { get; set; }
    public StoredFile? StoredFile { get; set; }

    public Guid? UserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public string Status { get; set; } = "Uploaded";

    public string? ExtractedText { get; set; }
    public string? ErrorMessage { get; set; }
    public string? BlockReason { get; set; }
    public Guid? MergedIntoDocumentId { get; set; }

    public string?  ProcessingStage  { get; set; }
    public int?     ProgressCurrent  { get; set; }
    public int?     ProgressTotal    { get; set; }
    public double?  ProgressPercent  { get; set; }

    public DateTime? QueuedAt              { get; set; }
    public DateTime? ProcessingStartedAt   { get; set; }
    public DateTime? ProcessingCompletedAt { get; set; }

    public int     ProcessingAttemptCount { get; set; }
    public string? LastProcessingWorker   { get; set; }

    public string?   ProcessingLockId        { get; set; }
    public DateTime? ProcessingLockExpiresAt { get; set; }

    public bool StructuredDataReady { get; set; }
    public bool RagIndexReady       { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }

    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();

    [NotMapped]
    public DocumentStatus StatusEnum
    {
        get => Enum.TryParse<DocumentStatus>(Status, out var s) ? s : DocumentStatus.Uploaded;
        set => Status = value.ToString();
    }

    [NotMapped]
    public bool IsProcessing => StatusEnum is
        DocumentStatus.Queued     or
        DocumentStatus.Processing or
        DocumentStatus.Extracting or
        DocumentStatus.Chunking   or
        DocumentStatus.Embedding  or
        DocumentStatus.Indexing;
}

public enum DocumentStatus
{
    Uploaded, Queued, Processing, Extracting, Chunking, Embedding, Indexing, Ready, Failed, Blocked
}

public sealed class DocumentChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public int? PageNumber { get; set; }
    public string? Heading { get; set; }
    public int? CharacterCount { get; set; }
    public int? TokenCount { get; set; }
    public DateTime CreatedUtc { get; set; }
    public List<DocumentChunkEmbedding> Embeddings { get; set; } = [];
}

public class DocumentChunkEmbedding
{
    public Guid Id { get; set; }
    public Guid DocumentChunkId { get; set; }
    public DocumentChunk DocumentChunk { get; set; } = null!;
    public string EmbeddingProvider { get; set; } = "";
    public string Model { get; set; } = "";
    public string VectorJson { get; set; } = "";
    public int Dimensions { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class ApplicationFile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ConversationId { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public Guid? StoredFileId { get; set; }
    public StoredFile? StoredFile { get; set; }
    public string Source { get; set; } = "Uploaded";
    public DateTime CreatedUtc { get; set; }
}

public sealed class StoredFile
{
    public Guid Id { get; set; }
    public string Sha256Hash { get; set; } = "";
    public long Size { get; set; }
    public string StoragePath { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string FileExtension { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
}

public class AiRequestLog
{
    public long Id { get; set; }
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? Caller { get; set; }
    public string? UserId { get; set; }
    public string? CustomerId { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public long DurationMs { get; set; }
    public bool Success { get; set; }
    public int? StatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RequestPreview { get; set; }
    public string? ResponsePreview { get; set; }
    public Guid? LlmModelId { get; set; }
    public Guid? AiProfileId { get; set; }
    public LlmCapability? Capability { get; set; }
    public int? SelectionScore { get; set; }
    public string? FailureType { get; set; }
    public int? ProviderStatusCode { get; set; }
    public string? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public int? RetryCount { get; set; }
}

public class AiProfileProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AiProfileId { get; set; }
    public AiProfile AiProfile { get; set; } = null!;
    public Guid LlmProviderId { get; set; }
    public LlmProvider LlmProvider { get; set; } = null!;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PipelineComponentSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int Order { get; set; } = 10;
    public int TimeoutSeconds { get; set; } = 5;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public class LlmProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string? BaseUrl { get; set; }
    public string? ApiKeySecretName { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public ICollection<LlmModel> Models { get; set; } = new List<LlmModel>();
}

public class LlmModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LlmProviderId { get; set; }
    public LlmProvider? LlmProvider { get; set; }
    public string ModelId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool IsDefaultChatModel { get; set; }
    public bool IsDefaultEmbeddingModel { get; set; }
    public bool SupportsChat { get; set; } = true;
    public bool SupportsVision { get; set; }
    public bool SupportsEmbeddings { get; set; }
    public bool SupportsStreaming { get; set; }
    public bool SupportsImageGeneration { get; set; }
    public bool SupportsAudio { get; set; }
    public bool IsDefaultImageModel { get; set; }
    public int? ContextWindowTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
    public decimal? InputCostPer1M { get; set; }
    public decimal? OutputCostPer1M { get; set; }
    public decimal? EmbeddingCostPer1M { get; set; }
    public decimal? ImageCostPerGeneration { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MaintenanceJob
{
    public Guid                  Id                { get; set; }
    public MaintenanceJobType    JobType           { get; set; }
    public MaintenanceJobStatus  Status            { get; set; } = MaintenanceJobStatus.Pending;
    public Guid?                 RequestedByUserId { get; set; }
    public DateTime?             StartedUtc        { get; set; }
    public DateTime?             CompletedUtc      { get; set; }
    public string?               ResultSummary     { get; set; }
    public string?               ErrorMessage      { get; set; }
    public DateTime              CreatedUtc        { get; set; } = DateTime.UtcNow;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MaintenanceJobType
{
    OrphanFileScan, OrphanFileCleanup, OrphanDocumentScan, OrphanDocumentCleanup,
    OrphanQdrantScan, OrphanQdrantCleanup, SessionCleanup, LegacyHashBackfill,
    ReindexDocument, ReindexCollection, RebuildKnowledgeCollectionPermissions
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MaintenanceJobStatus { Pending, Running, Completed, Failed }

public sealed class MaintenanceWorkerHeartbeat
{
    public string   WorkerName    { get; set; } = "default";
    public DateTime LastSeenUtc   { get; set; }
    public string   CurrentStatus { get; set; } = "Idle";
    public Guid?    CurrentJobId  { get; set; }
}
