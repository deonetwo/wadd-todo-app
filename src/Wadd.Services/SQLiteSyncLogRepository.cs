using SQLite;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Wadd.Services.Entities;

namespace Wadd.Services;

public class SQLiteSyncLogRepository : ISyncLogRepository
{
    private readonly SQLiteAsyncConnection _database;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _isInitialized;

    public SQLiteSyncLogRepository(SQLiteAsyncConnection database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public SQLiteSyncLogRepository(string dbPath)
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
                await _database.CreateTableAsync<SyncLogEntity>();
                _isInitialized = true;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task AddLogAsync(SyncLog log, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        if (log.Revision <= 0)
        {
            var maxRev = await GetHighestRevisionAsync(cancellationToken);
            log.Revision = maxRev + 1;
        }

        var entity = SyncLogEntity.FromDomain(log);
        await _database.InsertAsync(entity);
    }

    public async Task<IEnumerable<SyncLog>> GetPendingLogsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entities = await _database.Table<SyncLogEntity>()
            .Where(x => !x.Synced)
            .OrderBy(x => x.Revision)
            .ToListAsync();

        return entities.Select(e => e.ToDomain());
    }

    public async Task MarkLogsAsSyncedAsync(IEnumerable<Guid> logIds, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var idsList = logIds.ToList();
        if (!idsList.Any()) return;

        foreach (var id in idsList)
        {
            var entity = await _database.Table<SyncLogEntity>().FirstOrDefaultAsync(x => x.Id == id);
            if (entity != null)
            {
                entity.Synced = true;
                await _database.UpdateAsync(entity);
            }
        }
    }

    public async Task<long> GetHighestRevisionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var maxEntity = await _database.Table<SyncLogEntity>()
            .OrderByDescending(x => x.Revision)
            .FirstOrDefaultAsync();

        return maxEntity?.Revision ?? 0;
    }
}
