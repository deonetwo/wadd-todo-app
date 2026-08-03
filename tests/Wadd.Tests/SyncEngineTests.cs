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
}
