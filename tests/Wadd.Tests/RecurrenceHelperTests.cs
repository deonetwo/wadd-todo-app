using System;
using Wadd.Core.Helpers;
using Wadd.Core.Models;
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
}
