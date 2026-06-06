using InfinityAI.Api.Models.ImageGeneration;
using InfinityAI.Api.Services;

namespace InfinityAI.Api.Services;

/// <summary>
/// Satisfies the IImageGenerationService dependency of LlmRouter without registering ImageGenerationService
/// (which requires IWebHostEnvironment). The document worker never generates images.
/// </summary>
internal sealed class NullImageGenerationService : IImageGenerationService
{
    public Task<ImageGenerationAttemptResult> TryGenerateAsync(
        Guid userId,
        Guid conversationId,
        string prompt,
        string providerType,
        string modelId,
        CancellationToken ct)
        => throw new NotSupportedException("Image generation is not available in InfinityAI.Document.Worker.");
}
