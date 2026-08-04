using System;
using Wadd.Core.Helpers;
using Wadd.Core.Models;
using Wadd.Services;
using Xunit;

namespace Wadd.Tests;

public class RecurrenceHelperTests
{
    [Fact]
    public void CalculateNextDueDate_Daily_AddsOneDay()
    {
        var baseDate = new DateTime(2026, 8, 3);
        var item = new TodoItem
        {
            DueDate = baseDate,
            IsRecurring = true,
            RecurrenceType = "Daily"
        };

        var nextDate = RecurrenceHelper.CalculateNextDueDate(item);

        Assert.Equal(new DateTime(2026, 8, 4), nextDate);
    }

    [Fact]
    public void CalculateNextDueDate_Weekdays_SkipsWeekends()
    {
        // Friday, Aug 7, 2026
        var friday = new DateTime(2026, 8, 7);
        var item = new TodoItem
        {
            DueDate = friday,
            IsRecurring = true,
            RecurrenceType = "Weekdays"
        };

        var nextDate = RecurrenceHelper.CalculateNextDueDate(item);

        // Next weekday after Friday should be Monday, Aug 10, 2026
        Assert.Equal(new DateTime(2026, 8, 10), nextDate);
        Assert.Equal(DayOfWeek.Monday, nextDate.DayOfWeek);
    }

    [Fact]
    public void CalculateNextDueDate_Weekly_AddsSevenDays()
    {
        var baseDate = new DateTime(2026, 8, 3);
        var item = new TodoItem
        {
            DueDate = baseDate,
            IsRecurring = true,
            RecurrenceType = "Weekly"
        };

        var nextDate = RecurrenceHelper.CalculateNextDueDate(item);

        Assert.Equal(new DateTime(2026, 8, 10), nextDate);
    }

    [Fact]
    public void CalculateNextDueDate_Monthly_AddsOneMonth()
    {
        var baseDate = new DateTime(2026, 8, 3);
        var item = new TodoItem
        {
            DueDate = baseDate,
            IsRecurring = true,
            RecurrenceType = "Monthly"
        };

        var nextDate = RecurrenceHelper.CalculateNextDueDate(item);

        Assert.Equal(new DateTime(2026, 9, 3), nextDate);
    }

    [Fact]
    public void CalculateNextDueDate_CustomDays_AddsInterval()
    {
        var baseDate = new DateTime(2026, 8, 3);
        var item = new TodoItem
        {
            DueDate = baseDate,
            IsRecurring = true,
            RecurrenceType = "Custom",
            CustomRecurrenceInterval = 3,
            CustomRecurrenceUnit = "Days"
        };

        var nextDate = RecurrenceHelper.CalculateNextDueDate(item);

        Assert.Equal(new DateTime(2026, 8, 6), nextDate);
    }

    [Fact]
    public void CalculateNextDueDate_CustomWeeksWithSpecificDays_CalculatesCorrectDay()
    {
        // Monday, Aug 3, 2026
        var monday = new DateTime(2026, 8, 3);
        var item = new TodoItem
        {
            DueDate = monday,
            IsRecurring = true,
            RecurrenceType = "Custom",
            CustomRecurrenceInterval = 1,
            CustomRecurrenceUnit = "Weeks",
            CustomWeeklyDays = "Monday,Wednesday,Friday"
        };

        // Next day from Monday should be Wednesday, Aug 5, 2026
        var nextDate = RecurrenceHelper.CalculateNextDueDate(item);
        Assert.Equal(new DateTime(2026, 8, 5), nextDate);
        Assert.Equal(DayOfWeek.Wednesday, nextDate.DayOfWeek);
    }

    [Fact]
    public void FormatRecurrenceText_ReturnsFriendlyString()
    {
        Assert.Equal("Daily", RecurrenceHelper.FormatRecurrenceText(true, "Daily", null, null, null));
        Assert.Equal("Every Weekday", RecurrenceHelper.FormatRecurrenceText(true, "Weekdays", null, null, null));
        Assert.Equal("Every 2 Weeks (Mon, Wed, Fri)", RecurrenceHelper.FormatRecurrenceText(true, "Custom", 2, "Weeks", "Monday,Wednesday,Friday"));
        Assert.Equal(string.Empty, RecurrenceHelper.FormatRecurrenceText(false, "None", null, null, null));
    }

    [Fact]
    public async System.Threading.Tasks.Task ToggleCompleteAsync_RecurringTask_OnlySpawnsOneChildOnRepeatedToggles()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wadd_test_recurrence_{Guid.NewGuid():N}.db");
        var service = new Wadd.Services.SQLiteTodoService(dbPath);

        var item = new TodoItem
        {
            Title = "Daily Water",
            IsRecurring = true,
            RecurrenceType = "Daily",
            DueDate = DateTime.Today
        };

        await service.AddTodoAsync(item);

        // 1. Toggle done -> spawns next instance
        await service.ToggleCompleteAsync(item.Id);
        var all1 = (await service.GetTodosAsync()).ToList();
        Assert.Equal(2, all1.Count); // Original completed + 1 new child

        // 2. Toggle undone on original item
        await service.ToggleCompleteAsync(item.Id);
        var all2 = (await service.GetTodosAsync()).ToList();
        Assert.Equal(2, all2.Count); // Still 2 items

        // 3. Toggle done on original item again
        await service.ToggleCompleteAsync(item.Id);
        var all3 = (await service.GetTodosAsync()).ToList();
        Assert.Equal(2, all3.Count); // NO duplicate child created! Still 2 items
    }

    [Fact]
    public async Task ToggleCompleteAsync_WhenAheadDateIsCompleted_SkipsPreCompletedDate()
    {
        var service = new InMemoryTodoService();
        var activeItem = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Daily Practice",
            DueDate = new DateTime(2026, 8, 9),
            IsRecurring = true,
            RecurrenceType = "Daily"
        };
        await service.CreateAsync(activeItem);

        // Pre-complete Aug 11 ahead of time
        await service.ToggleCompleteAsync(activeItem.Id, new DateTime(2026, 8, 11));

        // Complete Aug 9
        await service.ToggleCompleteAsync(activeItem.Id, new DateTime(2026, 8, 9));

        // Complete Aug 10
        await service.ToggleCompleteAsync(activeItem.Id, new DateTime(2026, 8, 10));

        // Active task should have hopped past Aug 11 to Aug 12
        Assert.Equal(new DateTime(2026, 8, 12), activeItem.DueDate?.Date);

        // Get tasks for Aug 11: should return ONLY the 1 completed instance for Aug 11
        var allTasks = (await service.GetAllAsync()).ToList();
        var aug11Tasks = RecurrenceEvaluator.GetTasksForDate(new DateTime(2026, 8, 11), allTasks).ToList();

        Assert.Single(aug11Tasks);
        Assert.True(aug11Tasks.Single().IsCompleted);

        // Now uncomplete Aug 11
        var completedAug11Item = aug11Tasks.Single();
        await service.ToggleCompleteAsync(completedAug11Item.Id, new DateTime(2026, 8, 11));

        // Active task due date should pull back to Aug 11
        Assert.Equal(new DateTime(2026, 8, 11), activeItem.DueDate?.Date);

        // Get tasks for Aug 11: task should NOT disappear, but return 1 active uncompleted task for Aug 11
        var allTasksAfterUndo = (await service.GetAllAsync()).ToList();
        var aug11TasksAfterUndo = RecurrenceEvaluator.GetTasksForDate(new DateTime(2026, 8, 11), allTasksAfterUndo).ToList();

        Assert.Single(aug11TasksAfterUndo);
        Assert.False(aug11TasksAfterUndo.Single().IsCompleted);
    }

    [Fact]
    public async Task AddTodoAsync_WhenRecurringTaskCreatedWithoutDueDate_SetsFirstDueDateToToday()
    {
        var service = new InMemoryTodoService();
        var recurringItemWithoutDueDate = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Morning Routine",
            IsRecurring = true,
            RecurrenceType = "Daily",
            DueDate = null
        };

        var created = await service.AddTodoAsync(recurringItemWithoutDueDate);

        Assert.NotNull(created.DueDate);
        Assert.Equal(DateTime.Today, created.DueDate.Value.Date);
    }

    [Fact]
    public void WeeklyTask_WithExplicitDueDate_AnchorsToDueDateDayOfWeek()
    {
        var aug6Thursday = new DateTime(2026, 8, 6);
        var weeklyTask = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Weekly Team Sync",
            CreatedAt = new DateTime(2026, 8, 4), // Tuesday
            DueDate = aug6Thursday,               // Thursday
            IsRecurring = true,
            RecurrenceType = "Weekly"
        };

        // Before Aug 6 (e.g. Aug 4, Tuesday): Should NOT be scheduled
        Assert.False(RecurrenceEvaluator.IsTaskScheduledOnDate(weeklyTask, new DateTime(2026, 8, 4)));

        // On Aug 6 (Thursday): Should BE scheduled
        Assert.True(RecurrenceEvaluator.IsTaskScheduledOnDate(weeklyTask, aug6Thursday));

        // Next week Aug 13 (Thursday): Should BE scheduled
        Assert.True(RecurrenceEvaluator.IsTaskScheduledOnDate(weeklyTask, new DateTime(2026, 8, 13)));

        // Intermediate Tuesday Aug 11: Should NOT be scheduled
        Assert.False(RecurrenceEvaluator.IsTaskScheduledOnDate(weeklyTask, new DateTime(2026, 8, 11)));
    }

    [Fact]
    public void WeekdaysTask_WithSaturdayDueDate_SnapsToMonday()
    {
        var satAug8 = new DateTime(2026, 8, 8);
        var task = new TodoItem
        {
            Title = "Workday Report",
            IsRecurring = true,
            RecurrenceType = "Weekdays"
        };

        var firstValid = RecurrenceHelper.GetFirstValidOccurrenceDate(task, satAug8);
        Assert.Equal(new DateTime(2026, 8, 10), firstValid); // Monday Aug 10
    }

    [Fact]
    public void CustomSunMonTask_WithSaturdayDueDate_SnapsToSunday()
    {
        var satAug8 = new DateTime(2026, 8, 8);
        var task = new TodoItem
        {
            Title = "Weekend Workout",
            IsRecurring = true,
            RecurrenceType = "Custom",
            CustomRecurrenceUnit = "Weeks",
            CustomWeeklyDays = "Sun,Mon"
        };

        var firstValid = RecurrenceHelper.GetFirstValidOccurrenceDate(task, satAug8);
        Assert.Equal(new DateTime(2026, 8, 9), firstValid); // Sunday Aug 9
    }
}
