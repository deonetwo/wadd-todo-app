using SQLite;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Wadd.Services.Entities;

namespace Wadd.Services;

public class SQLiteTodoService : ITodoService
{
    private readonly SQLiteAsyncConnection _database;
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private bool _isInitialized;

    public SQLiteTodoService()
    {
        SQLitePCL.Batteries_V2.Init();

        var folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wadd");
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        var databasePath = Path.Combine(folderPath, "wadd.db");
        _database = new SQLiteAsyncConnection(databasePath);
    }

    public SQLiteTodoService(string databasePath)
    {
        SQLitePCL.Batteries_V2.Init();

        var folderPath = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(folderPath) && !Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        _database = new SQLiteAsyncConnection(databasePath);
    }

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;

        await _initializationSemaphore.WaitAsync();
        try
        {
            if (!_isInitialized)
            {
                await _database.CreateTableAsync<TodoItemEntity>();
                _isInitialized = true;
            }
        }
        finally
        {
            _initializationSemaphore.Release();
        }
    }

    public async Task<IEnumerable<TodoItem>> GetTodosAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entities = await _database.Table<TodoItemEntity>().OrderByDescending(x => x.CreatedAt).ToListAsync();
        return entities.Select(e => e.ToDomain());
    }

    public async Task<IEnumerable<TodoItem>> GetAllAsync(CancellationToken cancellationToken = default)
        => await GetTodosAsync(cancellationToken);

    public async Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entity = await _database.Table<TodoItemEntity>().FirstOrDefaultAsync(x => x.Id == id);
        return entity?.ToDomain();
    }

    public async Task<TodoItem> AddTodoAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        if (item.Id == Guid.Empty)
        {
            item.Id = Guid.NewGuid();
        }
        item.CreatedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;

        var entity = TodoItemEntity.FromDomain(item);
        await _database.InsertAsync(entity);
        return item;
    }

    public async Task<TodoItem> CreateAsync(TodoItem item, CancellationToken cancellationToken = default)
        => await AddTodoAsync(item, cancellationToken);

    public async Task<bool> UpdateTodoAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        item.UpdatedAt = DateTime.UtcNow;
        var entity = TodoItemEntity.FromDomain(item);
        var rows = await _database.UpdateAsync(entity);
        return rows > 0;
    }

    public async Task<bool> UpdateAsync(TodoItem item, CancellationToken cancellationToken = default)
        => await UpdateTodoAsync(item, cancellationToken);

    public async Task<bool> DeleteTodoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entity = await _database.Table<TodoItemEntity>().FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return false;

        var rows = await _database.DeleteAsync(entity);
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => await DeleteTodoAsync(id, cancellationToken);

    public async Task<bool> ToggleCompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var item = await GetByIdAsync(id, cancellationToken);
        if (item == null) return false;

        item.IsCompleted = !item.IsCompleted;
        return await UpdateTodoAsync(item, cancellationToken);
    }
}
