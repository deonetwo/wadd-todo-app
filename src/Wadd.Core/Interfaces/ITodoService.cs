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
    Task<bool> ToggleCompleteAsync(Guid id, DateTime? targetDate = null, CancellationToken cancellationToken = default);

    // Calendar Date Notes
    Task<string?> GetDateNoteAsync(DateTime date, CancellationToken cancellationToken = default);
    Task<Dictionary<string, string>> GetAllDateNotesAsync(CancellationToken cancellationToken = default);
    Task SaveDateNoteAsync(DateTime date, string noteText, CancellationToken cancellationToken = default);
    Task DeleteDateNoteAsync(DateTime date, CancellationToken cancellationToken = default);
}
