using InfinityAI.Api.Models.Database;

namespace InfinityAI.Api.Models.Llm;

public enum LlmCapability { Chat = 1, Vision = 2, Embeddings = 3, ImageGeneration = 4, Audio = 5 }

public class AiProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int CostWeight { get; set; }
    public int SpeedWeight { get; set; }
    public int QualityWeight { get; set; }
    public bool IsDefault { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public ICollection<AiProfileProvider> AllowedProviders { get; set; } = new List<AiProfileProvider>();
}

public class AiSettings
{
    public Guid Id { get; set; }
    public Guid? ActiveAiProfileId { get; set; }
    public AiProfile? ActiveAiProfile { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public class LlmModelCapabilityMetric
{
    public Guid Id { get; set; }
    public Guid LlmModelId { get; set; }
    public LlmModel LlmModel { get; set; } = default!;
    public LlmCapability Capability { get; set; }
    public int CostScore { get; set; }
    public int SpeedScore { get; set; }
    public int QualityScore { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed record LlmPricingContext
{
    public decimal? InputCostPer1M { get; init; }
    public decimal? OutputCostPer1M { get; init; }
    public decimal? EmbeddingCostPer1M { get; init; }
    public decimal? ImageCostPerGeneration { get; init; }
}

public sealed class ModelSelectionResult
{
    public required LlmModel Model { get; init; }
    public required AiProfile Profile { get; init; }
    public int SelectionScore { get; init; }
}
