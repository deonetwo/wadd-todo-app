namespace Wadd.Core.Models;

public class JournalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string? GoalId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime EntryDate { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }
}
