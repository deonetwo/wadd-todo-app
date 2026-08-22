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
                await _database.CreateTableAsync<DateNoteEntity>();
                await _database.CreateTableAsync<CustomTagEntity>();
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
        if (item.IsRecurring && (!item.SeriesId.HasValue || item.SeriesId.Value == Guid.Empty))
        {
            item.SeriesId = item.Id;
            var initialDate = item.DueDate?.Date ?? DateTime.Today;
            item.DueDate = RecurrenceHelper.GetFirstValidOccurrenceDate(item, initialDate);
        }

        if (item.IsCompleted)
        {
            item.CompletedAt ??= DateTime.UtcNow;
        }

        var existing = await _database.Table<TodoItemEntity>().FirstOrDefaultAsync(x => x.Id == item.Id);
        if (existing != null)
        {
            item.Version = existing.Version + 1;
            var entity = TodoItemEntity.FromDomain(item);
            await _database.UpdateAsync(entity);
            await LogChangeAsync(item, SyncOperation.Update, cancellationToken);
        }
        else
        {
            var entity = TodoItemEntity.FromDomain(item);
            await _database.InsertAsync(entity);
            await LogChangeAsync(item, SyncOperation.Insert, cancellationToken);
        }

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

        if (item.IsDeleted)
        {
            item.DeletedAt ??= DateTime.UtcNow;
        }
        else
        {
            item.DeletedAt = null;
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
        entity.DeletedAt = DateTime.UtcNow;
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

    public async Task<bool> ToggleCompleteAsync(Guid id, DateTime? targetDate = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var item = await GetByIdAsync(id, cancellationToken);
        if (item == null) return false;

        var allTodos = await GetTodosAsync(cancellationToken);
        DateTime? effectiveTargetDate = targetDate ?? item.DueDate;
        Guid seriesId = item.SeriesId ?? item.Id;

        if (effectiveTargetDate.HasValue && !item.IsCompleted && item.IsRecurring && !string.Equals(item.RecurrenceType, "None", StringComparison.OrdinalIgnoreCase))
        {
            DateTime dateToComplete = effectiveTargetDate.Value.Date;
            item.SeriesId = seriesId;

            Guid deterministicId = RecurrenceHelper.GenerateDeterministicRecurringId(seriesId, dateToComplete);

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
                var nextDueDate = RecurrenceHelper.CalculateNextUncompletedDueDate(item, dateToComplete, allTodos);
                item.DueDate = nextDueDate;
                if (item.ReminderAt.HasValue)
                {
                    item.ReminderAt = nextDueDate.Add(item.ReminderAt.Value.TimeOfDay);
                }
            }

            await AddTodoAsync(completedInstance, cancellationToken);
            return await UpdateTodoAsync(item, cancellationToken);
        }

        if (item.IsCompleted && !string.Equals(item.RecurrenceType, "None", StringComparison.OrdinalIgnoreCase))
        {
            var activeParent = allTodos.FirstOrDefault(t =>
                t.IsRecurring &&
                !t.IsCompleted &&
                ((t.SeriesId.HasValue && t.SeriesId.Value == seriesId) || t.Id == seriesId || string.Equals(t.Title.Trim(), item.Title.Trim(), StringComparison.OrdinalIgnoreCase))
            );

            if (activeParent != null && activeParent.Id != item.Id)
            {
                await DeleteTodoAsync(item.Id, cancellationToken);

                DateTime untoggledDate = effectiveTargetDate?.Date ?? item.DueDate?.Date ?? DateTime.Today;
                if (untoggledDate < (activeParent.DueDate?.Date ?? DateTime.MaxValue))
                {
                    activeParent.DueDate = untoggledDate;
                    if (activeParent.ReminderAt.HasValue)
                    {
                        activeParent.ReminderAt = untoggledDate.Add(activeParent.ReminderAt.Value.TimeOfDay);
                    }
                    await UpdateTodoAsync(activeParent, cancellationToken);
                }
                return true;
            }
            else
            {
                item.IsCompleted = false;
                item.IsRecurring = true;
                item.CompletedAt = null;
                item.SeriesId = seriesId;
                return await UpdateTodoAsync(item, cancellationToken);
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

        if (item.IsCompleted && item.IsRecurring && !string.Equals(item.RecurrenceType, "None", StringComparison.OrdinalIgnoreCase))
        {
            item.SeriesId = seriesId;
            var nextDueDate = RecurrenceHelper.CalculateNextUncompletedDueDate(item, null, allTodos);
            DateTime completionDate = item.DueDate?.Date ?? DateTime.Today;
            Guid deterministicId = RecurrenceHelper.GenerateDeterministicRecurringId(seriesId, completionDate);

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

            await AddTodoAsync(completedInstance, cancellationToken);
            return await UpdateTodoAsync(item, cancellationToken);
        }

        return await UpdateTodoAsync(item, cancellationToken);
    }

    public async Task DirectUpsertFromSyncAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        await BatchDirectUpsertFromSyncAsync(new[] { item }, cancellationToken);
    }

    public async Task BatchDirectUpsertFromSyncAsync(IEnumerable<TodoItem> items, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var list = items.ToList();
        if (list.Count == 0) return;

        await _database.RunInTransactionAsync(conn =>
        {
            var existingIds = conn.Table<TodoItemEntity>().Select(x => x.Id).ToHashSet();
            foreach (var item in list)
            {
                if (item.IsCompleted)
                {
                    item.CompletedAt ??= item.UpdatedAt ?? DateTime.UtcNow;
                }
                else
                {
                    item.CompletedAt = null;
                }

                var entity = TodoItemEntity.FromDomain(item);
                if (!existingIds.Contains(item.Id))
                {
                    conn.Insert(entity);
                }
                else
                {
                    conn.Update(entity);
                }
            }
        });
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

    public async Task<string?> GetDateNoteAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var key = date.ToString("yyyy-MM-dd");
        var entity = await _database.Table<DateNoteEntity>().FirstOrDefaultAsync(x => x.DateKey == key);
        return entity?.NoteText;
    }

    public async Task<Dictionary<string, string>> GetAllDateNotesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entities = await _database.Table<DateNoteEntity>().ToListAsync();
        return entities.Where(x => !string.IsNullOrWhiteSpace(x.NoteText))
                       .ToDictionary(x => x.DateKey, x => x.NoteText);
    }

    public async Task SaveDateNoteAsync(DateTime date, string noteText, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var key = date.ToString("yyyy-MM-dd");
        var existing = await _database.Table<DateNoteEntity>().FirstOrDefaultAsync(x => x.DateKey == key);

        if (string.IsNullOrWhiteSpace(noteText))
        {
            if (existing != null)
            {
                await _database.DeleteAsync(existing);
            }
            return;
        }

        if (existing == null)
        {
            await _database.InsertAsync(new DateNoteEntity
            {
                DateKey = key,
                NoteText = noteText,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.NoteText = noteText;
            existing.UpdatedAt = DateTime.UtcNow;
            await _database.UpdateAsync(existing);
        }
    }

    public async Task DeleteDateNoteAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var key = date.ToString("yyyy-MM-dd");
        var existing = await _database.Table<DateNoteEntity>().FirstOrDefaultAsync(x => x.DateKey == key);
        if (existing != null)
        {
            await _database.DeleteAsync(existing);
        }
    }

    public async Task<bool> RenameCategoryAsync(string oldCategory, string newCategory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(oldCategory) || string.IsNullOrWhiteSpace(newCategory)) return false;
        await EnsureInitializedAsync();

        var oldName = oldCategory.Trim();
        var newName = newCategory.Trim();
        var allEntities = await _database.Table<TodoItemEntity>().ToListAsync();
        bool updatedAny = false;

        foreach (var entity in allEntities)
        {
            if (string.IsNullOrWhiteSpace(entity.Category)) continue;

            var model = entity.ToDomain();
            var categories = model.CategoriesList;
            if (categories.Any(c => c.Equals(oldName, StringComparison.OrdinalIgnoreCase)))
            {
                var updatedList = categories
                    .Select(c => c.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName : c)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                entity.Category = updatedList.Count > 0 ? string.Join(", ", updatedList) : null;
                await _database.UpdateAsync(entity);
                updatedAny = true;
            }
        }

        return updatedAny;
    }

    public async Task<bool> DeleteCategoryAsync(string categoryName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return false;
        await EnsureInitializedAsync();

        var targetName = categoryName.Trim();
        var allEntities = await _database.Table<TodoItemEntity>().ToListAsync();
        bool updatedAny = false;

        foreach (var entity in allEntities)
        {
            if (string.IsNullOrWhiteSpace(entity.Category)) continue;

            var model = entity.ToDomain();
            var categories = model.CategoriesList;
            if (categories.Any(c => c.Equals(targetName, StringComparison.OrdinalIgnoreCase)))
            {
                var updatedList = categories
                    .Where(c => !c.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                entity.Category = updatedList.Count > 0 ? string.Join(", ", updatedList) : null;
                await _database.UpdateAsync(entity);
                updatedAny = true;
            }
        }

        await DeleteCustomTagAsync(targetName, cancellationToken);
        return updatedAny;
    }

    public async Task<Dictionary<string, DateTime>> GetCustomTagsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var list = await _database.Table<CustomTagEntity>().ToListAsync();
        return list.ToDictionary(x => x.Name, x => x.CreatedAt, StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveCustomTagAsync(string name, DateTime? createdAt = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        await EnsureInitializedAsync();
        var tag = name.Trim();
        var existing = await _database.Table<CustomTagEntity>().FirstOrDefaultAsync(x => x.Name == tag);
        if (existing == null)
        {
            await _database.InsertAsync(new CustomTagEntity
            {
                Name = tag,
                CreatedAt = createdAt ?? DateTime.UtcNow
            });
        }
    }

    public async Task DeleteCustomTagAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        await EnsureInitializedAsync();
        var tag = name.Trim();
        var existing = await _database.Table<CustomTagEntity>().FirstOrDefaultAsync(x => x.Name == tag);
        if (existing != null)
        {
            await _database.DeleteAsync(existing);
        }
    }

    public async Task RenameCustomTagAsync(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;
        await EnsureInitializedAsync();
        var oldTag = oldName.Trim();
        var newTag = newName.Trim();
        var existing = await _database.Table<CustomTagEntity>().FirstOrDefaultAsync(x => x.Name == oldTag);
        if (existing != null)
        {
            var created = existing.CreatedAt;
            await _database.DeleteAsync(existing);
            await _database.InsertOrReplaceAsync(new CustomTagEntity
            {
                Name = newTag,
                CreatedAt = created
            });
        }
        else
        {
            await SaveCustomTagAsync(newTag, DateTime.UtcNow, cancellationToken);
        }
    }
}
