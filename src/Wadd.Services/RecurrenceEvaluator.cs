using Wadd.Core.Models;

namespace Wadd.Services;

public static class RecurrenceEvaluator
{
    public static bool IsTaskScheduledOnDate(TodoItem item, DateTime date)
    {
        DateTime targetDate = date.Date;

        // 1. Direct match: Due Date takes priority; fallback to Reminder Date only if Due Date is not set
        var taskDate = item.DueDate?.Date ?? item.ReminderAt?.Date;
        if (taskDate.HasValue && taskDate.Value == targetDate)
        {
            return true;
        }

        // 2. Recurring Task evaluation
        if (!item.IsRecurring)
        {
            return false;
        }

        // Determine recurrence anchor date & start cutoff date
        DateTime anchorDate = item.DueDate?.Date ?? item.ReminderAt?.Date ?? item.CreatedAt.Date;
        DateTime startCutoff = item.DueDate?.Date ?? item.ReminderAt?.Date ?? item.CreatedAt.Date;

        if (targetDate < startCutoff)
        {
            return false;
        }

        string recurrenceType = item.RecurrenceType ?? "None";

        switch (recurrenceType.ToLowerInvariant())
        {
            case "daily":
                return true;

            case "weekdays":
                return targetDate.DayOfWeek != DayOfWeek.Saturday && targetDate.DayOfWeek != DayOfWeek.Sunday;

            case "weekly":
                return targetDate.DayOfWeek == anchorDate.DayOfWeek;

            case "monthly":
                int targetMaxDaysInMonth = DateTime.DaysInMonth(targetDate.Year, targetDate.Month);
                int targetDay = Math.Min(anchorDate.Day, targetMaxDaysInMonth);
                return targetDate.Day == targetDay;

            case "yearly":
                if (targetDate.Month != anchorDate.Month) return false;
                int maxDaysInYearlyMonth = DateTime.DaysInMonth(targetDate.Year, targetDate.Month);
                int expectedDay = Math.Min(anchorDate.Day, maxDaysInYearlyMonth);
                return targetDate.Day == expectedDay;

            case "custom":
                return IsCustomRecurrenceOnDate(item, anchorDate, targetDate);

            default:
                return false;
        }
    }

    private static bool IsCustomRecurrenceOnDate(TodoItem item, DateTime startDate, DateTime targetDate)
    {
        int interval = Math.Max(1, item.CustomRecurrenceInterval ?? 1);
        string unit = (item.CustomRecurrenceUnit ?? "Days").ToLowerInvariant();

        switch (unit)
        {
            case "days":
                int totalDays = (targetDate - startDate).Days;
                return totalDays >= 0 && totalDays % interval == 0;

            case "weeks":
                // Check if target week matches interval
                int totalDaysFromStart = (targetDate - startDate).Days;
                if (totalDaysFromStart < 0) return false;

                // Check weekly days if specified (e.g., "Mon,Wed,Fri")
                if (!string.IsNullOrWhiteSpace(item.CustomWeeklyDays))
                {
                    string dayAbbrev = targetDate.DayOfWeek.ToString().Substring(0, 3);
                    if (!item.CustomWeeklyDays.Contains(dayAbbrev, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
                else
                {
                    if (targetDate.DayOfWeek != startDate.DayOfWeek)
                    {
                        return false;
                    }
                }

                int weekDiff = totalDaysFromStart / 7;
                return weekDiff % interval == 0;

            case "months":
                int monthDiff = (targetDate.Year - startDate.Year) * 12 + (targetDate.Month - startDate.Month);
                if (monthDiff < 0 || monthDiff % interval != 0) return false;
                int daysInTargetMonth = DateTime.DaysInMonth(targetDate.Year, targetDate.Month);
                int expectedTargetDay = Math.Min(startDate.Day, daysInTargetMonth);
                return targetDate.Day == expectedTargetDay;

            case "years":
                int yearDiff = targetDate.Year - startDate.Year;
                if (yearDiff < 0 || yearDiff % interval != 0) return false;
                if (targetDate.Month != startDate.Month) return false;
                int daysInTargetYearMonth = DateTime.DaysInMonth(targetDate.Year, targetDate.Month);
                int expectedYearDay = Math.Min(startDate.Day, daysInTargetYearMonth);
                return targetDate.Day == expectedYearDay;

            default:
                return false;
        }
    }

    public static IEnumerable<TodoItem> GetTasksForDate(DateTime date, IEnumerable<TodoItem> allTasks)
    {
        DateTime targetDate = date.Date;
        var taskList = allTasks.ToList();

        // 1. Direct/explicit items assigned to this date (DueDate takes priority, fallback to ReminderAt only if no DueDate)
        var directItemsForDate = taskList
            .Where(t => (t.DueDate?.Date ?? t.ReminderAt?.Date) == targetDate)
            .DistinctBy(t => new { Title = t.Title.Trim().ToLowerInvariant(), t.IsCompleted })
            .ToList();

        var completedTitlesOnDate = directItemsForDate
            .Where(t => t.IsCompleted)
            .Select(t => t.Title.Trim().ToLowerInvariant())
            .ToHashSet();

        // 2. Active recurring tasks evaluated for this date (excluding titles already completed on this date)
        var activeRecurringForDate = taskList
            .Where(t => t.IsRecurring && !t.IsCompleted)
            .Where(t => IsTaskScheduledOnDate(t, targetDate))
            .Where(t => !completedTitlesOnDate.Contains(t.Title.Trim().ToLowerInvariant()))
            .ToList();

        return directItemsForDate.Concat(activeRecurringForDate).DistinctBy(t => t.Id);
    }
}
