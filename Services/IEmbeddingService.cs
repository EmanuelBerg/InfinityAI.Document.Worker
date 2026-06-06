namespace InfinityAI.Document.Worker.Services;

public sealed class EmbeddingExecutionResult
{
    public float[] Embedding   { get; init; } = [];
    public int     InputTokens { get; init; }
}

public interface IEmbeddingService
{
    Task<EmbeddingExecutionResult> CreateEmbeddingAsync(
        string input,
        string providerType,
        string modelId,
        CancellationToken ct);
}
