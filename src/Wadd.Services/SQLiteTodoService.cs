using System.Text.Json;
using SQLite;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Wadd.Services.Entities;

namespace Wadd.Services;

public class SQLiteTodoService : ITodoService
{
    private readonly SQLiteAsyncConnection _database;
    private readonly ISyncLogRepository _syncLogRepository;
    private readonly IDeviceService _deviceService;
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private bool _isInitialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public SQLiteTodoService()
    {
        SQLitePCL.Batteries_V2.Init();

        var databasePath = Wadd.Core.Helpers.AppDataHelper.GetWaddFilePath("wadd.db");
        _database = new SQLiteAsyncConnection(databasePath);
        _deviceService = new DeviceService();
        _syncLogRepository = new SQLiteSyncLogRepository(_database);
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
        _deviceService = new DeviceService();
        _syncLogRepository = new SQLiteSyncLogRepository(_database);
    }

    public SQLiteTodoService(SQLiteAsyncConnection database, ISyncLogRepository syncLogRepository, IDeviceService deviceService)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _deviceService = deviceService ?? new DeviceService();
        _syncLogRepository = syncLogRepository ?? new SQLiteSyncLogRepository(_database);
    }

    public SQLiteAsyncConnection DatabaseConnection => _database;
    public ISyncLogRepository SyncLogRepository => _syncLogRepository;
    public IDeviceService DeviceService => _deviceService;

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;

        await _initializationSemaphore.WaitAsync();
        try
        {
            if (!_isInitialized)
            {
                await _database.CreateTableAsync<TodoItemEntity>();
                await _database.CreateTableAsync<SyncLogEntity>();
                await _database.CreateTableAsync<SyncConflictEntity>();
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
        var entities = await _database.Table<TodoItemEntity>()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
        return entities.Select(e => e.ToDomain());
    }

    public async Task<IEnumerable<TodoItem>> GetAllAsync(CancellationToken cancellationToken = default)
        => await GetTodosAsync(cancellationToken);

    public async Task<IEnumerable<TodoItem>> GetAllRawAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entities = await _database.Table<TodoItemEntity>().ToListAsync();
        return entities.Select(e => e.ToDomain());
    }

    public async Task<TodoItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entity = await _database.Table<TodoItemEntity>()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        return entity?.ToDomain();
    }

    public async Task<TodoItem?> GetRawByIdAsync(Guid id, CancellationToken cancellationToken = default)
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
        item.Version = 1;
        item.IsDeleted = false;
        if (item.IsCompleted)
        {
            item.CompletedAt ??= DateTime.UtcNow;
        }

        var entity = TodoItemEntity.FromDomain(item);
        await _database.InsertAsync(entity);

        // Record sync log
        await LogChangeAsync(item, SyncOperation.Insert, cancellationToken);

        return item;
    }

    public async Task<TodoItem> CreateAsync(TodoItem item, CancellationToken cancellationToken = default)
        => await AddTodoAsync(item, cancellationToken);

    public async Task<bool> UpdateTodoAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        var existing = await _database.Table<TodoItemEntity>().FirstOrDefaultAsync(x => x.Id == item.Id);
        item.UpdatedAt = DateTime.UtcNow;
        item.Version = (existing?.Version ?? item.Version) + 1;
        if (item.IsCompleted)
        {
            item.CompletedAt ??= DateTime.UtcNow;
        }
        else
        {
            item.CompletedAt = null;
        }

        var entity = TodoItemEntity.FromDomain(item);
        var rows = await _database.UpdateAsync(entity);

        if (rows > 0)
        {
            await LogChangeAsync(item, SyncOperation.Update, cancellationToken);
        }

        return rows > 0;
    }

    public async Task<bool> UpdateAsync(TodoItem item, CancellationToken cancellationToken = default)
        => await UpdateTodoAsync(item, cancellationToken);

    public async Task<bool> DeleteTodoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entity = await _database.Table<TodoItemEntity>().FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null || entity.IsDeleted) return false;

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.Version++;

        var rows = await _database.UpdateAsync(entity);

        if (rows > 0)
        {
            await LogChangeAsync(entity.ToDomain(), SyncOperation.Delete, cancellationToken);
        }

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

        if (item.IsCompleted && item.IsRecurring && !string.Equals(item.RecurrenceType, "None", StringComparison.OrdinalIgnoreCase))
        {
            var nextDueDate = RecurrenceHelper.CalculateNextDueDate(item);
            var nextItem = new TodoItem
            {
                Id = Guid.NewGuid(),
                Title = item.Title,
                Description = item.Description,
                IsCompleted = false,
                Priority = item.Priority,
                CreatedAt = DateTime.UtcNow,
                DueDate = nextDueDate,
                ReminderAt = item.ReminderAt.HasValue && item.DueDate.HasValue
                    ? nextDueDate.Add(item.ReminderAt.Value.TimeOfDay)
                    : item.ReminderAt,
                IsRecurring = true,
                RecurrenceType = item.RecurrenceType,
                CustomRecurrenceInterval = item.CustomRecurrenceInterval,
                CustomRecurrenceUnit = item.CustomRecurrenceUnit,
                CustomWeeklyDays = item.CustomWeeklyDays
            };

            // Mark this completed historical instance as no longer actively recurring so toggling done/undone does not duplicate tasks
            item.IsRecurring = false;

            var updatedOriginal = await UpdateTodoAsync(item, cancellationToken);
            await AddTodoAsync(nextItem, cancellationToken);
            return updatedOriginal;
        }

        return await UpdateTodoAsync(item, cancellationToken);
    }

    public async Task DirectUpsertFromSyncAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var existing = await _database.Table<TodoItemEntity>().FirstOrDefaultAsync(x => x.Id == item.Id);
        var entity = TodoItemEntity.FromDomain(item);

        if (existing == null)
        {
            await _database.InsertAsync(entity);
        }
        else
        {
            await _database.UpdateAsync(entity);
        }
    }

    public async Task DirectUpsertWithLogAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var existing = await _database.Table<TodoItemEntity>().FirstOrDefaultAsync(x => x.Id == item.Id);
        var entity = TodoItemEntity.FromDomain(item);

        if (existing == null)
        {
            await _database.InsertAsync(entity);
            await LogChangeAsync(item, SyncOperation.Insert, cancellationToken);
        }
        else
        {
            await _database.UpdateAsync(entity);
            await LogChangeAsync(item, SyncOperation.Update, cancellationToken);
        }
    }

    public Task CloseAsync() => _database.CloseAsync();

    private async Task LogChangeAsync(TodoItem item, SyncOperation operation, CancellationToken cancellationToken)
    {
        try
        {
            var log = new SyncLog
            {
                Id = Guid.NewGuid(),
                TableName = "TodoItem",
                RecordId = item.Id,
                Operation = operation,
                PayloadJson = JsonSerializer.Serialize(item, JsonOptions),
                Timestamp = DateTime.UtcNow,
                DeviceId = _deviceService.GetDeviceId(),
                Synced = false
            };

            await _syncLogRepository.AddLogAsync(log, cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to write sync log: {ex.Message}");
        }
    }
}
