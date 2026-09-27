using SQLite;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Wadd.Services.Entities;

namespace Wadd.Services;

public class SQLiteConflictRepository : IConflictRepository
{
    private readonly SQLiteAsyncConnection _database;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _isInitialized;

    public SQLiteConflictRepository(SQLiteAsyncConnection database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public SQLiteConflictRepository(string dbPath)
    {
        _database = new SQLiteAsyncConnection(dbPath);
    }

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;
        await _semaphore.WaitAsync();
        try
        {
            if (!_isInitialized)
            {
                await _database.CreateTableAsync<SyncConflictEntity>();
                _isInitialized = true;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task AddConflictAsync(SyncConflict conflict, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entity = SyncConflictEntity.FromDomain(conflict);
        await _database.InsertAsync(entity);
    }

    public async Task<IEnumerable<SyncConflict>> GetUnresolvedConflictsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entities = await _database.Table<SyncConflictEntity>()
            .Where(x => x.Status == (int)ConflictStatus.Unresolved)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return entities.Select(e => e.ToDomain());
    }

    public async Task<IEnumerable<SyncConflict>> GetConflictHistoryAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entities = await _database.Table<SyncConflictEntity>()
            .Where(x => x.Status == (int)ConflictStatus.Resolved)
            .OrderByDescending(x => x.ResolvedAt)
            .ToListAsync();

        return entities.Select(e => e.ToDomain());
    }

    public async Task ResolveConflictAsync(Guid conflictId, ConflictResolutionType resolutionType, string resolvedVersionJson, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entity = await _database.Table<SyncConflictEntity>().FirstOrDefaultAsync(x => x.Id == conflictId);
        if (entity != null)
        {
            entity.Status = (int)ConflictStatus.Resolved;
            entity.ResolvedAt = DateTime.UtcNow;
            entity.ResolutionType = (int)resolutionType;
            entity.ResolvedVersionJson = resolvedVersionJson;
            await _database.UpdateAsync(entity);
        }
    }
}
