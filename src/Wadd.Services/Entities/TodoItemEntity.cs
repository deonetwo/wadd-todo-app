using SQLite;
using Wadd.Core.Enums;
using Wadd.Core.Models;

namespace Wadd.Services.Entities;

[Table("TodoItem")]
public class TodoItemEntity
{
    [PrimaryKey]
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }

    public DateTime? CompletedAt { get; set; }

    public TodoPriority Priority { get; set; } = TodoPriority.Medium;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DueDate { get; set; }

    public DateTime? ReminderAt { get; set; }

    [Indexed]
    public long Version { get; set; } = 1;

    [Indexed]
    public bool IsDeleted { get; set; } = false;

    public bool IsRecurring { get; set; } = false;

    public string RecurrenceType { get; set; } = "None";

    public int? CustomRecurrenceInterval { get; set; }

    public string? CustomRecurrenceUnit { get; set; }

    public string? CustomWeeklyDays { get; set; }

    public string? Category { get; set; }

    public TodoItem ToDomain()
    {
        return new TodoItem
        {
            Id = Id,
            Title = Title,
            Description = Description,
            IsCompleted = IsCompleted,
            CompletedAt = CompletedAt,
            Priority = Priority,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            DueDate = DueDate,
            ReminderAt = ReminderAt,
            Version = Version,
            IsDeleted = IsDeleted,
            IsRecurring = IsRecurring,
            RecurrenceType = RecurrenceType,
            CustomRecurrenceInterval = CustomRecurrenceInterval,
            CustomRecurrenceUnit = CustomRecurrenceUnit,
            CustomWeeklyDays = CustomWeeklyDays,
            Category = Category
        };
    }

    public static TodoItemEntity FromDomain(TodoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new TodoItemEntity
        {
            Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
            Title = item.Title,
            Description = item.Description,
            IsCompleted = item.IsCompleted,
            CompletedAt = item.CompletedAt,
            Priority = item.Priority,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            DueDate = item.DueDate,
            ReminderAt = item.ReminderAt,
            Version = item.Version <= 0 ? 1 : item.Version,
            IsDeleted = item.IsDeleted,
            IsRecurring = item.IsRecurring,
            RecurrenceType = item.RecurrenceType,
            CustomRecurrenceInterval = item.CustomRecurrenceInterval,
            CustomRecurrenceUnit = item.CustomRecurrenceUnit,
            CustomWeeklyDays = item.CustomWeeklyDays,
            Category = item.Category
        };
    }
}
