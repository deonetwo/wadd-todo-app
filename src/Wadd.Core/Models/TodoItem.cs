using Wadd.Core.Enums;

namespace Wadd.Core.Models;

public class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }

    public DateTime? CompletedAt { get; set; }

    public TodoPriority Priority { get; set; } = TodoPriority.Medium;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DueDate { get; set; }

    public DateTime? ReminderAt { get; set; }

    public long Version { get; set; } = 1;

    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedAt { get; set; }

    public Guid? SeriesId { get; set; }

    public bool IsRecurring { get; set; } = false;

    public string RecurrenceType { get; set; } = "None";

    public int? CustomRecurrenceInterval { get; set; }

    public string? CustomRecurrenceUnit { get; set; }

    public string? CustomWeeklyDays { get; set; }

    public string? Category { get; set; }

    public List<string> CategoriesList => string.IsNullOrWhiteSpace(Category)
        ? new List<string>()
        : Category.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .ToList();
}
