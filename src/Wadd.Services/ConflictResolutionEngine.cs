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

    /// <summary>
    /// Pure deterministic Last-Write-Wins (LWW) merge with Tombstone resolution.
    /// </summary>
    public static TodoItem MergeTask(TodoItem? local, TodoItem? remote)
    {
        if (local == null && remote == null)
            throw new ArgumentException("Both local and remote tasks cannot be null.");
        if (local == null) return Clone(remote!);
        if (remote == null) return Clone(local!);

        DateTime localUpdated = local.UpdatedAt ?? local.CreatedAt;
        DateTime remoteUpdated = remote.UpdatedAt ?? remote.CreatedAt;

        long localTicks = localUpdated.ToUniversalTime().Ticks;
        long remoteTicks = remoteUpdated.ToUniversalTime().Ticks;

        TodoItem result;
        if (remoteTicks > localTicks)
        {
            result = Clone(remote);
        }
        else if (localTicks > remoteTicks)
        {
            result = Clone(local);
        }
        else if (local.IsDeleted != remote.IsDeleted)
        {
            result = local.IsDeleted ? Clone(local) : Clone(remote);
        }
        else
        {
            string localKey = local.Title + (local.Description ?? "") + (local.Category ?? "");
            string remoteKey = remote.Title + (remote.Description ?? "") + (remote.Category ?? "");

            result = string.Compare(localKey, remoteKey, StringComparison.Ordinal) >= 0
                ? Clone(local)
                : Clone(remote);
        }

        if (result.IsCompleted)
        {
            result.CompletedAt ??= result.UpdatedAt ?? DateTime.UtcNow;
        }
        else
        {
            result.CompletedAt = null;
        }

        return result;
    }

    public FieldMergeResult<TodoItem> MergeTodoItems(TodoItem local, TodoItem cloud, TodoItem? baseItem)
    {
        if (baseItem == null)
        {
            var mergedLww = MergeTask(local, cloud);
            return new FieldMergeResult<TodoItem>
            {
                HasConflict = false,
                MergedItem = mergedLww,
                ConflictingFields = new List<string>()
            };
        }

        var result = new FieldMergeResult<TodoItem>
        {
            MergedItem = Clone(baseItem)
        };

        void MergeProperty<T>(string propertyName, Func<TodoItem, T> selector, Action<TodoItem, T> setter)
        {
            T localVal = selector(local);
            T cloudVal = selector(cloud);
            T baseVal = selector(baseItem);

            bool localChanged = !EqualityComparer<T>.Default.Equals(localVal, baseVal);
            bool cloudChanged = !EqualityComparer<T>.Default.Equals(cloudVal, baseVal);

            if (localChanged && cloudChanged)
            {
                if (EqualityComparer<T>.Default.Equals(localVal, cloudVal))
                {
                    setter(result.MergedItem, localVal);
                }
                else
                {
                    result.HasConflict = true;
                    result.ConflictingFields.Add(propertyName);
                    setter(result.MergedItem, localVal);
                }
            }
            else if (localChanged)
            {
                setter(result.MergedItem, localVal);
            }
            else if (cloudChanged)
            {
                setter(result.MergedItem, cloudVal);
            }
            else
            {
                setter(result.MergedItem, baseVal);
            }
        }

        MergeProperty(nameof(TodoItem.Title), x => x.Title, (x, v) => x.Title = v);
        MergeProperty(nameof(TodoItem.Description), x => x.Description, (x, v) => x.Description = v);
        MergeProperty(nameof(TodoItem.IsCompleted), x => x.IsCompleted, (x, v) => x.IsCompleted = v);
        MergeProperty(nameof(TodoItem.CompletedAt), x => x.CompletedAt, (x, v) => x.CompletedAt = v);
        MergeProperty(nameof(TodoItem.Priority), x => x.Priority, (x, v) => x.Priority = v);
        MergeProperty(nameof(TodoItem.DueDate), x => x.DueDate, (x, v) => x.DueDate = v);
        MergeProperty(nameof(TodoItem.ReminderAt), x => x.ReminderAt, (x, v) => x.ReminderAt = v);
        MergeProperty(nameof(TodoItem.Category), x => x.Category, (x, v) => x.Category = v);
        MergeProperty(nameof(TodoItem.IsRecurring), x => x.IsRecurring, (x, v) => x.IsRecurring = v);
        MergeProperty(nameof(TodoItem.RecurrenceType), x => x.RecurrenceType, (x, v) => x.RecurrenceType = v);
        MergeProperty(nameof(TodoItem.CustomRecurrenceInterval), x => x.CustomRecurrenceInterval, (x, v) => x.CustomRecurrenceInterval = v);
        MergeProperty(nameof(TodoItem.CustomRecurrenceUnit), x => x.CustomRecurrenceUnit, (x, v) => x.CustomRecurrenceUnit = v);
        MergeProperty(nameof(TodoItem.CustomWeeklyDays), x => x.CustomWeeklyDays, (x, v) => x.CustomWeeklyDays = v);

        result.MergedItem.Id = local.Id;
        result.MergedItem.UpdatedAt = DateTime.UtcNow;

        if (result.MergedItem.IsCompleted)
        {
            result.MergedItem.CompletedAt ??= result.MergedItem.UpdatedAt ?? DateTime.UtcNow;
        }
        else
        {
            result.MergedItem.CompletedAt = null;
        }

        return result;
    }

    public static TodoItem Clone(TodoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new TodoItem
        {
            Id = item.Id,
            Title = item.Title,
            Description = item.Description,
            IsCompleted = item.IsCompleted,
            CompletedAt = item.CompletedAt,
            Priority = item.Priority,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            DueDate = item.DueDate,
            ReminderAt = item.ReminderAt,
            Version = item.Version,
            IsDeleted = item.IsDeleted,
            DeletedAt = item.DeletedAt,
            SeriesId = item.SeriesId,
            IsRecurring = item.IsRecurring,
            RecurrenceType = item.RecurrenceType,
            CustomRecurrenceInterval = item.CustomRecurrenceInterval,
            CustomRecurrenceUnit = item.CustomRecurrenceUnit,
            CustomWeeklyDays = item.CustomWeeklyDays,
            Category = item.Category
        };
    }
}
