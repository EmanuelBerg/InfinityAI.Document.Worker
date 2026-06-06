namespace InfinityAI.Document.Worker.Models;

public sealed class DocumentIngestionJob
{
    public Guid   DocumentId     { get; init; }
    public Guid?  FileId         { get; init; }
    public Guid?  StoredFileId   { get; init; }
    public Guid?  UserId         { get; init; }
    public Guid?  ConversationId { get; init; }
    public string FileName       { get; init; } = "";
    public string DocumentType   { get; init; } = "";
    public int    Attempt        { get; init; } = 1;
    public DateTime QueuedAtUtc  { get; init; } = DateTime.UtcNow;
}
