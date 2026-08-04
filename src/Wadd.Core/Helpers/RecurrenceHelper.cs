using Wadd.Core.Models;

namespace Wadd.Core.Helpers;

public static class RecurrenceHelper
{
    public static DateTime CalculateNextDueDate(TodoItem item, DateTime? fromDate = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        var baseDate = fromDate?.Date ?? item.DueDate?.Date ?? DateTime.Today;

        if (!item.IsRecurring || string.IsNullOrWhiteSpace(item.RecurrenceType) || item.RecurrenceType.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return baseDate;
        }

        return item.RecurrenceType switch
        {
            "Daily" => baseDate.AddDays(1),
            "Weekdays" => GetNextWeekday(baseDate),
            "Weekly" => baseDate.AddDays(7),
            "Monthly" => baseDate.AddMonths(1),
            "Yearly" => baseDate.AddYears(1),
            "Custom" => CalculateCustomNextDueDate(baseDate, item.CustomRecurrenceInterval ?? 1, item.CustomRecurrenceUnit, item.CustomWeeklyDays),
            _ => baseDate.AddDays(1)
        };
    }

    public static DateTime CalculateNextUncompletedDueDate(TodoItem item, DateTime? fromDate = null, IEnumerable<TodoItem>? allTasks = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        var next = CalculateNextDueDate(item, fromDate);

        if (allTasks == null) return next;

        var completedDates = allTasks
            .Where(t => t.IsCompleted && t.Title.Equals(item.Title, StringComparison.OrdinalIgnoreCase) && t.DueDate.HasValue)
            .Select(t => t.DueDate!.Value.Date)
            .ToHashSet();

        int safetyMax = 365;
        while (completedDates.Contains(next.Date) && safetyMax-- > 0)
        {
            next = CalculateNextDueDate(item, next);
        }

        return next;
    }

    public static DateTime GetFirstValidOccurrenceDate(TodoItem item, DateTime baseDate)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!item.IsRecurring || string.IsNullOrWhiteSpace(item.RecurrenceType) || item.RecurrenceType.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return baseDate.Date;
        }

        var candidate = baseDate.Date;
        string recType = item.RecurrenceType;

        if (recType.Equals("Weekdays", StringComparison.OrdinalIgnoreCase))
        {
            while (candidate.DayOfWeek == DayOfWeek.Saturday || candidate.DayOfWeek == DayOfWeek.Sunday)
            {
                candidate = candidate.AddDays(1);
            }
            return candidate;
        }

        if (recType.Equals("Custom", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.CustomRecurrenceUnit, "Weeks", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.CustomWeeklyDays))
        {
            var allowedDays = item.CustomWeeklyDays
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => d.Substring(0, Math.Min(3, d.Length)))
                .ToList();

            if (allowedDays.Count > 0)
            {
                int maxCheck = 7;
                while (maxCheck-- > 0)
                {
                    string dayAbbrev = candidate.DayOfWeek.ToString().Substring(0, 3);
                    if (allowedDays.Any(d => d.Equals(dayAbbrev, StringComparison.OrdinalIgnoreCase)))
                    {
                        return candidate;
                    }
                    candidate = candidate.AddDays(1);
                }
            }
        }

        return candidate;
    }

    private static DateTime GetNextWeekday(DateTime baseDate)
    {
        var next = baseDate.AddDays(1);
        while (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)
        {
            next = next.AddDays(1);
        }
        return next;
    }

    private static DateTime CalculateCustomNextDueDate(DateTime baseDate, int interval, string? unit, string? weeklyDays)
    {
        if (interval < 1) interval = 1;
        var normalizedUnit = unit?.Trim() ?? "Days";

        if (normalizedUnit.Equals("Days", StringComparison.OrdinalIgnoreCase))
        {
            return baseDate.AddDays(interval);
        }
        if (normalizedUnit.Equals("Months", StringComparison.OrdinalIgnoreCase))
        {
            return baseDate.AddMonths(interval);
        }
        if (normalizedUnit.Equals("Years", StringComparison.OrdinalIgnoreCase))
        {
            return baseDate.AddYears(interval);
        }
        if (normalizedUnit.Equals("Weeks", StringComparison.OrdinalIgnoreCase))
        {
            return CalculateCustomWeeklyNextDueDate(baseDate, interval, weeklyDays);
        }

        return baseDate.AddDays(interval);
    }

    private static DateTime CalculateCustomWeeklyNextDueDate(DateTime baseDate, int interval, string? weeklyDays)
    {
        var days = ParseWeeklyDays(weeklyDays);
        if (days.Count == 0)
        {
            return baseDate.AddDays(7 * interval);
        }

        // Check if there is another selected day later in the current week
        var currentDayOfWeek = baseDate.DayOfWeek;
        foreach (var day in days.OrderBy(d => d))
        {
            if (day > currentDayOfWeek)
            {
                int daysToAdd = (int)day - (int)currentDayOfWeek;
                return baseDate.AddDays(daysToAdd);
            }
        }

        // Otherwise advance to the target interval week and select the earliest day
        var firstSelectedDay = days.OrderBy(d => d).First();
        int daysUntilEndOfWeek = 7 - (int)currentDayOfWeek; // days to next Sunday/Monday
        int additionalWeeksOffset = (interval - 1) * 7;
        int totalDaysToAdd = daysUntilEndOfWeek + additionalWeeksOffset + (int)firstSelectedDay;
        return baseDate.AddDays(totalDaysToAdd);
    }

    public static List<DayOfWeek> ParseWeeklyDays(string? weeklyDays)
    {
        var result = new List<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(weeklyDays)) return result;

        var parts = weeklyDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (Enum.TryParse<DayOfWeek>(part, true, out var day))
            {
                if (!result.Contains(day))
                {
                    result.Add(day);
                }
            }
        }
        return result;
    }

    public static string FormatRecurrenceText(bool isRecurring, string recurrenceType, int? customInterval, string? customUnit, string? customWeeklyDays)
    {
        if (!isRecurring || string.IsNullOrWhiteSpace(recurrenceType) || recurrenceType.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        switch (recurrenceType)
        {
            case "Daily":
                return "Daily";
            case "Weekdays":
                return "Every Weekday";
            case "Weekly":
                return "Weekly";
            case "Monthly":
                return "Monthly";
            case "Yearly":
                return "Yearly";
            case "Custom":
                var interval = customInterval ?? 1;
                var unit = customUnit ?? "Days";
                if (unit.Equals("Weeks", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(customWeeklyDays))
                {
                    var parsedDays = ParseWeeklyDays(customWeeklyDays);
                    var dayAbbrs = parsedDays.Select(d => d.ToString()[..3]).ToList();
                    var daysFormatted = string.Join(", ", dayAbbrs);
                    return interval == 1 ? $"Every Week ({daysFormatted})" : $"Every {interval} Weeks ({daysFormatted})";
                }

                var unitSingle = unit.TrimEnd('s');
                return interval == 1 ? $"Every {unitSingle}" : $"Every {interval} {unit}";
            default:
                return recurrenceType;
        }
    }
}
