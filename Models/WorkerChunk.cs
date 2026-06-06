namespace InfinityAI.Document.Worker.Models;

public sealed class WorkerChunk
{
    public Guid    Id             { get; init; }
    public Guid    DocumentId     { get; init; }
    public int     ChunkIndex     { get; init; }
    public string  Content        { get; init; } = "";
    public int?    PageNumber     { get; init; }
    public string? Heading        { get; init; }
    public int     CharacterCount { get; init; }
    public int     TokenCount     { get; init; }
}
