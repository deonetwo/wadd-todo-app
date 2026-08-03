using System.Text.Json;
using Wadd.Core.Models;

namespace Wadd.Services;

public class FieldMergeResult<T>
{
    public bool HasConflict { get; set; }
    public T MergedItem { get; set; } = default!;
    public List<string> ConflictingFields { get; set; } = new();
}

public class ConflictResolutionEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public FieldMergeResult<TodoItem> MergeTodoItems(TodoItem local, TodoItem cloud, TodoItem? baseItem)
    {
        var result = new FieldMergeResult<TodoItem>
        {
            MergedItem = new TodoItem
            {
                Id = local.Id,
                CreatedAt = local.CreatedAt < cloud.CreatedAt ? local.CreatedAt : cloud.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                Version = Math.Max(local.Version, cloud.Version) + 1,
                IsDeleted = local.IsDeleted || cloud.IsDeleted
            }
        };

        var target = result.MergedItem;

        // If one is deleted, mark soft delete
        if (local.IsDeleted != cloud.IsDeleted)
        {
            // If baseItem is present, check who deleted it
            if (baseItem != null)
            {
                target.IsDeleted = local.IsDeleted ? local.IsDeleted : cloud.IsDeleted;
            }
            else
            {
                target.IsDeleted = local.IsDeleted || cloud.IsDeleted;
            }
        }

        // Field 1: Title
        if (local.Title == cloud.Title)
        {
            target.Title = local.Title;
        }
        else if (baseItem != null && local.Title == baseItem.Title)
        {
            target.Title = cloud.Title;
        }
        else if (baseItem != null && cloud.Title == baseItem.Title)
        {
            target.Title = local.Title;
        }
        else
        {
            result.HasConflict = true;
            result.ConflictingFields.Add(nameof(TodoItem.Title));
            target.Title = local.Title; // fallback default
        }

        // Field 2: Description
        if (local.Description == cloud.Description)
        {
            target.Description = local.Description;
        }
        else if (baseItem != null && local.Description == baseItem.Description)
        {
            target.Description = cloud.Description;
        }
        else if (baseItem != null && cloud.Description == baseItem.Description)
        {
            target.Description = local.Description;
        }
        else
        {
            result.HasConflict = true;
            result.ConflictingFields.Add(nameof(TodoItem.Description));
            target.Description = local.Description;
        }

        // Field 3: IsCompleted
        if (local.IsCompleted == cloud.IsCompleted)
        {
            target.IsCompleted = local.IsCompleted;
        }
        else if (baseItem != null && local.IsCompleted == baseItem.IsCompleted)
        {
            target.IsCompleted = cloud.IsCompleted;
        }
        else if (baseItem != null && cloud.IsCompleted == baseItem.IsCompleted)
        {
            target.IsCompleted = local.IsCompleted;
        }
        else
        {
            result.HasConflict = true;
            result.ConflictingFields.Add(nameof(TodoItem.IsCompleted));
            target.IsCompleted = local.IsCompleted;
        }

        // Field 4: Priority
        if (local.Priority == cloud.Priority)
        {
            target.Priority = local.Priority;
        }
        else if (baseItem != null && local.Priority == baseItem.Priority)
        {
            target.Priority = cloud.Priority;
        }
        else if (baseItem != null && cloud.Priority == baseItem.Priority)
        {
            target.Priority = local.Priority;
        }
        else
        {
            result.HasConflict = true;
            result.ConflictingFields.Add(nameof(TodoItem.Priority));
            target.Priority = local.Priority;
        }

        // Field 5: DueDate
        if (local.DueDate == cloud.DueDate)
        {
            target.DueDate = local.DueDate;
        }
        else if (baseItem != null && local.DueDate == baseItem.DueDate)
        {
            target.DueDate = cloud.DueDate;
        }
        else if (baseItem != null && cloud.DueDate == baseItem.DueDate)
        {
            target.DueDate = local.DueDate;
        }
        else
        {
            result.HasConflict = true;
            result.ConflictingFields.Add(nameof(TodoItem.DueDate));
            target.DueDate = local.DueDate;
        }

        // Field 6: ReminderAt
        if (local.ReminderAt == cloud.ReminderAt)
        {
            target.ReminderAt = local.ReminderAt;
        }
        else if (baseItem != null && local.ReminderAt == baseItem.ReminderAt)
        {
            target.ReminderAt = cloud.ReminderAt;
        }
        else if (baseItem != null && cloud.ReminderAt == baseItem.ReminderAt)
        {
            target.ReminderAt = local.ReminderAt;
        }
        else
        {
            result.HasConflict = true;
            result.ConflictingFields.Add(nameof(TodoItem.ReminderAt));
            target.ReminderAt = local.ReminderAt;
        }

        // Field 7: IsRecurring
        target.IsRecurring = local.IsRecurring == cloud.IsRecurring
            ? local.IsRecurring
            : (baseItem != null && local.IsRecurring == baseItem.IsRecurring ? cloud.IsRecurring : local.IsRecurring);

        // Field 8: RecurrenceType
        target.RecurrenceType = local.RecurrenceType == cloud.RecurrenceType
            ? local.RecurrenceType
            : (baseItem != null && local.RecurrenceType == baseItem.RecurrenceType ? cloud.RecurrenceType : local.RecurrenceType);

        // Field 9: CustomRecurrenceInterval
        target.CustomRecurrenceInterval = local.CustomRecurrenceInterval == cloud.CustomRecurrenceInterval
            ? local.CustomRecurrenceInterval
            : (baseItem != null && local.CustomRecurrenceInterval == baseItem.CustomRecurrenceInterval ? cloud.CustomRecurrenceInterval : local.CustomRecurrenceInterval);

        // Field 10: CustomRecurrenceUnit
        target.CustomRecurrenceUnit = local.CustomRecurrenceUnit == cloud.CustomRecurrenceUnit
            ? local.CustomRecurrenceUnit
            : (baseItem != null && local.CustomRecurrenceUnit == baseItem.CustomRecurrenceUnit ? cloud.CustomRecurrenceUnit : local.CustomRecurrenceUnit);

        // Field 11: CustomWeeklyDays
        target.CustomWeeklyDays = local.CustomWeeklyDays == cloud.CustomWeeklyDays
            ? local.CustomWeeklyDays
            : (baseItem != null && local.CustomWeeklyDays == baseItem.CustomWeeklyDays ? cloud.CustomWeeklyDays : local.CustomWeeklyDays);

        return result;
    }
}
