namespace Msgifly.Web.Models.Entities;

/// <summary>Reusable AI-instruction preset used to generate chat reply suggestions.</summary>
public class AiPrompt
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public int? AddedFromId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
