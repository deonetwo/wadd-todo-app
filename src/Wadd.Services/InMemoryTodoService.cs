using System.Collections.Concurrent;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;

namespace Wadd.Services;

public class InMemoryTodoService : ITodoService
{
    private readonly List<TodoItem> _items = new();

    public Task<IEnumerable<TodoItem>> GetTodosAsync(CancellationToken cancellationToken = default)
        => GetAllAsync(cancellationToken);

    public Task<IEnumerable<TodoItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<TodoItem>>(_items.ToList());
    }

    public Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = _items.FirstOrDefault(x => x.Id == id);
        return Task.FromResult(item);
    }

    public Task<TodoItem> AddTodoAsync(TodoItem item, CancellationToken cancellationToken = default)
        => CreateAsync(item, cancellationToken);

    public Task<TodoItem> CreateAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        item.CreatedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;
        if (item.IsRecurring)
        {
            var initialDate = item.DueDate?.Date ?? DateTime.Today;
            item.DueDate = Wadd.Core.Helpers.RecurrenceHelper.GetFirstValidOccurrenceDate(item, initialDate);
        }

        if (item.IsCompleted)
        {
            item.CompletedAt ??= DateTime.UtcNow;
        }
        _items.Add(item);
        return Task.FromResult(item);
    }

    public Task<bool> UpdateTodoAsync(TodoItem item, CancellationToken cancellationToken = default)
        => UpdateAsync(item, cancellationToken);

    public Task<bool> UpdateAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        var existingIndex = _items.FindIndex(x => x.Id == item.Id);
        if (existingIndex < 0) return Task.FromResult(false);

        item.UpdatedAt = DateTime.UtcNow;
        if (item.IsCompleted)
        {
            item.CompletedAt ??= DateTime.UtcNow;
        }
        else
        {
            item.CompletedAt = null;
        }
        _items[existingIndex] = item;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteTodoAsync(Guid id, CancellationToken cancellationToken = default)
        => DeleteAsync(id, cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = _items.FirstOrDefault(x => x.Id == id);
        if (item == null) return Task.FromResult(false);

        _items.Remove(item);
        return Task.FromResult(true);
    }

    public Task<bool> ToggleCompleteAsync(Guid id, DateTime? targetDate = null, CancellationToken cancellationToken = default)
    {
        var item = _items.FirstOrDefault(x => x.Id == id);
        if (item == null) return Task.FromResult(false);

        DateTime? effectiveTargetDate = targetDate ?? item.DueDate;
        Guid seriesId = item.SeriesId ?? item.Id;

        if (effectiveTargetDate.HasValue && !item.IsCompleted && item.IsRecurring && !string.Equals(item.RecurrenceType, "None", StringComparison.OrdinalIgnoreCase))
        {
            DateTime dateToComplete = effectiveTargetDate.Value.Date;
            item.SeriesId = seriesId;

            Guid deterministicId = Wadd.Core.Helpers.RecurrenceHelper.GenerateDeterministicRecurringId(seriesId, dateToComplete);

            var completedInstance = new TodoItem
            {
                Id = deterministicId,
                SeriesId = seriesId,
                Title = item.Title,
                Description = item.Description,
                IsCompleted = true,
                CompletedAt = DateTime.UtcNow,
                Priority = item.Priority,
                CreatedAt = item.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                DueDate = dateToComplete,
                ReminderAt = item.ReminderAt.HasValue && item.DueDate.HasValue
                    ? dateToComplete.Add(item.ReminderAt.Value.TimeOfDay)
                    : item.ReminderAt,
                IsRecurring = false,
                RecurrenceType = item.RecurrenceType,
                CustomRecurrenceInterval = item.CustomRecurrenceInterval,
                CustomRecurrenceUnit = item.CustomRecurrenceUnit,
                CustomWeeklyDays = item.CustomWeeklyDays,
                Category = item.Category
            };

            DateTime activeDueDate = item.DueDate?.Date ?? DateTime.Today;
            if (dateToComplete <= activeDueDate)
            {
                var nextDueDate = Wadd.Core.Helpers.RecurrenceHelper.CalculateNextUncompletedDueDate(item, dateToComplete, _items);
                item.DueDate = nextDueDate;
                if (item.ReminderAt.HasValue)
                {
                    item.ReminderAt = nextDueDate.Add(item.ReminderAt.Value.TimeOfDay);
                }
            }

            _items.Add(completedInstance);
            return Task.FromResult(true);
        }

        if (item.IsCompleted && !string.Equals(item.RecurrenceType, "None", StringComparison.OrdinalIgnoreCase))
        {
            var activeParent = _items.FirstOrDefault(t =>
                t.IsRecurring &&
                !t.IsCompleted &&
                ((t.SeriesId.HasValue && t.SeriesId.Value == seriesId) || t.Id == seriesId || string.Equals(t.Title.Trim(), item.Title.Trim(), StringComparison.OrdinalIgnoreCase))
            );

            if (activeParent != null && activeParent.Id != item.Id)
            {
                _items.Remove(item);

                DateTime untoggledDate = effectiveTargetDate?.Date ?? item.DueDate?.Date ?? DateTime.Today;
                if (untoggledDate < (activeParent.DueDate?.Date ?? DateTime.MaxValue))
                {
                    activeParent.DueDate = untoggledDate;
                    if (activeParent.ReminderAt.HasValue)
                    {
                        activeParent.ReminderAt = untoggledDate.Add(activeParent.ReminderAt.Value.TimeOfDay);
                    }
                }
                return Task.FromResult(true);
            }
            else
            {
                item.IsCompleted = false;
                item.IsRecurring = true;
                item.CompletedAt = null;
                item.SeriesId = seriesId;
                item.UpdatedAt = DateTime.UtcNow;
                return Task.FromResult(true);
            }
        }

        item.IsCompleted = !item.IsCompleted;
        if (item.IsCompleted)
        {
            item.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            item.CompletedAt = null;
        }
        item.UpdatedAt = DateTime.UtcNow;

        if (item.IsCompleted && item.IsRecurring && !string.Equals(item.RecurrenceType, "None", StringComparison.OrdinalIgnoreCase))
        {
            item.SeriesId = seriesId;
            var nextDueDate = Wadd.Core.Helpers.RecurrenceHelper.CalculateNextUncompletedDueDate(item, null, _items);
            DateTime completionDate = item.DueDate?.Date ?? DateTime.Today;
            Guid deterministicId = Wadd.Core.Helpers.RecurrenceHelper.GenerateDeterministicRecurringId(seriesId, completionDate);

            var completedInstance = new TodoItem
            {
                Id = deterministicId,
                SeriesId = seriesId,
                Title = item.Title,
                Description = item.Description,
                IsCompleted = true,
                CompletedAt = DateTime.UtcNow,
                Priority = item.Priority,
                CreatedAt = item.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                DueDate = completionDate,
                ReminderAt = item.ReminderAt,
                IsRecurring = false,
                RecurrenceType = item.RecurrenceType,
                CustomRecurrenceInterval = item.CustomRecurrenceInterval,
                CustomRecurrenceUnit = item.CustomRecurrenceUnit,
                CustomWeeklyDays = item.CustomWeeklyDays,
                Category = item.Category
            };

            item.IsCompleted = false;
            item.DueDate = nextDueDate;
            if (item.ReminderAt.HasValue)
            {
                item.ReminderAt = nextDueDate.Add(item.ReminderAt.Value.TimeOfDay);
            }

            _items.Add(completedInstance);
        }

        return Task.FromResult(true);
    }

    private readonly ConcurrentDictionary<string, string> _dateNotes = new();

    public Task<string?> GetDateNoteAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        _dateNotes.TryGetValue(date.ToString("yyyy-MM-dd"), out var note);
        return Task.FromResult(note);
    }

    public Task<Dictionary<string, string>> GetAllDateNotesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Dictionary<string, string>(_dateNotes));
    }

    public Task SaveDateNoteAsync(DateTime date, string noteText, CancellationToken cancellationToken = default)
    {
        var key = date.ToString("yyyy-MM-dd");
        if (string.IsNullOrWhiteSpace(noteText))
        {
            _dateNotes.TryRemove(key, out _);
        }
        else
        {
            _dateNotes[key] = noteText;
        }
        return Task.CompletedTask;
    }

    public Task DeleteDateNoteAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        _dateNotes.TryRemove(date.ToString("yyyy-MM-dd"), out _);
        return Task.CompletedTask;
    }

    public Task<bool> RenameCategoryAsync(string oldCategory, string newCategory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(oldCategory) || string.IsNullOrWhiteSpace(newCategory)) return Task.FromResult(false);

        var oldName = oldCategory.Trim();
        var newName = newCategory.Trim();
        bool updatedAny = false;

        foreach (var item in _items)
        {
            if (string.IsNullOrWhiteSpace(item.Category)) continue;

            var categories = item.CategoriesList;
            if (categories.Any(c => c.Equals(oldName, StringComparison.OrdinalIgnoreCase)))
            {
                var updatedList = categories
                    .Select(c => c.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName : c)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                item.Category = updatedList.Count > 0 ? string.Join(", ", updatedList) : null;
                item.UpdatedAt = DateTime.UtcNow;
                updatedAny = true;
            }
        }

        return Task.FromResult(updatedAny);
    }

    public Task<bool> DeleteCategoryAsync(string categoryName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return Task.FromResult(false);

        var targetName = categoryName.Trim();
        bool updatedAny = false;

        foreach (var item in _items)
        {
            if (string.IsNullOrWhiteSpace(item.Category)) continue;

            var categories = item.CategoriesList;
            if (categories.Any(c => c.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
            {
                var updatedList = categories
                    .Where(c => !c.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                item.Category = updatedList.Count > 0 ? string.Join(", ", updatedList) : null;
                item.UpdatedAt = DateTime.UtcNow;
                updatedAny = true;
            }
        }

        return Task.FromResult(updatedAny);
    }

    private readonly ConcurrentDictionary<string, DateTime> _customTags = new(StringComparer.OrdinalIgnoreCase);

    public Task<Dictionary<string, DateTime>> GetCustomTagsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Dictionary<string, DateTime>(_customTags, StringComparer.OrdinalIgnoreCase));
    }

    public Task SaveCustomTagAsync(string name, DateTime? createdAt = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return Task.CompletedTask;
        var tag = name.Trim();
        _customTags[tag] = createdAt ?? DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task DeleteCustomTagAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return Task.CompletedTask;
        _customTags.TryRemove(name.Trim(), out _);
        return Task.CompletedTask;
    }

    public Task RenameCustomTagAsync(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return Task.CompletedTask;
        var oldTag = oldName.Trim();
        var newTag = newName.Trim();

        if (_customTags.TryRemove(oldTag, out var created))
        {
            _customTags[newTag] = created;
        }
        return Task.CompletedTask;
    }
}
