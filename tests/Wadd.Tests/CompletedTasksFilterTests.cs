using System;
using Wadd.Core.Models;
using Wadd.UI.ViewModels;
using Xunit;

namespace Wadd.Tests;

public class CompletedTasksFilterTests
{
    [Fact]
    public void CompletedDateFilter_DefaultInitialization_ShouldBeToday()
    {
        var vm = new MainViewModel();

        Assert.Equal("Today", vm.CompletedDateFilterPreset);
        Assert.Equal("Today", vm.CompletedDateFilterButtonText);
        Assert.Equal(DateTime.Today, vm.CompletedDateFilterStartDate);
        Assert.Equal(DateTime.Today, vm.CompletedDateFilterEndDate);
        Assert.True(vm.IsCompletedDateFilterActive);
    }

    [Theory]
    [InlineData("Today", "Today")]
    [InlineData("Yesterday", "Yesterday")]
    [InlineData("7Days", "Last 7 Days")]
    [InlineData("30Days", "Last 30 Days")]
    [InlineData("ThisMonth", "This Month")]
    [InlineData("All", "All Time")]
    public void CompletedDateFilter_SetPreset_ShouldPreservePresetNameAndText(string preset, string expectedButtonText)
    {
        var vm = new MainViewModel();

        vm.SetCompletedDateFilterPresetCommand.Execute(preset);

        Assert.Equal(preset, vm.CompletedDateFilterPreset);
        Assert.Equal(expectedButtonText, vm.CompletedDateFilterButtonText);
    }

    [Fact]
    public void CompletedDateFilter_SelectingCustomDate_ShouldSwitchPresetToCustom()
    {
        var vm = new MainViewModel();
        var customStart = new DateTime(2026, 5, 10);
        var customEnd = new DateTime(2026, 5, 15);

        vm.CompletedDateFilterStartDate = customStart;
        vm.CompletedDateFilterEndDate = customEnd;

        Assert.Equal("Custom", vm.CompletedDateFilterPreset);
        Assert.Equal("May 10 - May 15", vm.CompletedDateFilterButtonText);
    }

    [Fact]
    public void CompletedDateFilter_SingleCustomDate_ShouldFormatCorrectly()
    {
        var vm = new MainViewModel();
        var singleDate = new DateTime(2026, 5, 10);

        vm.CompletedDateFilterStartDate = singleDate;
        vm.CompletedDateFilterEndDate = singleDate;

        Assert.Equal("Custom", vm.CompletedDateFilterPreset);
        Assert.Equal("May 10, 2026", vm.CompletedDateFilterButtonText);
    }

    [Fact]
    public void CompletedDateFilter_FilterCompletedTasks_PassesCorrectItems()
    {
        var vm = new MainViewModel();
        var today = DateTime.Today;

        var taskToday = new TodoItemViewModel(new TodoItem
        {
            Title = "Task Completed Today",
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow
        });

        var taskYesterday = new TodoItemViewModel(new TodoItem
        {
            Title = "Task Completed Yesterday",
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow.AddDays(-1)
        });

        var taskOld = new TodoItemViewModel(new TodoItem
        {
            Title = "Task Completed 10 Days Ago",
            IsCompleted = true,
            CompletedAt = DateTime.UtcNow.AddDays(-10)
        });

        vm.TodoItems.Clear();
        vm.TodoItems.Add(taskToday);
        vm.TodoItems.Add(taskYesterday);
        vm.TodoItems.Add(taskOld);
        vm.SetCompletedDateFilterPresetCommand.Execute("All");
        vm.SetCompletedDateFilterPresetCommand.Execute("Today");

        Assert.Contains(taskToday, vm.CompletedHistoryTodoItems);
        Assert.DoesNotContain(taskYesterday, vm.CompletedHistoryTodoItems);
        Assert.DoesNotContain(taskOld, vm.CompletedHistoryTodoItems);

        // Switch to Yesterday
        vm.SetCompletedDateFilterPresetCommand.Execute("Yesterday");
        Assert.DoesNotContain(taskToday, vm.CompletedHistoryTodoItems);
        Assert.Contains(taskYesterday, vm.CompletedHistoryTodoItems);
        Assert.DoesNotContain(taskOld, vm.CompletedHistoryTodoItems);

        // Switch to All
        vm.SetCompletedDateFilterPresetCommand.Execute("All");
        Assert.Contains(taskToday, vm.CompletedHistoryTodoItems);
        Assert.Contains(taskYesterday, vm.CompletedHistoryTodoItems);
        Assert.Contains(taskOld, vm.CompletedHistoryTodoItems);
    }
}
