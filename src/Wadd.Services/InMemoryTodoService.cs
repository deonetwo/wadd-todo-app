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

        if (targetDate.HasValue && item.IsRecurring && !string.Equals(item.RecurrenceType, "None", StringComparison.OrdinalIgnoreCase))
        {
            DateTime dateToComplete = targetDate.Value.Date;

            var completedInstance = new TodoItem
            {
                Id = Guid.NewGuid(),
                Title = item.Title,
                Description = item.Description,
                IsCompleted = true,
                CompletedAt = DateTime.UtcNow,
                Priority = item.Priority,
                CreatedAt = DateTime.UtcNow,
                DueDate = dateToComplete,
                ReminderAt = item.ReminderAt.HasValue && item.DueDate.HasValue
                    ? dateToComplete.Add(item.ReminderAt.Value.TimeOfDay)
                    : item.ReminderAt,
                IsRecurring = false,
                RecurrenceType = item.RecurrenceType
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

        if (targetDate.HasValue && item.IsCompleted && !item.IsRecurring)
        {
            _items.Remove(item);

            var activeParent = _items.FirstOrDefault(t => t.IsRecurring && !t.IsCompleted && t.Title.Equals(item.Title, StringComparison.OrdinalIgnoreCase));
            if (activeParent != null && targetDate.Value.Date < (activeParent.DueDate?.Date ?? DateTime.MaxValue))
            {
                activeParent.DueDate = targetDate.Value.Date;
                if (activeParent.ReminderAt.HasValue)
                {
                    activeParent.ReminderAt = targetDate.Value.Date.Add(activeParent.ReminderAt.Value.TimeOfDay);
                }
            }
            return Task.FromResult(true);
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
        return Task.FromResult(true);
    }
}
