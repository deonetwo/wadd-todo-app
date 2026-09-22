using SQLite;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Wadd.Services.Entities;

namespace Wadd.Services;

public class SQLiteGoalService : IGoalService
{
    private readonly SQLiteAsyncConnection _database;
    private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
    private bool _isInitialized;

    public SQLiteGoalService()
    {
        SQLitePCL.Batteries_V2.Init();
        var databasePath = AppDataHelper.GetWaddFilePath("wadd.db");
        _database = new SQLiteAsyncConnection(databasePath);
    }

    public SQLiteGoalService(string databasePath)
    {
        SQLitePCL.Batteries_V2.Init();
        var folderPath = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(folderPath) && !Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
        _database = new SQLiteAsyncConnection(databasePath);
    }

    public SQLiteGoalService(SQLiteAsyncConnection database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    private async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;

        await _initializationSemaphore.WaitAsync();
        try
        {
            if (!_isInitialized)
            {
                await _database.CreateTableAsync<LifeGoalEntity>();
                await _database.CreateTableAsync<GoalMilestoneEntity>();
                await _database.CreateTableAsync<JournalEntryEntity>();
                _isInitialized = true;
            }
        }
        finally
        {
            _initializationSemaphore.Release();
        }
    }

    #region Life Goals CRUD

    public async Task<IEnumerable<LifeGoal>> GetGoalsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entities = await _database.Table<LifeGoalEntity>()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.OrderIndex)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync();
        return entities.Select(e => e.ToDomain());
    }

    public async Task<LifeGoal?> GetGoalByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        await EnsureInitializedAsync();
        var entity = await _database.Table<LifeGoalEntity>()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        return entity?.ToDomain();
    }

    public async Task<LifeGoal> SaveGoalAsync(LifeGoal goal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(goal);
        await EnsureInitializedAsync();

        goal.UpdatedAt = DateTime.UtcNow;
        var entity = LifeGoalEntity.FromDomain(goal);
        await _database.InsertOrReplaceAsync(entity);
        return entity.ToDomain();
    }

    public async Task<bool> DeleteGoalAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        await EnsureInitializedAsync();

        var entity = await _database.Table<LifeGoalEntity>()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _database.UpdateAsync(entity);

        // Also soft delete associated milestones
        var milestones = await _database.Table<GoalMilestoneEntity>()
            .Where(m => m.GoalId == id && !m.IsDeleted)
            .ToListAsync();
        foreach (var m in milestones)
        {
            m.IsDeleted = true;
            m.DeletedAt = DateTime.UtcNow;
            m.UpdatedAt = DateTime.UtcNow;
            await _database.UpdateAsync(m);
        }

        return true;
    }

    #endregion

    #region Goal Milestones CRUD

    public async Task<IEnumerable<GoalMilestone>> GetMilestonesForGoalAsync(string goalId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(goalId)) return Enumerable.Empty<GoalMilestone>();
        await EnsureInitializedAsync();

        var entities = await _database.Table<GoalMilestoneEntity>()
            .Where(x => x.GoalId == goalId && !x.IsDeleted)
            .OrderBy(x => x.OrderIndex)
            .ToListAsync();
        return entities.Select(e => e.ToDomain());
    }

    public async Task<GoalMilestone> SaveMilestoneAsync(GoalMilestone milestone, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(milestone);
        await EnsureInitializedAsync();

        milestone.UpdatedAt = DateTime.UtcNow;
        var entity = GoalMilestoneEntity.FromDomain(milestone);
        await _database.InsertOrReplaceAsync(entity);
        return entity.ToDomain();
    }

    public async Task<bool> ToggleMilestoneAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        await EnsureInitializedAsync();

        var entity = await _database.Table<GoalMilestoneEntity>()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (entity == null) return false;

        entity.IsCompleted = !entity.IsCompleted;
        entity.UpdatedAt = DateTime.UtcNow;
        await _database.UpdateAsync(entity);
        return true;
    }

    public async Task<bool> DeleteMilestoneAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        await EnsureInitializedAsync();

        var entity = await _database.Table<GoalMilestoneEntity>()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _database.UpdateAsync(entity);
        return true;
    }

    public async Task ReorderMilestonesAsync(IEnumerable<GoalMilestone> milestones, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(milestones);
        await EnsureInitializedAsync();

        int idx = 0;
        foreach (var m in milestones)
        {
            m.OrderIndex = idx++;
            m.UpdatedAt = DateTime.UtcNow;
            var entity = GoalMilestoneEntity.FromDomain(m);
            await _database.UpdateAsync(entity);
        }
    }

    #endregion

    #region Journal Entries CRUD

    public async Task<IEnumerable<JournalEntry>> GetJournalEntriesAsync(string? goalId = null, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        List<JournalEntryEntity> entities;
        if (string.IsNullOrWhiteSpace(goalId))
        {
            entities = await _database.Table<JournalEntryEntity>()
                .Where(x => !x.IsDeleted)
                .OrderByDescending(x => x.EntryDate)
                .ToListAsync();
        }
        else
        {
            entities = await _database.Table<JournalEntryEntity>()
                .Where(x => x.GoalId == goalId && !x.IsDeleted)
                .OrderByDescending(x => x.EntryDate)
                .ToListAsync();
        }
        return entities.Select(e => e.ToDomain());
    }

    public async Task<JournalEntry> SaveJournalEntryAsync(JournalEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await EnsureInitializedAsync();

        entry.UpdatedAt = DateTime.UtcNow;
        var entity = JournalEntryEntity.FromDomain(entry);
        await _database.InsertOrReplaceAsync(entity);
        return entity.ToDomain();
    }

    public async Task<bool> DeleteJournalEntryAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        await EnsureInitializedAsync();

        var entity = await _database.Table<JournalEntryEntity>()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (entity == null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _database.UpdateAsync(entity);
        return true;
    }

    #endregion

    #region Progress Calculation Logic

    public double CalculateGoalProgress(IEnumerable<GoalMilestone> milestones)
    {
        if (milestones == null) return 0.0;
        var activeMilestones = milestones.Where(m => !m.IsDeleted).ToList();
        if (activeMilestones.Count == 0) return 0.0;

        int completed = activeMilestones.Count(m => m.IsCompleted);
        return ((double)completed / activeMilestones.Count) * 100.0;
    }

    #endregion

    #region Sync Support (Raw Queries & Direct Batch Upserts)

    public async Task<IEnumerable<LifeGoal>> GetAllGoalsRawAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entities = await _database.Table<LifeGoalEntity>().ToListAsync();
        return entities.Select(e => e.ToDomain());
    }

    public async Task<IEnumerable<GoalMilestone>> GetAllMilestonesRawAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entities = await _database.Table<GoalMilestoneEntity>().ToListAsync();
        return entities.Select(e => e.ToDomain());
    }

    public async Task<IEnumerable<JournalEntry>> GetAllJournalEntriesRawAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();
        var entities = await _database.Table<JournalEntryEntity>().ToListAsync();
        return entities.Select(e => e.ToDomain());
    }

    public async Task BatchDirectUpsertGoalsAsync(IEnumerable<LifeGoal> goals, CancellationToken cancellationToken = default)
    {
        if (goals == null) return;
        await EnsureInitializedAsync();

        var entities = goals.Select(LifeGoalEntity.FromDomain).ToList();
        if (entities.Count == 0) return;

        await _database.RunInTransactionAsync(conn =>
        {
            foreach (var entity in entities)
            {
                conn.InsertOrReplace(entity);
            }
        });
    }

    public async Task BatchDirectUpsertMilestonesAsync(IEnumerable<GoalMilestone> milestones, CancellationToken cancellationToken = default)
    {
        if (milestones == null) return;
        await EnsureInitializedAsync();

        var entities = milestones.Select(GoalMilestoneEntity.FromDomain).ToList();
        if (entities.Count == 0) return;

        await _database.RunInTransactionAsync(conn =>
        {
            foreach (var entity in entities)
            {
                conn.InsertOrReplace(entity);
            }
        });
    }

    public async Task BatchDirectUpsertJournalEntriesAsync(IEnumerable<JournalEntry> entries, CancellationToken cancellationToken = default)
    {
        if (entries == null) return;
        await EnsureInitializedAsync();

        var entities = entries.Select(JournalEntryEntity.FromDomain).ToList();
        if (entities.Count == 0) return;

        await _database.RunInTransactionAsync(conn =>
        {
            foreach (var entity in entities)
            {
                conn.InsertOrReplace(entity);
            }
        });
    }

    #endregion
}
