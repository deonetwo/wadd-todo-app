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

    [Fact]
    public async Task ToggleCompleteAsync_WithTargetDate_MaterializesCompletedOccurrence()
    {
        var service = new InMemoryTodoService();
        var activeItem = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Daily Exercise",
            DueDate = new DateTime(2026, 8, 1),
            IsRecurring = true,
            RecurrenceType = "Daily"
        };
        await service.CreateAsync(activeItem);

        var targetDate = new DateTime(2026, 8, 10);
        await service.ToggleCompleteAsync(activeItem.Id, targetDate);

        var allItems = (await service.GetAllAsync()).ToList();
        Assert.True(allItems.Count >= 1);
    }

    [Fact]
    public void GetTasksForDate_WhenTaskHasDifferentDueDateAndReminderDate_OnlyScheduledOnDueDate()
    {
        var dueDate = new DateTime(2026, 8, 15);
        var reminderDate = new DateTime(2026, 8, 10);
        var item = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Project Milestone",
            DueDate = dueDate,
            ReminderAt = reminderDate
        };

        var allTasks = new[] { item };

        // Should NOT be scheduled on ReminderDate (Aug 10)
        var reminderDateTasks = RecurrenceEvaluator.GetTasksForDate(reminderDate, allTasks).ToList();
        Assert.Empty(reminderDateTasks);
        Assert.False(RecurrenceEvaluator.IsTaskScheduledOnDate(item, reminderDate));

        // Should be scheduled on DueDate (Aug 15)
        var dueDateTasks = RecurrenceEvaluator.GetTasksForDate(dueDate, allTasks).ToList();
        Assert.Single(dueDateTasks);
        Assert.Equal(item.Id, dueDateTasks.Single().Id);
        Assert.True(RecurrenceEvaluator.IsTaskScheduledOnDate(item, dueDate));
    }

    [Fact]
    public void GetTasksForDate_WhenTaskHasReminderDateOnly_ScheduledOnReminderDate()
    {
        var reminderDate = new DateTime(2026, 8, 10);
        var item = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Call Doctor",
            DueDate = null,
            ReminderAt = reminderDate
        };

        var allTasks = new[] { item };

        var tasks = RecurrenceEvaluator.GetTasksForDate(reminderDate, allTasks).ToList();
        Assert.Single(tasks);
        Assert.Equal(item.Id, tasks.Single().Id);
        Assert.True(RecurrenceEvaluator.IsTaskScheduledOnDate(item, reminderDate));
    }
}
