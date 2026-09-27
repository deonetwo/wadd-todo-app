using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Wadd.Core.Models;
using Wadd.Services;
using Wadd.UI.ViewModels;
using Xunit;

namespace Wadd.Tests;

public class DeleteConfirmationTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SQLiteTodoService _todoService;
    private readonly SQLiteGoalService _goalService;

    public DeleteConfirmationTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"wadd_del_test_{Guid.NewGuid():N}.db");
        _todoService = new SQLiteTodoService(_testDbPath);
        _goalService = new SQLiteGoalService(_todoService.DatabaseConnection);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch { }
    }

    private MainViewModel CreateMainViewModel()
    {
        return new MainViewModel(
            _todoService,
            new ThemeService(),
            new MockSyncService(),
            new ExcelExportService(),
            _goalService,
            new WindowsStartupService(),
            new AiGoalService(new System.Net.Http.HttpClient()),
            new WindowsNotificationService(),
            new AudioService());
    }

    [Fact]
    public async Task RequestDeleteConfirmationAsync_Confirm_ReturnsTrueAndClosesDialog()
    {
        var vm = CreateMainViewModel();

        var confirmTask = vm.RequestDeleteConfirmationAsync(
            "Delete Task?",
            "Are you sure you want to delete this task?",
            "Buy Groceries",
            "Today");

        Assert.True(vm.IsDeleteConfirmationOpen);
        Assert.Equal("Delete Task?", vm.DeleteConfirmationTitle);
        Assert.Equal("Are you sure you want to delete this task?", vm.DeleteConfirmationMessage);
        Assert.Equal("Buy Groceries", vm.DeleteConfirmationItemName);
        Assert.Equal("Today", vm.DeleteConfirmationItemDetails);

        vm.ConfirmDeleteCommand.Execute(null);

        var result = await confirmTask;
        Assert.True(result);
        Assert.False(vm.IsDeleteConfirmationOpen);
    }

    [Fact]
    public async Task RequestDeleteConfirmationAsync_Cancel_ReturnsFalseAndClosesDialog()
    {
        var vm = CreateMainViewModel();

        var confirmTask = vm.RequestDeleteConfirmationAsync(
            "Delete Tag?",
            "Are you sure?",
            "#work");

        Assert.True(vm.IsDeleteConfirmationOpen);

        vm.CancelDeleteCommand.Execute(null);

        var result = await confirmTask;
        Assert.False(result);
        Assert.False(vm.IsDeleteConfirmationOpen);
    }

    [Fact]
    public async Task RequestDeleteConfirmationAsync_SubsequentRequest_CancelsPrevious()
    {
        var vm = CreateMainViewModel();

        var firstTask = vm.RequestDeleteConfirmationAsync("First?", "Msg 1", "Item 1");
        Assert.True(vm.IsDeleteConfirmationOpen);

        var secondTask = vm.RequestDeleteConfirmationAsync("Second?", "Msg 2", "Item 2");
        Assert.True(vm.IsDeleteConfirmationOpen);

        // Previous request should automatically be canceled
        var firstResult = await firstTask;
        Assert.False(firstResult);

        // Confirm second request
        vm.ConfirmDeleteCommand.Execute(null);
        var secondResult = await secondTask;
        Assert.True(secondResult);
        Assert.False(vm.IsDeleteConfirmationOpen);
    }

    [Fact]
    public async Task GoalsViewModel_DeleteGoal_WhenCancelled_DoesNotDelete()
    {
        var goal = new LifeGoal { Title = "Master C# & Avalonia" };
        var saved = await _goalService.SaveGoalAsync(goal);

        var goalsVM = new GoalsViewModel(_goalService);
        await goalsVM.LoadAllGoalsAsync();

        // Register confirmation handler that returns false (canceled)
        goalsVM.ConfirmDeleteRequested = (title, msg, item, details) => Task.FromResult(false);

        var goalItem = goalsVM.Goals.First(g => g.Id == saved.Id);
        await goalsVM.DeleteGoalCommand.ExecuteAsync(goalItem);

        var inDb = await _goalService.GetGoalByIdAsync(saved.Id);
        Assert.NotNull(inDb);
        Assert.False(inDb.IsDeleted);
        Assert.Contains(goalsVM.Goals, g => g.Id == saved.Id);
    }

    [Fact]
    public async Task GoalsViewModel_DeleteGoal_WhenConfirmed_DeletesGoal()
    {
        var goal = new LifeGoal { Title = "Master C# & Avalonia" };
        var saved = await _goalService.SaveGoalAsync(goal);

        var goalsVM = new GoalsViewModel(_goalService);
        await Task.Delay(50);
        goalsVM.Goals.Clear();
        await goalsVM.LoadAllGoalsAsync();

        // Register confirmation handler that returns true (confirmed)
        goalsVM.ConfirmDeleteRequested = (title, msg, item, details) => Task.FromResult(true);

        var goalItem = goalsVM.Goals.First(g => g.Id == saved.Id);
        await goalsVM.DeleteGoalCommand.ExecuteAsync(goalItem);

        var inDb = await _goalService.GetGoalByIdAsync(saved.Id);
        Assert.True(inDb == null || inDb.IsDeleted);
        Assert.DoesNotContain(goalsVM.Goals, g => g.Id == saved.Id);
    }
}
