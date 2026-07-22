using Wadd.Core.Models;

namespace Wadd.Core.Interfaces;

/// <summary>
/// Service interface for managing CRUD operations for Todo items.
/// </summary>
public interface ITodoService
{
    Task<IEnumerable<TodoItem>> GetTodosAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<TodoItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TodoItem> AddTodoAsync(TodoItem item, CancellationToken cancellationToken = default);
    Task<TodoItem> CreateAsync(TodoItem item, CancellationToken cancellationToken = default);
    Task<bool> UpdateTodoAsync(TodoItem item, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(TodoItem item, CancellationToken cancellationToken = default);
    Task<bool> DeleteTodoAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ToggleCompleteAsync(Guid id, CancellationToken cancellationToken = default);
}
