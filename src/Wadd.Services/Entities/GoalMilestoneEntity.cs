using SQLite;
using Wadd.Core.Models;

namespace Wadd.Services.Entities;

[Table("GoalMilestone")]
public class GoalMilestoneEntity
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [Indexed]
    public string GoalId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }

    public int OrderIndex { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Indexed]
    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    public GoalMilestone ToDomain()
    {
        return new GoalMilestone
        {
            Id = Id,
            GoalId = GoalId,
            Title = Title,
            IsCompleted = IsCompleted,
            OrderIndex = OrderIndex,
            UpdatedAt = UpdatedAt,
            IsDeleted = IsDeleted,
            DeletedAt = DeletedAt
        };
    }

    public static GoalMilestoneEntity FromDomain(GoalMilestone item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new GoalMilestoneEntity
        {
            Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString() : item.Id,
            GoalId = item.GoalId,
            Title = item.Title,
            IsCompleted = item.IsCompleted,
            OrderIndex = item.OrderIndex,
            UpdatedAt = item.UpdatedAt,
            IsDeleted = item.IsDeleted,
            DeletedAt = item.DeletedAt
        };
    }
}
