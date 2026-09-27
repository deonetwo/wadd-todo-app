using System;
using System.Text.Json;
using Wadd.Core.Helpers;
using Wadd.Core.Models;
using Wadd.UI.ViewModels;
using Xunit;

namespace Wadd.Tests;

public class UpcomingTasksFilterTests
{
    [Fact]
    public void AppSettings_ShouldSerializeAndDeserialize_UpcomingTasksRange()
    {
        var settings = new AppSettingsData
        {
            TasksViewLayout = "Focus",
            UpcomingTasksRange = "ThisWeek",
            ShowNotePreviewsInList = false
        };

        var json = JsonSerializer.Serialize(settings);
        var deserialized = JsonSerializer.Deserialize<AppSettingsData>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("ThisWeek", deserialized.UpcomingTasksRange);
    }

    [Fact]
    public void Default_UpcomingTasksRange_IsAll()
    {
        var settings = new AppSettingsData();
        Assert.Equal("All", settings.UpcomingTasksRange);
    }

    [Theory]
    [InlineData("Tomorrow", 1, true)]
    [InlineData("Tomorrow", 2, false)]
    [InlineData("Tomorrow", 0, false)]
    [InlineData("Tomorrow", -1, false)]
    [InlineData("Next3Days", 1, true)]
    [InlineData("Next3Days", 3, true)]
    [InlineData("Next3Days", 4, false)]
    [InlineData("Next7Days", 7, true)]
    [InlineData("Next7Days", 8, false)]
    [InlineData("Next30Days", 30, true)]
    [InlineData("Next30Days", 31, false)]
    [InlineData("All", 100, true)]
    public void PassesUpcomingRangeFilter_ShouldFilterCorrectly_ByDayOffset(string range, int dayOffset, bool expected)
    {
        var today = new DateTime(2026, 8, 30); // A Sunday
        var targetDate = today.AddDays(dayOffset);
        var todoItem = new TodoItem
        {
            Title = "Test Upcoming Item",
            DueDate = targetDate
        };
        var vm = new TodoItemViewModel(todoItem);

        var result = MainViewModel.PassesUpcomingRangeFilter(vm, today, range);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void PassesUpcomingRangeFilter_ReminderAt_FallbackWhenNoDueDate()
    {
        var today = new DateTime(2026, 8, 30);
        var todoItem = new TodoItem
        {
            Title = "Reminder only item",
            DueDate = null,
            ReminderAt = today.AddDays(1)
        };
        var vm = new TodoItemViewModel(todoItem);

        Assert.True(MainViewModel.PassesUpcomingRangeFilter(vm, today, "Tomorrow"));
        Assert.True(MainViewModel.PassesUpcomingRangeFilter(vm, today, "Next3Days"));
        Assert.True(MainViewModel.PassesUpcomingRangeFilter(vm, today, "All"));
    }

    [Fact]
    public void PassesUpcomingRangeFilter_ThisMonth_FiltersWithinSameMonth()
    {
        var today = new DateTime(2026, 8, 20);
        var sameMonthDate = new DateTime(2026, 8, 31);
        var nextMonthDate = new DateTime(2026, 9, 1);

        var itemSameMonth = new TodoItemViewModel(new TodoItem { Title = "Same Month", DueDate = sameMonthDate });
        var itemNextMonth = new TodoItemViewModel(new TodoItem { Title = "Next Month", DueDate = nextMonthDate });

        Assert.True(MainViewModel.PassesUpcomingRangeFilter(itemSameMonth, today, "ThisMonth"));
        Assert.False(MainViewModel.PassesUpcomingRangeFilter(itemNextMonth, today, "ThisMonth"));
    }

    [Fact]
    public void PassesUpcomingRangeFilter_ThisWeek_CalculatesEndOfWeekCorrectly()
    {
        // Wednesday, Aug 26, 2026
        var wednesday = new DateTime(2026, 8, 26);
        var fridaySameWeek = new DateTime(2026, 8, 28);
        var sundaySameWeek = new DateTime(2026, 8, 30);
        var mondayNextWeek = new DateTime(2026, 8, 31);

        var itemFri = new TodoItemViewModel(new TodoItem { Title = "Fri", DueDate = fridaySameWeek });
        var itemSun = new TodoItemViewModel(new TodoItem { Title = "Sun", DueDate = sundaySameWeek });
        var itemMon = new TodoItemViewModel(new TodoItem { Title = "Mon", DueDate = mondayNextWeek });

        Assert.True(MainViewModel.PassesUpcomingRangeFilter(itemFri, wednesday, "ThisWeek"));
        Assert.True(MainViewModel.PassesUpcomingRangeFilter(itemSun, wednesday, "ThisWeek"));
        Assert.False(MainViewModel.PassesUpcomingRangeFilter(itemMon, wednesday, "ThisWeek"));
    }

    [Fact]
    public void PassesUpcomingRangeFilter_RecurringTasks_PassWhenDueInFuture()
    {
        var today = new DateTime(2026, 8, 30);
        var recurringTomorrow = new TodoItem
        {
            Title = "Daily Standup",
            IsRecurring = true,
            RecurrenceType = "Daily",
            DueDate = today.AddDays(1)
        };
        var vm = new TodoItemViewModel(recurringTomorrow);

        Assert.True(MainViewModel.PassesUpcomingRangeFilter(vm, today, "Tomorrow"));
        Assert.True(MainViewModel.PassesUpcomingRangeFilter(vm, today, "Next7Days"));
        Assert.True(MainViewModel.PassesUpcomingRangeFilter(vm, today, "All"));
    }

    [Fact]
    public void TodayTasks_PrioritizesTasksWithDueDateFirst()
    {
        var vm = new MainViewModel();
        var today = DateTime.Today;

        var undatedTask = new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Undated Task",
            DueDate = null,
            ReminderAt = null
        });

        var taskDueToday = new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Task Due Today",
            DueDate = today
        });

        var taskOverdue = new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Overdue Task",
            DueDate = today.AddDays(-1)
        });

        var taskReminderOnly = new TodoItemViewModel(new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Reminder Only Task",
            DueDate = null,
            ReminderAt = today.AddHours(14)
        });

        // Add in random order
        vm.TodoItems.Clear();
        vm.TodoItems.Add(undatedTask);
        vm.TodoItems.Add(taskDueToday);
        vm.TodoItems.Add(taskReminderOnly);
        vm.TodoItems.Add(taskOverdue);

        // Trigger subcollection update
        vm.SetCompletedDateFilterPresetCommand.Execute("All");
        vm.SetCompletedDateFilterPresetCommand.Execute("Today");

        // Verify order: Overdue first, then Due Today, then Reminder only, then Undated
        Assert.Equal(4, vm.TodayTodoItems.Count);
        Assert.Equal("Overdue Task", vm.TodayTodoItems[0].Title);
        Assert.Equal("Task Due Today", vm.TodayTodoItems[1].Title);
        Assert.Equal("Reminder Only Task", vm.TodayTodoItems[2].Title);
        Assert.Equal("Undated Task", vm.TodayTodoItems[3].Title);
    }
}
