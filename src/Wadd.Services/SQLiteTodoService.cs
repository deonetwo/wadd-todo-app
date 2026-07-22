using SQLite;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;

namespace Wadd.Services;

public class SQLiteTodoService : ITodoService
{
    private readonly SQLiteAsyncConnection _database;
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private bool _isInitialized;

    public SQLiteTodoService()
    {
        // Initialize SQLitePCLRaw bundle_e_sqlite3 battery
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
                await _database.CreateTableAsync<TodoItem>();
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
        return await _database.Table<TodoItem>().OrderByDescending(x => x.CreatedAt).ToListAsync();
    }

    public async Task<IEnumerable<TodoItem>> GetAllAsync(CancellationToken cancellationToken = default)
        => await GetTodosAsync(cancellationToken);

    public async Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        return await _database.Table<TodoItem>().FirstOrDefaultAsync(x => x.Id == id);
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

        await _database.InsertAsync(item);
        return item;
    }

    public async Task<TodoItem> CreateAsync(TodoItem item, CancellationToken cancellationToken = default)
        => await AddTodoAsync(item, cancellationToken);

    public async Task<bool> UpdateTodoAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        item.UpdatedAt = DateTime.UtcNow;
        var rows = await _database.UpdateAsync(item);
        return rows > 0;
    }

    public async Task<bool> UpdateAsync(TodoItem item, CancellationToken cancellationToken = default)
        => await UpdateTodoAsync(item, cancellationToken);

    public async Task<bool> DeleteTodoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var item = await GetByIdAsync(id, cancellationToken);
        if (item == null) return false;

        var rows = await _database.DeleteAsync(item);
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
        item.UpdatedAt = DateTime.UtcNow;

        var rows = await _database.UpdateAsync(item);
        return rows > 0;
    }
}
