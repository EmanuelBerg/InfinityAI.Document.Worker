namespace InfinityAI.Document.Worker.Services;

public interface IDocumentExtractionService
{
    Task<string> ExtractTextAsync(byte[] content, string fileExtension, CancellationToken ct);
}
