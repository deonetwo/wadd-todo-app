namespace Wadd.Core.Models;

public class GoalMilestone
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string GoalId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }

    public int OrderIndex { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }
}
