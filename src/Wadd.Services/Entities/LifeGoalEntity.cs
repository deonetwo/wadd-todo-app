using SQLite;
using Wadd.Core.Models;

namespace Wadd.Services.Entities;

[Table("LifeGoal")]
public class LifeGoalEntity
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Category { get; set; } = string.Empty;

    public DateTime? TargetDate { get; set; }

    public bool IsAchieved { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    [Indexed]
    public int OrderIndex { get; set; }

    [Indexed]
    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    public LifeGoal ToDomain()
    {
        return new LifeGoal
        {
            Id = Id,
            Title = Title,
            Description = Description,
            Category = Category,
            TargetDate = TargetDate,
            IsAchieved = IsAchieved,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            OrderIndex = OrderIndex,
            IsDeleted = IsDeleted,
            DeletedAt = DeletedAt
        };
    }

    public static LifeGoalEntity FromDomain(LifeGoal item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new LifeGoalEntity
        {
            Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString() : item.Id,
            Title = item.Title,
            Description = item.Description,
            Category = item.Category ?? string.Empty,
            TargetDate = item.TargetDate,
            IsAchieved = item.IsAchieved,
            CreatedAt = item.CreatedAt == default ? DateTime.UtcNow : item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            OrderIndex = item.OrderIndex,
            IsDeleted = item.IsDeleted,
            DeletedAt = item.DeletedAt
        };
    }
}
