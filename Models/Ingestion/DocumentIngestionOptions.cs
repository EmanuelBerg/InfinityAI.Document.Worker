namespace InfinityAI.Document.Worker.Models;

public sealed class DocumentIngestionOptions
{
    public string Mode                                  { get; set; } = "ExternalWorker";
    public bool   Enabled                              { get; set; } = true;
    public string QueueName                            { get; set; } = "document-ingestion";
    public int    EmbeddingBatchSize                   { get; set; } = 32;
    public int    MaxAttempts                          { get; set; } = 3;
    public int    ProcessingLockMinutes                { get; set; } = 30;
    public long   LargeFileThresholdBytes              { get; set; } = 10_485_760;
    public bool   EnableStructuredReadyBeforeEmbeddings { get; set; } = true;
}
