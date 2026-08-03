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

    public Task<bool> ToggleCompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = _items.FirstOrDefault(x => x.Id == id);
        if (item == null) return Task.FromResult(false);

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
