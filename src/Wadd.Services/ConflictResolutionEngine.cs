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

    public static DateTime EnsureUtc(DateTime dt)
    {
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
    }

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

        long localTicks = EnsureUtc(localUpdated).Ticks;
        long remoteTicks = EnsureUtc(remoteUpdated).Ticks;

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

    public static LifeGoal MergeGoal(LifeGoal? local, LifeGoal? remote)
    {
        if (local == null && remote == null)
            throw new ArgumentException("Both local and remote goals cannot be null.");
        if (local == null) return CloneGoal(remote!);
        if (remote == null) return CloneGoal(local!);

        DateTime localUpdated = local.UpdatedAt ?? local.CreatedAt;
        DateTime remoteUpdated = remote.UpdatedAt ?? remote.CreatedAt;

        long localTicks = EnsureUtc(localUpdated).Ticks;
        long remoteTicks = EnsureUtc(remoteUpdated).Ticks;

        // Tombstone wins to prevent resurrection
        if (local.IsDeleted || remote.IsDeleted)
        {
            var baseGoal = remoteTicks > localTicks ? CloneGoal(remote) : CloneGoal(local);
            baseGoal.IsDeleted = true;
            baseGoal.DeletedAt = (local.IsDeleted ? local.DeletedAt : remote.DeletedAt) ?? local.DeletedAt ?? remote.DeletedAt ?? DateTime.UtcNow;
            baseGoal.UpdatedAt = new DateTime(Math.Max(localTicks, remoteTicks), DateTimeKind.Utc);
            return baseGoal;
        }

        if (remoteTicks > localTicks)
        {
            return CloneGoal(remote);
        }
        else if (localTicks > remoteTicks)
        {
            return CloneGoal(local);
        }
        else
        {
            string localKey = local.Title + (local.Description ?? "") + local.Category;
            string remoteKey = remote.Title + (remote.Description ?? "") + remote.Category;
            return string.Compare(localKey, remoteKey, StringComparison.Ordinal) >= 0
                ? CloneGoal(local)
                : CloneGoal(remote);
        }
    }

    public static LifeGoal CloneGoal(LifeGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return new LifeGoal
        {
            Id = goal.Id,
            Title = goal.Title,
            Description = goal.Description,
            Category = goal.Category,
            TargetDate = goal.TargetDate,
            IsAchieved = goal.IsAchieved,
            CreatedAt = goal.CreatedAt,
            UpdatedAt = goal.UpdatedAt,
            IsDeleted = goal.IsDeleted,
            DeletedAt = goal.DeletedAt
        };
    }

    public static GoalMilestone MergeMilestone(GoalMilestone? local, GoalMilestone? remote)
    {
        if (local == null && remote == null)
            throw new ArgumentException("Both local and remote milestones cannot be null.");
        if (local == null) return CloneMilestone(remote!);
        if (remote == null) return CloneMilestone(local!);

        DateTime localUpdated = local.UpdatedAt ?? DateTime.MinValue;
        DateTime remoteUpdated = remote.UpdatedAt ?? DateTime.MinValue;

        long localTicks = EnsureUtc(localUpdated).Ticks;
        long remoteTicks = EnsureUtc(remoteUpdated).Ticks;

        // Tombstone wins to prevent resurrection
        if (local.IsDeleted || remote.IsDeleted)
        {
            var baseMilestone = remoteTicks > localTicks ? CloneMilestone(remote) : CloneMilestone(local);
            baseMilestone.IsDeleted = true;
            baseMilestone.DeletedAt = (local.IsDeleted ? local.DeletedAt : remote.DeletedAt) ?? local.DeletedAt ?? remote.DeletedAt ?? DateTime.UtcNow;
            baseMilestone.UpdatedAt = new DateTime(Math.Max(localTicks, remoteTicks), DateTimeKind.Utc);
            return baseMilestone;
        }

        if (remoteTicks > localTicks)
        {
            return CloneMilestone(remote);
        }
        else if (localTicks > remoteTicks)
        {
            return CloneMilestone(local);
        }
        else
        {
            return string.Compare(local.Title, remote.Title, StringComparison.Ordinal) >= 0
                ? CloneMilestone(local)
                : CloneMilestone(remote);
        }
    }

    public static GoalMilestone CloneMilestone(GoalMilestone milestone)
    {
        ArgumentNullException.ThrowIfNull(milestone);
        return new GoalMilestone
        {
            Id = milestone.Id,
            GoalId = milestone.GoalId,
            Title = milestone.Title,
            IsCompleted = milestone.IsCompleted,
            OrderIndex = milestone.OrderIndex,
            UpdatedAt = milestone.UpdatedAt,
            IsDeleted = milestone.IsDeleted,
            DeletedAt = milestone.DeletedAt
        };
    }

    public static JournalEntry MergeJournalEntry(JournalEntry? local, JournalEntry? remote)
    {
        if (local == null && remote == null)
            throw new ArgumentException("Both local and remote journal entries cannot be null.");
        if (local == null) return CloneJournalEntry(remote!);
        if (remote == null) return CloneJournalEntry(local!);

        DateTime localUpdated = local.UpdatedAt ?? local.EntryDate;
        DateTime remoteUpdated = remote.UpdatedAt ?? remote.EntryDate;

        long localTicks = EnsureUtc(localUpdated).Ticks;
        long remoteTicks = EnsureUtc(remoteUpdated).Ticks;

        // Tombstone wins to prevent resurrection
        if (local.IsDeleted || remote.IsDeleted)
        {
            var baseEntry = remoteTicks > localTicks ? CloneJournalEntry(remote) : CloneJournalEntry(local);
            baseEntry.IsDeleted = true;
            baseEntry.DeletedAt = (local.IsDeleted ? local.DeletedAt : remote.DeletedAt) ?? local.DeletedAt ?? remote.DeletedAt ?? DateTime.UtcNow;
            baseEntry.UpdatedAt = new DateTime(Math.Max(localTicks, remoteTicks), DateTimeKind.Utc);
            return baseEntry;
        }

        if (remoteTicks > localTicks)
        {
            return CloneJournalEntry(remote);
        }
        else if (localTicks > remoteTicks)
        {
            return CloneJournalEntry(local);
        }
        else
        {
            string localKey = local.Title + (local.Content ?? "");
            string remoteKey = remote.Title + (remote.Content ?? "");
            return string.Compare(localKey, remoteKey, StringComparison.Ordinal) >= 0
                ? CloneJournalEntry(local)
                : CloneJournalEntry(remote);
        }
    }

    public static JournalEntry CloneJournalEntry(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new JournalEntry
        {
            Id = entry.Id,
            GoalId = entry.GoalId,
            Title = entry.Title,
            Content = entry.Content,
            EntryDate = entry.EntryDate,
            UpdatedAt = entry.UpdatedAt,
            IsDeleted = entry.IsDeleted,
            DeletedAt = entry.DeletedAt
        };
    }

    public static CalendarDateNote MergeDateNote(CalendarDateNote? local, CalendarDateNote? remote)
    {
        if (local == null && remote == null)
            throw new ArgumentException("Both local and remote date notes cannot be null.");
        if (local == null) return CloneDateNote(remote!);
        if (remote == null) return CloneDateNote(local!);

        DateTime localUpdated = local.UpdatedAt;
        DateTime remoteUpdated = remote.UpdatedAt;

        long localTicks = EnsureUtc(localUpdated).Ticks;
        long remoteTicks = EnsureUtc(remoteUpdated).Ticks;

        // Tombstone wins to prevent resurrection
        if (local.IsDeleted || remote.IsDeleted)
        {
            var baseNote = remoteTicks > localTicks ? CloneDateNote(remote) : CloneDateNote(local);
            baseNote.IsDeleted = true;
            baseNote.DeletedAt = (local.IsDeleted ? local.DeletedAt : remote.DeletedAt) ?? local.DeletedAt ?? remote.DeletedAt ?? DateTime.UtcNow;
            baseNote.UpdatedAt = new DateTime(Math.Max(localTicks, remoteTicks), DateTimeKind.Utc);
            return baseNote;
        }

        if (remoteTicks > localTicks)
        {
            return CloneDateNote(remote);
        }
        else if (localTicks > remoteTicks)
        {
            return CloneDateNote(local);
        }
        else
        {
            return string.Compare(local.NoteText, remote.NoteText, StringComparison.Ordinal) >= 0
                ? CloneDateNote(local)
                : CloneDateNote(remote);
        }
    }

    public static CalendarDateNote CloneDateNote(CalendarDateNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return new CalendarDateNote
        {
            DateKey = note.DateKey,
            NoteText = note.NoteText,
            CreatedAt = note.CreatedAt,
            UpdatedAt = note.UpdatedAt,
            IsDeleted = note.IsDeleted,
            DeletedAt = note.DeletedAt
        };
    }
}
