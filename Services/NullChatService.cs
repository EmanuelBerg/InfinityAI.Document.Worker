using InfinityAI.Models.Database;
using InfinityAI.Api.Models.Database;
using InfinityAI.Api.Models.Gateway;
using InfinityAI.Api.Services;

namespace InfinityAI.Api.Services;

/// <summary>
/// Satisfies the IAIChatService dependency of LlmRouter without pulling in InfinityAIGatewayService
/// (which requires IWebHostEnvironment). The document worker never sends chat messages.
/// </summary>
internal sealed class NullChatService : IAIChatService
{
    public Task<string> SendAsync(List<ChatMessage> messages, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Chat is not available in InfinityAI.Document.Worker.");

    public Task<string> SendAsync(List<ChatMessage> messages, IEnumerable<ApplicationFile> files, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Chat is not available in InfinityAI.Document.Worker.");

    public Task<GatewayExecutionResult> SendAsync(List<ChatMessage> messages, List<ApplicationFile> imageFiles, string providerType, string modelId, CancellationToken ct)
        => throw new NotSupportedException("Chat is not available in InfinityAI.Document.Worker.");
}
