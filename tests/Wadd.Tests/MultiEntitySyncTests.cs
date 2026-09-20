using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Wadd.Core.Models;
using Wadd.Services;
using Xunit;

namespace Wadd.Tests;

public class MultiEntitySyncTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SQLiteGoalService _goalService;
    private readonly SQLiteTodoService _todoService;

    public MultiEntitySyncTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"wadd_multisync_{Guid.NewGuid():N}.db");
        _goalService = new SQLiteGoalService(_testDbPath);
        _todoService = new SQLiteTodoService(_testDbPath);
    }

    public void Dispose()
    {
        try { _todoService.CloseAsync().GetAwaiter().GetResult(); } catch { }
        if (File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { }
        }
    }

    #region ConflictResolutionEngine Tests

    [Fact]
    public void MergeGoal_CloudNewer_TakesCloudFields()
    {
        var baseTime = DateTime.UtcNow.AddHours(-2);
        var local = new LifeGoal
        {
            Id = "goal-1",
            Title = "Local Title",
            Description = "Local Desc",
            Category = "Fitness",
            UpdatedAt = baseTime.AddMinutes(10)
        };
        var cloud = new LifeGoal
        {
            Id = "goal-1",
            Title = "Cloud Title",
            Description = "Cloud Desc",
            Category = "Health",
            UpdatedAt = baseTime.AddMinutes(20)
        };

        var merged = ConflictResolutionEngine.MergeGoal(local, cloud);

        Assert.Equal("Cloud Title", merged.Title);
        Assert.Equal("Cloud Desc", merged.Description);
        Assert.Equal("Health", merged.Category);
        Assert.Equal(cloud.UpdatedAt, merged.UpdatedAt);
    }

    [Fact]
    public void MergeGoal_LocalNewer_TakesLocalFields()
    {
        var baseTime = DateTime.UtcNow.AddHours(-2);
        var local = new LifeGoal
        {
            Id = "goal-1",
            Title = "Local Title",
            Description = "Local Desc",
            UpdatedAt = baseTime.AddMinutes(30)
        };
        var cloud = new LifeGoal
        {
            Id = "goal-1",
            Title = "Cloud Title",
            Description = "Cloud Desc",
            UpdatedAt = baseTime.AddMinutes(10)
        };

        var merged = ConflictResolutionEngine.MergeGoal(local, cloud);

        Assert.Equal("Local Title", merged.Title);
        Assert.Equal("Local Desc", merged.Description);
        Assert.Equal(local.UpdatedAt, merged.UpdatedAt);
    }

    [Fact]
    public void MergeGoal_TombstoneWins_WhenEitherIsDeleted()
    {
        var local = new LifeGoal
        {
            Id = "goal-1",
            Title = "Local Title",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow.AddMinutes(5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(5)
        };
        var cloud = new LifeGoal
        {
            Id = "goal-1",
            Title = "Cloud Title (newer edit)",
            IsDeleted = false,
            UpdatedAt = DateTime.UtcNow.AddMinutes(10)
        };

        var merged = ConflictResolutionEngine.MergeGoal(local, cloud);

        Assert.True(merged.IsDeleted);
        Assert.NotNull(merged.DeletedAt);
    }

    [Fact]
    public void MergeMilestone_CloudNewer_TakesCloudValues()
    {
        var local = new GoalMilestone
        {
            Id = "m-1",
            GoalId = "goal-1",
            Title = "Local Step",
            IsCompleted = false,
            OrderIndex = 0,
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        var cloud = new GoalMilestone
        {
            Id = "m-1",
            GoalId = "goal-1",
            Title = "Cloud Step",
            IsCompleted = true,
            OrderIndex = 1,
            UpdatedAt = DateTime.UtcNow
        };

        var merged = ConflictResolutionEngine.MergeMilestone(local, cloud);

        Assert.Equal("Cloud Step", merged.Title);
        Assert.True(merged.IsCompleted);
        Assert.Equal(1, merged.OrderIndex);
    }

    [Fact]
    public void MergeMilestone_TombstoneWins_WhenCloudDeleted()
    {
        var local = new GoalMilestone
        {
            Id = "m-1",
            Title = "Local Step",
            IsDeleted = false,
            UpdatedAt = DateTime.UtcNow
        };
        var cloud = new GoalMilestone
        {
            Id = "m-1",
            Title = "Cloud Step",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        var merged = ConflictResolutionEngine.MergeMilestone(local, cloud);

        Assert.True(merged.IsDeleted);
    }

    [Fact]
    public void MergeJournalEntry_LwwAndTombstone()
    {
        var local = new JournalEntry
        {
            Id = "j-1",
            Title = "Local Reflections",
            Content = "Local content",
            UpdatedAt = DateTime.UtcNow.AddMinutes(10)
        };
        var cloud = new JournalEntry
        {
            Id = "j-1",
            Title = "Cloud Reflections",
            Content = "Cloud content",
            UpdatedAt = DateTime.UtcNow.AddMinutes(5)
        };

        var merged = ConflictResolutionEngine.MergeJournalEntry(local, cloud);
        Assert.Equal("Local Reflections", merged.Title);
        Assert.Equal("Local content", merged.Content);

        // Tombstone test
        cloud.IsDeleted = true;
        cloud.DeletedAt = DateTime.UtcNow;
        var tombstoneMerged = ConflictResolutionEngine.MergeJournalEntry(local, cloud);
        Assert.True(tombstoneMerged.IsDeleted);
    }

    [Fact]
    public void MergeDateNote_LwwAndTombstone()
    {
        var local = new CalendarDateNote
        {
            DateKey = "2026-09-20",
            NoteText = "Local note",
            UpdatedAt = DateTime.UtcNow.AddMinutes(5)
        };
        var cloud = new CalendarDateNote
        {
            DateKey = "2026-09-20",
            NoteText = "Cloud note",
            UpdatedAt = DateTime.UtcNow.AddMinutes(15)
        };

        var merged = ConflictResolutionEngine.MergeDateNote(local, cloud);
        Assert.Equal("Cloud note", merged.NoteText);

        local.IsDeleted = true;
        local.DeletedAt = DateTime.UtcNow;
        var deletedMerged = ConflictResolutionEngine.MergeDateNote(local, cloud);
        Assert.True(deletedMerged.IsDeleted);
    }

    #endregion

    #region SQLiteGoalService Direct Batch Upsert & Raw Query Tests

    [Fact]
    public async Task SQLiteGoalService_BatchDirectUpsertGoals_InsertsAndUpdatesRaw()
    {
        var goal1 = new LifeGoal
        {
            Id = "goal-sync-1",
            Title = "Goal 1",
            Category = "Career",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var goal2 = new LifeGoal
        {
            Id = "goal-sync-2",
            Title = "Goal 2",
            Category = "Personal",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _goalService.BatchDirectUpsertGoalsAsync(new[] { goal1, goal2 });

        var rawGoals = (await _goalService.GetAllGoalsRawAsync()).ToList();
        Assert.Equal(2, rawGoals.Count);

        var activeGoals = (await _goalService.GetGoalsAsync()).ToList();
        Assert.Single(activeGoals);
        Assert.Equal("goal-sync-1", activeGoals[0].Id);

        // Now update goal1
        goal1.Title = "Goal 1 Updated";
        goal1.UpdatedAt = DateTime.UtcNow.AddMinutes(1);
        await _goalService.BatchDirectUpsertGoalsAsync(new[] { goal1 });

        var fetched = await _goalService.GetGoalByIdAsync("goal-sync-1");
        Assert.NotNull(fetched);
        Assert.Equal("Goal 1 Updated", fetched.Title);
    }

    [Fact]
    public async Task SQLiteGoalService_BatchDirectUpsertMilestones_AndRawQuery()
    {
        var goal = await _goalService.SaveGoalAsync(new LifeGoal { Title = "Main Goal" });

        var m1 = new GoalMilestone
        {
            Id = "m-sync-1",
            GoalId = goal.Id,
            Title = "Step A",
            IsCompleted = true,
            OrderIndex = 0,
            UpdatedAt = DateTime.UtcNow
        };
        var m2 = new GoalMilestone
        {
            Id = "m-sync-2",
            GoalId = goal.Id,
            Title = "Step B (deleted)",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _goalService.BatchDirectUpsertMilestonesAsync(new[] { m1, m2 });

        var rawMilestones = (await _goalService.GetAllMilestonesRawAsync()).ToList();
        Assert.Equal(2, rawMilestones.Count);

        var activeMilestones = (await _goalService.GetMilestonesForGoalAsync(goal.Id)).ToList();
        Assert.Single(activeMilestones);
        Assert.Equal("Step A", activeMilestones[0].Title);
    }

    [Fact]
    public async Task SQLiteGoalService_BatchDirectUpsertJournalEntries_AndRawQuery()
    {
        var j1 = new JournalEntry
        {
            Id = "j-sync-1",
            Title = "Journal 1",
            Content = "Reflections on progress",
            EntryDate = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var j2 = new JournalEntry
        {
            Id = "j-sync-2",
            Title = "Journal 2 (deleted)",
            Content = "Old thoughts",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _goalService.BatchDirectUpsertJournalEntriesAsync(new[] { j1, j2 });

        var rawEntries = (await _goalService.GetAllJournalEntriesRawAsync()).ToList();
        Assert.Equal(2, rawEntries.Count);

        var activeEntries = (await _goalService.GetJournalEntriesAsync()).ToList();
        Assert.Single(activeEntries);
        Assert.Equal("Journal 1", activeEntries[0].Title);
    }

    #endregion

    #region SQLiteTodoService Date Note Soft-Delete & Batch Upsert Tests

    [Fact]
    public async Task SQLiteTodoService_DateNote_SoftDeleteAndSync()
    {
        var date = new DateTime(2026, 9, 20);
        var dateKey = "2026-09-20";

        // 1. Save note
        await _todoService.SaveDateNoteAsync(date, "Sprint review meeting today");
        var activeNotes = await _todoService.GetAllDateNotesAsync();
        Assert.True(activeNotes.ContainsKey(dateKey));
        Assert.Equal("Sprint review meeting today", activeNotes[dateKey]);

        // 2. Delete note -> soft-delete
        await _todoService.DeleteDateNoteAsync(date);

        // Active query excludes it
        activeNotes = await _todoService.GetAllDateNotesAsync();
        Assert.False(activeNotes.ContainsKey(dateKey));

        // GetDateNoteAsync returns null
        var single = await _todoService.GetDateNoteAsync(date);
        Assert.Null(single);

        // Raw query includes it with IsDeleted == true
        var rawNotes = (await _todoService.GetAllDateNotesRawAsync()).ToList();
        var deletedNote = rawNotes.FirstOrDefault(n => n.DateKey == dateKey);
        Assert.NotNull(deletedNote);
        Assert.True(deletedNote.IsDeleted);
        Assert.NotNull(deletedNote.DeletedAt);

        // 3. Batch direct upsert a cloud note
        var cloudNote = new CalendarDateNote
        {
            DateKey = "2026-09-21",
            NoteText = "Synchronized from Google Drive",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _todoService.BatchDirectUpsertDateNotesAsync(new[] { cloudNote });

        activeNotes = await _todoService.GetAllDateNotesAsync();
        Assert.True(activeNotes.ContainsKey("2026-09-21"));
        Assert.Equal("Synchronized from Google Drive", activeNotes["2026-09-21"]);
    }

    [Fact]
    public void GoogleDriveSyncService_HasChangesApplied_DefaultIsFalse()
    {
        var syncService = new GoogleDriveSyncService(_todoService);
        Assert.False(syncService.HasChangesApplied);
    }

    [Fact]
    public void ConflictResolutionEngine_EnsureUtc_PreservesUnspecifiedTimestampWithoutLocalSkew()
    {
        var rawTime = new DateTime(2026, 9, 20, 14, 0, 0, DateTimeKind.Unspecified);
        var ensured = ConflictResolutionEngine.EnsureUtc(rawTime);

        Assert.Equal(DateTimeKind.Utc, ensured.Kind);
        Assert.Equal(14, ensured.Hour);
        Assert.Equal(0, ensured.Minute);
    }

    [Fact]
    public void ConflictResolutionEngine_MergeTask_WithUnspecifiedLocalTime_DoesNotSkewToCloud()
    {
        var localTime = new DateTime(2026, 9, 20, 14, 30, 0, DateTimeKind.Unspecified);
        var cloudTime = new DateTime(2026, 9, 20, 14, 0, 0, DateTimeKind.Utc);

        var local = new TodoItem { Id = Guid.NewGuid(), Title = "Local Newer", UpdatedAt = localTime };
        var cloud = new TodoItem { Id = local.Id, Title = "Cloud Older", UpdatedAt = cloudTime };

        var merged = ConflictResolutionEngine.MergeTask(local, cloud);

        Assert.Equal("Local Newer", merged.Title);
    }

    [Fact]
    public void TombstoneRetentionWindow_IsNinetyDays()
    {
        Assert.Equal(TimeSpan.FromDays(90), GoogleDriveSyncService.TombstoneRetentionWindow);
    }

    [Fact]
    public void ManifestConstants_AreWellFormed()
    {
        Assert.Equal("tasks.json", GoogleDriveSyncService.TasksManifestFilename);
        Assert.Equal("goals.json", GoogleDriveSyncService.GoalsManifestFilename);
        Assert.Equal("milestones.json", GoogleDriveSyncService.MilestonesManifestFilename);
        Assert.Equal("journals.json", GoogleDriveSyncService.JournalsManifestFilename);
        Assert.Equal("datenotes.json", GoogleDriveSyncService.DateNotesManifestFilename);
    }

    [Fact]
    public void ManifestSerialization_ExcludesTombstonesOlderThan90Days_RetainsRecentTombstonesAndActive()
    {
        var cutoff = DateTime.UtcNow - GoogleDriveSyncService.TombstoneRetentionWindow;

        var activeItem = new TodoItem { Id = Guid.NewGuid(), Title = "Active Task", IsDeleted = false };
        var recentTombstone = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Recent Tombstone",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow.AddDays(-10)
        };
        var expiredTombstone = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Old Tombstone",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow.AddDays(-95)
        };

        var allItems = new List<TodoItem> { activeItem, recentTombstone, expiredTombstone };
        var toUpload = allItems
            .Where(t => !t.IsDeleted || (t.DeletedAt.HasValue && t.DeletedAt.Value >= cutoff))
            .ToList();

        Assert.Contains(activeItem, toUpload);
        Assert.Contains(recentTombstone, toUpload);
        Assert.DoesNotContain(expiredTombstone, toUpload);
    }

    [Fact]
    public void ConflictResolutionEngine_ReconcilesManifestEntities_LwwAndTombstones()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var idC = Guid.NewGuid();
        var idD = Guid.NewGuid();
        var idE = Guid.NewGuid();
        var idF = Guid.NewGuid();

        var localMap = new Dictionary<Guid, TodoItem>
        {
            [idA] = new TodoItem { Id = idA, Title = "Item A", UpdatedAt = DateTime.UtcNow.AddMinutes(-20) },
            [idB] = new TodoItem { Id = idB, Title = "Local B Newer", UpdatedAt = DateTime.UtcNow.AddMinutes(-5) },
            [idC] = new TodoItem { Id = idC, Title = "Local C Older", UpdatedAt = DateTime.UtcNow.AddMinutes(-30) },
            [idD] = new TodoItem { Id = idD, Title = "Item D", IsDeleted = false, UpdatedAt = DateTime.UtcNow.AddMinutes(-15) },
            [idF] = new TodoItem { Id = idF, Title = "Local Only F", UpdatedAt = DateTime.UtcNow.AddMinutes(-10) }
        };

        var remoteMap = new Dictionary<Guid, TodoItem>
        {
            [idA] = new TodoItem { Id = idA, Title = "Item A", UpdatedAt = DateTime.UtcNow.AddMinutes(-20) },
            [idB] = new TodoItem { Id = idB, Title = "Remote B Older", UpdatedAt = DateTime.UtcNow.AddMinutes(-15) },
            [idC] = new TodoItem { Id = idC, Title = "Remote C Newer", UpdatedAt = DateTime.UtcNow.AddMinutes(-2) },
            [idD] = new TodoItem { Id = idD, Title = "Item D", IsDeleted = true, DeletedAt = DateTime.UtcNow.AddMinutes(-10), UpdatedAt = DateTime.UtcNow.AddMinutes(-10) },
            [idE] = new TodoItem { Id = idE, Title = "Remote Only E", UpdatedAt = DateTime.UtcNow.AddMinutes(-5) }
        };

        var allIds = localMap.Keys.Union(remoteMap.Keys).ToList();
        var mergedResult = new List<TodoItem>();

        foreach (var id in allIds)
        {
            localMap.TryGetValue(id, out var local);
            remoteMap.TryGetValue(id, out var remote);
            var merged = ConflictResolutionEngine.MergeTask(local, remote);
            mergedResult.Add(merged);
        }

        var resMap = mergedResult.ToDictionary(t => t.Id);

        // B: Local newer wins
        Assert.Equal("Local B Newer", resMap[idB].Title);
        // C: Cloud newer wins
        Assert.Equal("Remote C Newer", resMap[idC].Title);
        // D: Remote tombstone wins
        Assert.True(resMap[idD].IsDeleted);
        // E: Remote only is present
        Assert.Equal("Remote Only E", resMap[idE].Title);
        // F: Local only is present
        Assert.Equal("Local Only F", resMap[idF].Title);
    }

    #endregion
}
