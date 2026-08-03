using Wadd.Core.Models;

namespace Wadd.Services;

public static class RecurrenceEvaluator
{
    public static bool IsTaskScheduledOnDate(TodoItem item, DateTime date)
    {
        DateTime targetDate = date.Date;

        // 1. Direct Due Date match
        if (item.DueDate.HasValue && item.DueDate.Value.Date == targetDate)
        {
            return true;
        }

        // 2. Direct Reminder Date match
        if (item.ReminderAt.HasValue && item.ReminderAt.Value.Date == targetDate)
        {
            return true;
        }

        // 3. Recurring Task evaluation
        if (!item.IsRecurring)
        {
            return false;
        }

        DateTime startDate = (item.DueDate ?? item.ReminderAt ?? item.CreatedAt).Date;
        if (targetDate < startDate)
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
                return targetDate.DayOfWeek == startDate.DayOfWeek;

            case "monthly":
                int targetMaxDaysInMonth = DateTime.DaysInMonth(targetDate.Year, targetDate.Month);
                int targetDay = Math.Min(startDate.Day, targetMaxDaysInMonth);
                return targetDate.Day == targetDay;

            case "yearly":
                if (targetDate.Month != startDate.Month) return false;
                int maxDaysInYearlyMonth = DateTime.DaysInMonth(targetDate.Year, targetDate.Month);
                int expectedDay = Math.Min(startDate.Day, maxDaysInYearlyMonth);
                return targetDate.Day == expectedDay;

            case "custom":
                return IsCustomRecurrenceOnDate(item, startDate, targetDate);

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
        return allTasks.Where(task => IsTaskScheduledOnDate(task, date));
    }
}
