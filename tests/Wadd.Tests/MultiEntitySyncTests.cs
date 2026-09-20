using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
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

    #region Google Drive Http Integration Tests (Pagination, Migration Concurrency, Active Pruning)

    [Fact]
    public async Task ListAppDataFolderFilesAsync_WithNextPageToken_ReturnsAllPages()
    {
        var fakeHandler = new FakeDriveHttpMessageHandler();
        fakeHandler.PagedFiles = new List<List<(string Id, string Name, DateTime ModifiedTime)>>
        {
            new()
            {
                ("f-1", "file1.json", DateTime.UtcNow),
                ("f-2", "file2.json", DateTime.UtcNow),
                ("f-3", "file3.json", DateTime.UtcNow)
            },
            new()
            {
                ("f-4", "file4.json", DateTime.UtcNow),
                ("f-5", "file5.json", DateTime.UtcNow),
                ("f-6", "file6.json", DateTime.UtcNow)
            },
            new()
            {
                ("f-7", "file7.json", DateTime.UtcNow),
                ("f-8", "file8.json", DateTime.UtcNow)
            }
        };

        using var httpClient = new HttpClient(fakeHandler);
        var syncService = new GoogleDriveSyncService(_todoService, httpClient: httpClient, goalService: _goalService);

        var result = await syncService.ListAppDataFolderFilesAsync("fake-token", CancellationToken.None);

        Assert.Equal(8, result.Count);
        Assert.Equal("file1.json", result[0].Name);
        Assert.Equal("file4.json", result[3].Name);
        Assert.Equal("file8.json", result[7].Name);
    }

    [Fact]
    public async Task EnsureManifestMigrationAsync_With55LegacyFiles_CompletesConcurrently_DeletesAll_UploadsManifests()
    {
        var fakeHandler = new FakeDriveHttpMessageHandler();
        var remoteFiles = new List<GoogleDriveSyncService.DriveFileItem>();
        var remoteFileMap = new Dictionary<string, GoogleDriveSyncService.DriveFileItem>(StringComparer.OrdinalIgnoreCase);
        var fileIdMap = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. 15 Tasks
        for (int i = 0; i < 15; i++)
        {
            var id = Guid.NewGuid();
            string fileName = $"{id}.json";
            string fileId = $"legacy-task-{i}";
            var item = new TodoItem { Id = id, Title = $"Legacy Task {i}", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            string json = JsonSerializer.Serialize(item);
            fakeHandler.DriveFiles[fileId] = (fileName, json, DateTime.UtcNow);
            var driveItem = new GoogleDriveSyncService.DriveFileItem(fileId, fileName, DateTime.UtcNow);
            remoteFiles.Add(driveItem);
            remoteFileMap[fileName] = driveItem;
            fileIdMap[fileName] = fileId;
        }

        // 2. 10 Goals
        for (int i = 0; i < 10; i++)
        {
            string id = $"goal-{Guid.NewGuid():N}";
            string fileName = $"goal_{id}.json";
            string fileId = $"legacy-goal-{i}";
            var item = new LifeGoal { Id = id, Title = $"Legacy Goal {i}", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            string json = JsonSerializer.Serialize(item);
            fakeHandler.DriveFiles[fileId] = (fileName, json, DateTime.UtcNow);
            var driveItem = new GoogleDriveSyncService.DriveFileItem(fileId, fileName, DateTime.UtcNow);
            remoteFiles.Add(driveItem);
            remoteFileMap[fileName] = driveItem;
            fileIdMap[fileName] = fileId;
        }

        // 3. 10 Milestones
        for (int i = 0; i < 10; i++)
        {
            string id = $"milestone-{Guid.NewGuid():N}";
            string fileName = $"milestone_{id}.json";
            string fileId = $"legacy-milestone-{i}";
            var item = new GoalMilestone { Id = id, GoalId = "goal-1", Title = $"Legacy Milestone {i}", UpdatedAt = DateTime.UtcNow };
            string json = JsonSerializer.Serialize(item);
            fakeHandler.DriveFiles[fileId] = (fileName, json, DateTime.UtcNow);
            var driveItem = new GoogleDriveSyncService.DriveFileItem(fileId, fileName, DateTime.UtcNow);
            remoteFiles.Add(driveItem);
            remoteFileMap[fileName] = driveItem;
            fileIdMap[fileName] = fileId;
        }

        // 4. 10 Journals
        for (int i = 0; i < 10; i++)
        {
            string id = $"journal-{Guid.NewGuid():N}";
            string fileName = $"journal_{id}.json";
            string fileId = $"legacy-journal-{i}";
            var item = new JournalEntry { Id = id, Title = $"Legacy Journal {i}", Content = "Content", EntryDate = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            string json = JsonSerializer.Serialize(item);
            fakeHandler.DriveFiles[fileId] = (fileName, json, DateTime.UtcNow);
            var driveItem = new GoogleDriveSyncService.DriveFileItem(fileId, fileName, DateTime.UtcNow);
            remoteFiles.Add(driveItem);
            remoteFileMap[fileName] = driveItem;
            fileIdMap[fileName] = fileId;
        }

        // 5. 10 Date Notes
        for (int i = 1; i <= 10; i++)
        {
            string dateKey = $"2026-09-{i:D2}";
            string fileName = $"datenote_{dateKey}.json";
            string fileId = $"legacy-datenote-{i}";
            var item = new CalendarDateNote { DateKey = dateKey, NoteText = $"Note {i}", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            string json = JsonSerializer.Serialize(item);
            fakeHandler.DriveFiles[fileId] = (fileName, json, DateTime.UtcNow);
            var driveItem = new GoogleDriveSyncService.DriveFileItem(fileId, fileName, DateTime.UtcNow);
            remoteFiles.Add(driveItem);
            remoteFileMap[fileName] = driveItem;
            fileIdMap[fileName] = fileId;
        }

        Assert.Equal(55, remoteFiles.Count);

        using var httpClient = new HttpClient(fakeHandler);
        var syncService = new GoogleDriveSyncService(_todoService, httpClient: httpClient, goalService: _goalService);
        syncService.SetAuthRecordForTesting(new UserAuthRecord { IsSignedIn = true, AccessToken = "test-token" });

        // (a) Runs under real concurrency (16 semaphore) without throwing
        await syncService.EnsureManifestMigrationAsync("test-token", remoteFiles, remoteFileMap, fileIdMap, CancellationToken.None);

        // (b) All 55 legacy files are deleted
        Assert.Equal(55, fakeHandler.DeletedFileIds.Count);

        // (c) Each manifest file is uploaded with the correct merged content
        Assert.True(fakeHandler.UploadedManifests.ContainsKey("tasks.json"));
        Assert.True(fakeHandler.UploadedManifests.ContainsKey("goals.json"));
        Assert.True(fakeHandler.UploadedManifests.ContainsKey("milestones.json"));
        Assert.True(fakeHandler.UploadedManifests.ContainsKey("journals.json"));
        Assert.True(fakeHandler.UploadedManifests.ContainsKey("datenotes.json"));

        var uploadedTasks = JsonSerializer.Deserialize<List<TodoItem>>(fakeHandler.UploadedManifests["tasks.json"]);
        var uploadedGoals = JsonSerializer.Deserialize<List<LifeGoal>>(fakeHandler.UploadedManifests["goals.json"]);
        var uploadedMilestones = JsonSerializer.Deserialize<List<GoalMilestone>>(fakeHandler.UploadedManifests["milestones.json"]);
        var uploadedJournals = JsonSerializer.Deserialize<List<JournalEntry>>(fakeHandler.UploadedManifests["journals.json"]);
        var uploadedDateNotes = JsonSerializer.Deserialize<List<CalendarDateNote>>(fakeHandler.UploadedManifests["datenotes.json"]);

        Assert.Equal(15, uploadedTasks!.Count);
        Assert.Equal(10, uploadedGoals!.Count);
        Assert.Equal(10, uploadedMilestones!.Count);
        Assert.Equal(10, uploadedJournals!.Count);
        Assert.Equal(10, uploadedDateNotes!.Count);

        // (d) MigratedToManifests is true
        Assert.True(syncService.MigratedToManifests);

        // (e) Running a second time is an immediate no-op
        fakeHandler.DeletedFileIds.Clear();
        int uploadedCountBefore = fakeHandler.UploadedManifests.Count;
        await syncService.EnsureManifestMigrationAsync("test-token", remoteFiles, remoteFileMap, fileIdMap, CancellationToken.None);
        Assert.Empty(fakeHandler.DeletedFileIds);
        Assert.Equal(uploadedCountBefore, fakeHandler.UploadedManifests.Count);
    }

    [Fact]
    public async Task PruneExpiredCloudTombstonesAsync_ManifestWithExpiredTombstone_GetsReuploaded_CleanManifestUntouched()
    {
        var fakeHandler = new FakeDriveHttpMessageHandler();
        var remoteFileMap = new Dictionary<string, GoogleDriveSyncService.DriveFileItem>(StringComparer.OrdinalIgnoreCase);
        var fileIdMap = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 1. Expired Task (> 90 days ago) in local DB and remote tasks.json
        var expiredTaskId = Guid.NewGuid();
        var expiredTask = new TodoItem
        {
            Id = expiredTaskId,
            Title = "Expired Deleted Task",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow.AddDays(-95),
            UpdatedAt = DateTime.UtcNow.AddDays(-95)
        };
        await _todoService.BatchDirectUpsertFromSyncAsync(new[] { expiredTask });

        string tasksFileId = "drive-tasks-id";
        string tasksJson = JsonSerializer.Serialize(new List<TodoItem> { expiredTask });
        fakeHandler.DriveFiles[tasksFileId] = ("tasks.json", tasksJson, DateTime.UtcNow.AddDays(-1));
        remoteFileMap["tasks.json"] = new GoogleDriveSyncService.DriveFileItem(tasksFileId, "tasks.json", DateTime.UtcNow.AddDays(-1));
        fileIdMap["tasks.json"] = tasksFileId;

        // 2. Active Goal in local DB and remote goals.json (no expired tombstones)
        var activeGoal = new LifeGoal
        {
            Id = "active-goal-1",
            Title = "Active Goal",
            IsDeleted = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _goalService.BatchDirectUpsertGoalsAsync(new[] { activeGoal });

        string goalsFileId = "drive-goals-id";
        string goalsJson = JsonSerializer.Serialize(new List<LifeGoal> { activeGoal });
        fakeHandler.DriveFiles[goalsFileId] = ("goals.json", goalsJson, DateTime.UtcNow.AddDays(-1));
        remoteFileMap["goals.json"] = new GoogleDriveSyncService.DriveFileItem(goalsFileId, "goals.json", DateTime.UtcNow.AddDays(-1));
        fileIdMap["goals.json"] = goalsFileId;

        using var httpClient = new HttpClient(fakeHandler);
        var syncService = new GoogleDriveSyncService(_todoService, httpClient: httpClient, goalService: _goalService);
        syncService.SetAuthRecordForTesting(new UserAuthRecord { IsSignedIn = true, AccessToken = "test-token", MigratedToManifests = true });

        // Act: Run tombstone pruning pass
        await syncService.PruneExpiredCloudTombstonesAsync("test-token", remoteFileMap, fileIdMap, CancellationToken.None);

        // Assert: tasks.json was re-uploaded without the expired tombstone (empty list)
        Assert.True(fakeHandler.UploadedManifests.ContainsKey("tasks.json"));
        var prunedTasks = JsonSerializer.Deserialize<List<TodoItem>>(fakeHandler.UploadedManifests["tasks.json"]);
        Assert.NotNull(prunedTasks);
        Assert.Empty(prunedTasks);

        // Assert: goals.json has no expired tombstones, so it was left untouched (never uploaded)
        Assert.False(fakeHandler.UploadedManifests.ContainsKey("goals.json"));
    }

    #endregion
}

public class FakeDriveHttpMessageHandler : HttpMessageHandler
{
    public ConcurrentDictionary<string, (string Name, string Content, DateTime ModifiedTime)> DriveFiles { get; }
        = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentBag<string> DeletedFileIds { get; } = new();
    public ConcurrentDictionary<string, string> UploadedManifests { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentBag<HttpRequestMessage> CapturedRequests { get; } = new();

    public List<List<(string Id, string Name, DateTime ModifiedTime)>>? PagedFiles { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CapturedRequests.Add(request);
        var uri = request.RequestUri?.ToString() ?? string.Empty;

        // 1. List files in appDataFolder
        if (request.Method == HttpMethod.Get && uri.Contains("/files?") && uri.Contains("spaces=appDataFolder"))
        {
            if (PagedFiles != null && PagedFiles.Count > 0)
            {
                string? pageToken = null;
                var idx = uri.IndexOf("pageToken=", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var tokenPart = uri.Substring(idx + 10);
                    var amp = tokenPart.IndexOf('&');
                    pageToken = amp >= 0 ? tokenPart.Substring(0, amp) : tokenPart;
                    pageToken = Uri.UnescapeDataString(pageToken);
                }

                int pageIndex = 0;
                if (!string.IsNullOrEmpty(pageToken) && int.TryParse(pageToken, out var pi))
                {
                    pageIndex = pi;
                }

                if (pageIndex < PagedFiles.Count)
                {
                    var page = PagedFiles[pageIndex];
                    string? nextToken = pageIndex + 1 < PagedFiles.Count ? (pageIndex + 1).ToString() : null;

                    var responseObj = new
                    {
                        nextPageToken = nextToken,
                        files = page.Select(f => new
                        {
                            id = f.Id,
                            name = f.Name,
                            modifiedTime = f.ModifiedTime.ToString("o")
                        })
                    };

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(responseObj), Encoding.UTF8, "application/json")
                    };
                }
            }

            // Single page default
            var allFilesResponse = new
            {
                files = DriveFiles.Select(kvp => new
                {
                    id = kvp.Key,
                    name = kvp.Value.Name,
                    modifiedTime = kvp.Value.ModifiedTime.ToString("o")
                })
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(allFilesResponse), Encoding.UTF8, "application/json")
            };
        }

        // 2. Download file: GET /files/{id}?alt=media
        if (request.Method == HttpMethod.Get && uri.Contains("/files/") && uri.Contains("alt=media"))
        {
            var segments = request.RequestUri!.AbsolutePath.Split('/');
            var fileId = segments[^1];
            if (DriveFiles.TryGetValue(fileId, out var fileData))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(fileData.Content, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        // 3. Delete file: DELETE /files/{id}
        if (request.Method == HttpMethod.Delete && uri.Contains("/files/"))
        {
            var segments = request.RequestUri!.AbsolutePath.Split('/');
            var fileId = segments[^1];
            DeletedFileIds.Add(fileId);
            DriveFiles.TryRemove(fileId, out _);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        // 4. Upload multipart (POST /files?uploadType=multipart)
        if (request.Method == HttpMethod.Post && uri.Contains("/files") && uri.Contains("uploadType=multipart"))
        {
            string newId = "file-" + Guid.NewGuid().ToString("N");
            string fileName = "unknown";
            string payload = "";

            if (request.Content is MultipartContent mc)
            {
                using var enumerator = mc.GetEnumerator();
                if (enumerator.MoveNext())
                {
                    var metaJson = await enumerator.Current.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(metaJson);
                    if (doc.RootElement.TryGetProperty("name", out var np)) fileName = np.GetString() ?? fileName;
                }
                if (enumerator.MoveNext())
                {
                    payload = await enumerator.Current.ReadAsStringAsync(cancellationToken);
                }
            }
            else if (request.Content != null)
            {
                payload = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            DriveFiles[newId] = (fileName, payload, DateTime.UtcNow);
            UploadedManifests[fileName] = payload;

            var resp = new { id = newId, name = fileName };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(resp), Encoding.UTF8, "application/json")
            };
        }

        // 5. Patch media (PATCH /files/{id}?uploadType=media)
        if (request.Method == HttpMethod.Patch && uri.Contains("/files/") && uri.Contains("uploadType=media"))
        {
            var segments = request.RequestUri!.AbsolutePath.Split('/');
            var fileId = segments[^1];
            string payload = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : "";

            if (DriveFiles.TryGetValue(fileId, out var existing))
            {
                DriveFiles[fileId] = (existing.Name, payload, DateTime.UtcNow);
                UploadedManifests[existing.Name] = payload;
            }
            else
            {
                DriveFiles[fileId] = (fileId, payload, DateTime.UtcNow);
                UploadedManifests[fileId] = payload;
            }

            var resp = new { id = fileId };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(resp), Encoding.UTF8, "application/json")
            };
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
    }
}
