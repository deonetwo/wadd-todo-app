using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Wadd.Core.Enums;
using Wadd.Core.Models;
using Wadd.Services;
using Xunit;

namespace Wadd.Tests;

public class SyncEngineTests
{
    private readonly string _testDbPath;

    public SyncEngineTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"wadd_test_{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task AddTodo_CreatesSyncLogEntry()
    {
        var service = new SQLiteTodoService(_testDbPath);
        var item = new TodoItem
        {
            Title = "Test Sync Log",
            Description = "Testing incremental sync log creation"
        };

        var created = await service.AddTodoAsync(item);
        Assert.NotNull(created);

        var pendingLogs = await service.SyncLogRepository.GetPendingLogsAsync();
        var logsList = pendingLogs.ToList();

        Assert.NotEmpty(logsList);
        var log = logsList.FirstOrDefault(l => l.RecordId == created.Id);
        Assert.NotNull(log);
        Assert.Equal(SyncOperation.Insert, log.Operation);
        Assert.False(log.Synced);

        await service.CloseAsync();
        try { File.Delete(_testDbPath); } catch { }
    }

    [Fact]
    public async Task DeleteTodo_PerformsSoftDeleteAndLogsDeleteOperation()
    {
        var service = new SQLiteTodoService(_testDbPath);
        var item = await service.AddTodoAsync(new TodoItem { Title = "Task to Delete" });

        var deleteSuccess = await service.DeleteTodoAsync(item.Id);
        Assert.True(deleteSuccess);

        var activeTodos = await service.GetTodosAsync();
        Assert.DoesNotContain(activeTodos, x => x.Id == item.Id);

        var rawItem = await service.GetRawByIdAsync(item.Id);
        Assert.NotNull(rawItem);
        Assert.True(rawItem.IsDeleted);

        var pendingLogs = await service.SyncLogRepository.GetPendingLogsAsync();
        var deleteLog = pendingLogs.FirstOrDefault(l => l.RecordId == item.Id && l.Operation == SyncOperation.Delete);
        Assert.NotNull(deleteLog);

        await service.CloseAsync();
        try { File.Delete(_testDbPath); } catch { }
    }

    [Fact]
    public void AutoMerge_NonOverlappingFields_SucceedsWithoutConflict()
    {
        var engine = new ConflictResolutionEngine();
        var baseItem = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Original Title",
            Priority = TodoPriority.Low
        };

        var local = new TodoItem
        {
            Id = baseItem.Id,
            Title = "Updated Local Title",
            Priority = TodoPriority.Low
        };

        var cloud = new TodoItem
        {
            Id = baseItem.Id,
            Title = "Original Title",
            Priority = TodoPriority.Critical
        };

        var result = engine.MergeTodoItems(local, cloud, baseItem);

        Assert.False(result.HasConflict);
        Assert.Equal("Updated Local Title", result.MergedItem.Title);
        Assert.Equal(TodoPriority.Critical, result.MergedItem.Priority);
    }

    [Fact]
    public void AutoMerge_OverlappingFields_DetectsConflict()
    {
        var engine = new ConflictResolutionEngine();
        var baseItem = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Original Title"
        };

        var local = new TodoItem
        {
            Id = baseItem.Id,
            Title = "Local Title Variant A"
        };

        var cloud = new TodoItem
        {
            Id = baseItem.Id,
            Title = "Cloud Title Variant B"
        };

        var result = engine.MergeTodoItems(local, cloud, baseItem);

        Assert.True(result.HasConflict);
        Assert.Contains(nameof(TodoItem.Title), result.ConflictingFields);
    }

    [Fact]
    public async Task ConflictRepository_SaveAndResolve_UpdatesConflictStatus()
    {
        var service = new SQLiteTodoService(_testDbPath);
        var conflictRepo = new SQLiteConflictRepository(service.DatabaseConnection);

        var conflict = new SyncConflict
        {
            Id = Guid.NewGuid(),
            TableName = "TodoItem",
            RecordId = Guid.NewGuid(),
            LocalVersionJson = "{}",
            CloudVersionJson = "{}",
            LocalUpdatedAt = DateTime.UtcNow,
            CloudUpdatedAt = DateTime.UtcNow,
            Status = ConflictStatus.Unresolved
        };

        await conflictRepo.AddConflictAsync(conflict);

        var unresolved = (await conflictRepo.GetUnresolvedConflictsAsync()).ToList();
        Assert.Single(unresolved);
        Assert.Equal(conflict.Id, unresolved[0].Id);

        await conflictRepo.ResolveConflictAsync(conflict.Id, ConflictResolutionType.KeepLocal, "{}");

        var unresolvedAfter = (await conflictRepo.GetUnresolvedConflictsAsync()).ToList();
        Assert.Empty(unresolvedAfter);

        var history = (await conflictRepo.GetConflictHistoryAsync()).ToList();
        Assert.Single(history);
        Assert.Equal(ConflictResolutionType.KeepLocal, history[0].ResolutionType);

        await service.CloseAsync();
        try { File.Delete(_testDbPath); } catch { }
    }

    [Fact]
    public void MergeTask_NewerRemoteUpdatedAt_Wins()
    {
        var taskId = Guid.NewGuid();
        var local = new TodoItem
        {
            Id = taskId,
            Title = "Local Title",
            UpdatedAt = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc)
        };
        var remote = new TodoItem
        {
            Id = taskId,
            Title = "Remote Title",
            UpdatedAt = new DateTime(2026, 8, 17, 10, 5, 0, DateTimeKind.Utc)
        };

        var merged = ConflictResolutionEngine.MergeTask(local, remote);

        Assert.Equal("Remote Title", merged.Title);
        Assert.Equal(remote.UpdatedAt, merged.UpdatedAt);
    }

    [Fact]
    public void MergeTask_TombstonePriority_WinsOnSameTimestamp()
    {
        var taskId = Guid.NewGuid();
        var sameTime = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);

        var local = new TodoItem
        {
            Id = taskId,
            Title = "Active Item",
            IsDeleted = false,
            UpdatedAt = sameTime
        };
        var remote = new TodoItem
        {
            Id = taskId,
            Title = "Active Item",
            IsDeleted = true,
            DeletedAt = sameTime,
            UpdatedAt = sameTime
        };

        var merged = ConflictResolutionEngine.MergeTask(local, remote);

        Assert.True(merged.IsDeleted);
    }

    [Fact]
    public void MergeTask_RestoredTaskWithNewerTimestamp_WinsOverTombstone()
    {
        var taskId = Guid.NewGuid();
        var deletedTime = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);
        var restoredTime = new DateTime(2026, 8, 17, 12, 10, 0, DateTimeKind.Utc);

        var remoteTombstone = new TodoItem
        {
            Id = taskId,
            Title = "Deleted Item",
            IsDeleted = true,
            DeletedAt = deletedTime,
            UpdatedAt = deletedTime
        };

        var localRestored = new TodoItem
        {
            Id = taskId,
            Title = "Restored Item",
            IsDeleted = false,
            DeletedAt = null,
            UpdatedAt = restoredTime
        };

        var merged = ConflictResolutionEngine.MergeTask(localRestored, remoteTombstone);

        Assert.False(merged.IsDeleted);
        Assert.Equal("Restored Item", merged.Title);
        Assert.Equal(restoredTime, merged.UpdatedAt);
    }

    [Fact]
    public void GenerateDeterministicRecurringId_SameSeriesAndDate_YieldsIdenticalGuid()
    {
        var seriesId = Guid.NewGuid();
        var dueDate = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);

        var id1 = Wadd.Core.Helpers.RecurrenceHelper.GenerateDeterministicRecurringId(seriesId, dueDate);
        var id2 = Wadd.Core.Helpers.RecurrenceHelper.GenerateDeterministicRecurringId(seriesId, dueDate);

        Assert.Equal(id1, id2);
        Assert.NotEqual(Guid.Empty, id1);
    }

    [Fact]
    public async Task UpdateTodo_WhenUnchanged_DoesNotBumpVersionOrLogSync()
    {
        var service = new SQLiteTodoService(_testDbPath);
        var item = await service.AddTodoAsync(new TodoItem
        {
            Title = "Task A",
            Description = "Desc A",
            Priority = TodoPriority.High
        });

        // Mark all logs as synced to isolate the update test
        var initialLogs = await service.SyncLogRepository.GetPendingLogsAsync();
        await service.SyncLogRepository.MarkLogsAsSyncedAsync(initialLogs.Select(l => l.Id));

        var originalVersion = item.Version;
        var originalUpdated = item.UpdatedAt;

        // Call update with identical content
        var clone = new TodoItem
        {
            Id = item.Id,
            Title = item.Title,
            Description = item.Description,
            Priority = item.Priority,
            Category = item.Category,
            DueDate = item.DueDate,
            ReminderAt = item.ReminderAt,
            IsCompleted = item.IsCompleted,
            IsDeleted = item.IsDeleted,
            IsRecurring = item.IsRecurring,
            RecurrenceType = item.RecurrenceType,
            CustomRecurrenceInterval = item.CustomRecurrenceInterval,
            CustomRecurrenceUnit = item.CustomRecurrenceUnit,
            CustomWeeklyDays = item.CustomWeeklyDays,
            Version = originalVersion,
            UpdatedAt = originalUpdated
        };

        var updateResult = await service.UpdateTodoAsync(clone);
        Assert.True(updateResult);

        // Version and timestamps should remain unchanged
        Assert.Equal(originalVersion, clone.Version);
        Assert.Equal(originalUpdated, clone.UpdatedAt);

        // No new pending sync logs should be created
        var pendingLogsAfter = await service.SyncLogRepository.GetPendingLogsAsync();
        Assert.Empty(pendingLogsAfter);

        await service.CloseAsync();
        try { File.Delete(_testDbPath); } catch { }
    }

    [Fact]
    public async Task UpdateTodo_WhenChanged_BumpsVersionAndLogsSync()
    {
        var service = new SQLiteTodoService(_testDbPath);
        var item = await service.AddTodoAsync(new TodoItem
        {
            Title = "Original Title",
            Description = "Original Desc"
        });

        var initialLogs = await service.SyncLogRepository.GetPendingLogsAsync();
        await service.SyncLogRepository.MarkLogsAsSyncedAsync(initialLogs.Select(l => l.Id));

        var originalVersion = item.Version;

        item.Title = "Updated Title";
        var updateResult = await service.UpdateTodoAsync(item);
        Assert.True(updateResult);

        Assert.Equal(originalVersion + 1, item.Version);

        var pendingLogsAfter = await service.SyncLogRepository.GetPendingLogsAsync();
        var updateLog = pendingLogsAfter.FirstOrDefault(l => l.RecordId == item.Id && l.Operation == SyncOperation.Update);
        Assert.NotNull(updateLog);

        await service.CloseAsync();
        try { File.Delete(_testDbPath); } catch { }
    }
}
