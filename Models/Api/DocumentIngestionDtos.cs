namespace InfinityAI.Document.Worker.Models.Api;

// ── Claim ─────────────────────────────────────────────────────────────────────

public sealed class ClaimJobRequest
{
    public Guid   DocumentId  { get; init; }
    public string LockId      { get; init; } = "";
    public string MachineName { get; init; } = "";
}

public sealed class ClaimJobResponse
{
    public bool   Claimed      { get; init; }
    public string Reason       { get; init; } = "";
    public int    AttemptCount { get; init; }
    public string FileName     { get; init; } = "";
    public string DocumentType { get; init; } = "";
    public string FileExtension { get; init; } = "";
    public Guid?  UserId       { get; init; }
}

// ── File download ──────────────────────────────────────────────────────────────

public sealed class FileDownloadResult
{
    public byte[] Content      { get; init; } = [];
    public string FileExtension { get; init; } = "";
    public string FileName     { get; init; } = "";
    public string DocumentType { get; init; } = "";
}

// ── Progress ──────────────────────────────────────────────────────────────────

public sealed class ReportProgressRequest
{
    public string Stage   { get; init; } = "";
    public int    Current { get; init; }
    public int    Total   { get; init; }
    public double Percent { get; init; }
}

// ── Extracted text ────────────────────────────────────────────────────────────

public sealed class ExtractedTextRequest
{
    public string ExtractedText       { get; init; } = "";
    public bool   StructuredDataReady { get; init; }
}

public sealed class ExtractedTextResponse
{
    public bool   Blocked { get; init; }
    public string Reason  { get; init; } = "";
}

// ── Embedding model ───────────────────────────────────────────────────────────

public sealed class EmbeddingModelResponse
{
    public string ProviderType { get; init; } = "";
    public string ModelId      { get; init; } = "";
}

// ── Chunks ────────────────────────────────────────────────────────────────────

public sealed class ChunkDto
{
    public Guid    Id         { get; init; }
    public int     ChunkIndex { get; init; }
    public string  Content    { get; init; } = "";
    public int?    PageNumber { get; init; }
    public string? Heading    { get; init; }
}

public sealed class SubmitChunksRequest
{
    public List<ChunkDto> Chunks { get; init; } = [];
}

// ── Embeddings ────────────────────────────────────────────────────────────────

public sealed class ChunkEmbeddingDto
{
    public Guid    ChunkId    { get; init; }
    public float[] Vector     { get; init; } = [];
    public int     Dimensions { get; init; }
}

public sealed class SubmitEmbeddingsRequest
{
    public string                ProviderType        { get; init; } = "";
    public string                ModelId             { get; init; } = "";
    public int                   TotalInputTokens    { get; init; }
    public long                  EmbeddingDurationMs { get; init; }
    public List<ChunkEmbeddingDto> Embeddings        { get; init; } = [];
}

// ── Complete / Failed ─────────────────────────────────────────────────────────

public sealed class CompleteRequest
{
    public int  TotalChunks     { get; init; }
    public int  TotalEmbeddings { get; init; }
    public long DurationMs      { get; init; }
}

public sealed class FailedRequest
{
    public string ErrorMessage { get; init; } = "";
}
