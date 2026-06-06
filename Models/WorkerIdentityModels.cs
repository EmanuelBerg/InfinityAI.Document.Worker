using InfinityAI.Api.Models.Llm;

namespace InfinityAI.Api.Models.Database.Identity;

public class ApplicationUser
{
    public Guid Id { get; set; }
    public string EntraObjectId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public Guid? AiProfileId { get; set; }
    public AiProfile? AiProfile { get; set; }
}
