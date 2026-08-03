using System;
using Wadd.Core.Models;
using Wadd.Services;
using Xunit;

namespace Wadd.Tests;

public class CalendarAndRecurrenceEvaluatorTests
{
    [Fact]
    public void IsTaskScheduledOnDate_DirectDueDate_ReturnsTrue()
    {
        var targetDate = new DateTime(2026, 8, 15);
        var item = new TodoItem { DueDate = targetDate };

        var isScheduled = RecurrenceEvaluator.IsTaskScheduledOnDate(item, targetDate);

        Assert.True(isScheduled);
    }

    [Fact]
    public void IsTaskScheduledOnDate_DailyRecurrence_ReturnsTrueForFutureDates()
    {
        var startDate = new DateTime(2026, 8, 1);
        var testDate = new DateTime(2026, 8, 15);
        var item = new TodoItem
        {
            DueDate = startDate,
            IsRecurring = true,
            RecurrenceType = "Daily"
        };

        var isScheduled = RecurrenceEvaluator.IsTaskScheduledOnDate(item, testDate);

        Assert.True(isScheduled);
    }

    [Fact]
    public void IsTaskScheduledOnDate_WeeklyRecurrence_ReturnsTrueOnlySameDayOfWeek()
    {
        // Aug 1, 2026 is Saturday
        var startDate = new DateTime(2026, 8, 1);
        var item = new TodoItem
        {
            DueDate = startDate,
            IsRecurring = true,
            RecurrenceType = "Weekly"
        };

        // Aug 8, 2026 is Saturday -> True
        Assert.True(RecurrenceEvaluator.IsTaskScheduledOnDate(item, new DateTime(2026, 8, 8)));
        // Aug 9, 2026 is Sunday -> False
        Assert.False(RecurrenceEvaluator.IsTaskScheduledOnDate(item, new DateTime(2026, 8, 9)));
    }

    [Fact]
    public void CalendarDayModel_OverflowCalculation_CorrectlyIdentifiesOverflow()
    {
        var model = new CalendarDayModel { Date = new DateTime(2026, 8, 15) };
        model.DayTasks.Add(new TodoItem { Title = "Task 1" });
        model.DayTasks.Add(new TodoItem { Title = "Task 2" });
        model.DayTasks.Add(new TodoItem { Title = "Task 3" });
        model.DayTasks.Add(new TodoItem { Title = "Task 4" });

        Assert.True(model.HasOverflow);
        Assert.Equal(2, model.OverflowCount);
        Assert.Equal(2, model.VisibleTasks.Count());
    }
}
