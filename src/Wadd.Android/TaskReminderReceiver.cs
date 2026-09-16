using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Wadd.Core.Helpers;
using Wadd.Core.Models;
using Wadd.Services;

namespace Wadd.Android;

/// <summary>
/// Broadcast receiver that wakes up on scheduled AlarmManager alarms or system boot,
/// verifying task completion state and posting native notifications.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] {
    TaskAlarmScheduler.ActionTaskAlarm,
    TaskAlarmScheduler.ActionCheckReminders,
    global::Android.Content.Intent.ActionBootCompleted,
    global::Android.Content.Intent.ActionMyPackageReplaced
})]
public class TaskReminderReceiver : BroadcastReceiver
{
    private const string PrefsName = "wadd_notification_state";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent == null) return;

        var action = intent.Action;
        if (action == global::Android.Content.Intent.ActionBootCompleted ||
            action == global::Android.Content.Intent.ActionMyPackageReplaced)
        {
            TaskAlarmScheduler.RescheduleAll(context);
            return;
        }

        if (action == TaskAlarmScheduler.ActionTaskAlarm || action == TaskAlarmScheduler.ActionCheckReminders)
        {
            var pendingResult = GoAsync();
            _ = Task.Run(async () =>
            {
                try
                {
                    await CheckAndPostRemindersAsync(context);
                }
                catch (Exception ex)
                {
                    Wadd.Core.Logging.AppLogger.LogError("TaskReminderReceiver", "Error handling task alarm", ex);
                }
                finally
                {
                    pendingResult?.Finish();
                }
            });
        }
    }

    private static async Task CheckAndPostRemindersAsync(Context context)
    {
        SQLitePCL.Batteries_V2.Init();
        var settings = AppSettingsHelper.LoadSettings();
        if (!settings.EnableNotifications)
        {
            return;
        }

        var todoService = new SQLiteTodoService();
        var allTasks = await todoService.GetAllAsync();
        var now = DateTime.Now;
        var today = now.Date;
        var repeatInterval = settings.NotificationRepeatIntervalMinutes;
        var leadMinutes = settings.NotificationLeadTimeMinutes;

        var pendingAlerts = new List<(TodoItem Item, string Title, string Body)>();
        bool hasActiveUncompletedTasks = false;

        foreach (var task in allTasks)
        {
            if (task.IsCompleted)
            {
                ClearLastNotifiedTime(context, task.Id);
                TaskAlarmScheduler.CancelAlarm(context, task.Id);
                continue;
            }

            var lastTime = GetLastNotifiedTime(context, task.Id);
            bool shouldRepeat = repeatInterval > 0
                && lastTime.HasValue
                && (now - lastTime.Value).TotalMinutes >= repeatInterval;

            bool isFirstNotification = !lastTime.HasValue;

            // 1. Scheduled Task Reminders
            if (settings.NotifyOnTaskReminder && task.ReminderAt.HasValue)
            {
                var triggerTime = task.ReminderAt.Value.AddMinutes(-leadMinutes);
                if (triggerTime <= now)
                {
                    hasActiveUncompletedTasks = true;
                    if (isFirstNotification || shouldRepeat)
                    {
                        SetLastNotifiedTime(context, task.Id, now);
                        var body = (settings.WindowsNotificationIncludeNotes && !string.IsNullOrWhiteSpace(task.Description))
                            ? $"{task.Title} - {task.Description}"
                            : task.Title;
                        pendingAlerts.Add((task, $"Reminder: {task.Title}", body));
                        continue;
                    }
                }
            }

            // 2. Overdue Tasks
            if (settings.NotifyOnOverdueTasks)
            {
                bool isOverdue = (task.DueDate.HasValue && task.DueDate.Value.Date < today) ||
                                 (task.ReminderAt.HasValue && task.ReminderAt.Value.Date < today);
                if (isOverdue)
                {
                    hasActiveUncompletedTasks = true;
                    if (isFirstNotification || shouldRepeat)
                    {
                        SetLastNotifiedTime(context, task.Id, now);
                        var body = (settings.WindowsNotificationIncludeNotes && !string.IsNullOrWhiteSpace(task.Description))
                            ? $"{task.Title} - {task.Description}"
                            : task.Title;
                        pendingAlerts.Add((task, $"Overdue: {task.Title}", body));
                        continue;
                    }
                }
            }

            // 3. Due Date Today Tasks
            if (settings.NotifyOnTaskDueDate && task.DueDate.HasValue && task.DueDate.Value.Date == today)
            {
                hasActiveUncompletedTasks = true;
                if (isFirstNotification || shouldRepeat)
                {
                    SetLastNotifiedTime(context, task.Id, now);
                    var body = (settings.WindowsNotificationIncludeNotes && !string.IsNullOrWhiteSpace(task.Description))
                        ? $"{task.Title} - {task.Description}"
                        : task.Title;
                    pendingAlerts.Add((task, $"Due Today: {task.Title}", body));
                    continue;
                }
            }
        }

        if (pendingAlerts.Count == 1)
        {
            var single = pendingAlerts[0];
            AndroidNotificationService.PostNativeNotification(context, single.Title, single.Body, single.Item.Id.ToString(), settings);
        }
        else if (pendingAlerts.Count > 1)
        {
            var totalCount = pendingAlerts.Count;
            var bundleTitle = $"{totalCount} Task Reminders";
            const int maxDisplayItems = 3;
            var displayLines = pendingAlerts.Take(maxDisplayItems).Select(p => $"• {p.Item.Title}");
            var bundleBody = totalCount <= maxDisplayItems
                ? string.Join("\n", displayLines)
                : string.Join("\n", displayLines) + $"\n+ {totalCount - maxDisplayItems} more tasks";

            AndroidNotificationService.PostNativeNotification(context, bundleTitle, bundleBody, "tasks-summary", settings);
        }

        // Always schedule the next recurring background check if there are active uncompleted tasks
        if (hasActiveUncompletedTasks)
        {
            int intervalMinutes = repeatInterval > 0 ? repeatInterval : 15;
            TaskAlarmScheduler.SchedulePeriodicCheck(context, intervalMinutes);
        }
    }

    private static DateTime? GetLastNotifiedTime(Context context, Guid taskId)
    {
        try
        {
            var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            var ticks = prefs?.GetLong($"last_notified_{taskId}", 0) ?? 0;
            return ticks > 0 ? new DateTime(ticks) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SetLastNotifiedTime(Context context, Guid taskId, DateTime time)
    {
        try
        {
            var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            prefs?.Edit()?.PutLong($"last_notified_{taskId}", time.Ticks)?.Apply();
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("TaskReminderReceiver", "Error saving notified time", ex);
        }
    }

    public static void ClearLastNotifiedTime(Context context, Guid taskId)
    {
        try
        {
            var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            prefs?.Edit()?.Remove($"last_notified_{taskId}")?.Apply();
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("TaskReminderReceiver", "Error clearing notified time", ex);
        }
    }
}
