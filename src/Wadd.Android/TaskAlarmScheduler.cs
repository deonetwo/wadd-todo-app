using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Wadd.Core.Helpers;
using Wadd.Core.Models;
using Wadd.Services;

namespace Wadd.Android;

/// <summary>
/// Helper to schedule, update, and cancel OS-level background alarms with Android AlarmManager.
/// Ensures notifications trigger reliably when the app is in the background or completely closed.
/// </summary>
public static class TaskAlarmScheduler
{
    public const string ActionTaskAlarm = "com.wadd.todoapp.ACTION_TASK_ALARM";
    public const string ActionCheckReminders = "com.wadd.todoapp.ACTION_CHECK_REMINDERS";
    public const string ExtraTaskId = "extra_task_id";
    public const string ExtraTaskTitle = "extra_task_title";
    private const int PeriodicCheckRequestCode = 99999;

    public static void SchedulePeriodicCheck(Context context, int delayMinutes)
    {
        try
        {
            var alarmManager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
            if (alarmManager == null) return;

            var intent = new Intent(context, typeof(TaskReminderReceiver));
            intent.SetAction(ActionCheckReminders);

            var pendingFlags = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                pendingFlags |= PendingIntentFlags.Immutable;
            }

            var pendingIntent = PendingIntent.GetBroadcast(context, PeriodicCheckRequestCode, intent, pendingFlags);
            if (pendingIntent == null) return;

            var triggerTime = DateTime.Now.AddMinutes(Math.Max(1, delayMinutes));
            var triggerEpochMillis = new DateTimeOffset(triggerTime.ToLocalTime()).ToUnixTimeMilliseconds();

            if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerEpochMillis, pendingIntent);
            }
            else
            {
                alarmManager.Set(AlarmType.RtcWakeup, triggerEpochMillis, pendingIntent);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[TaskAlarmScheduler] SchedulePeriodicCheck error: {ex.Message}");
        }
    }

    public static void ScheduleAlarm(Context context, TodoItem task, DateTime triggerTime)
    {
        try
        {
            var alarmManager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
            if (alarmManager == null) return;

            var requestCode = Math.Abs(task.Id.GetHashCode());
            var intent = new Intent(context, typeof(TaskReminderReceiver));
            intent.SetAction(ActionTaskAlarm);
            intent.PutExtra(ExtraTaskId, task.Id.ToString());
            intent.PutExtra(ExtraTaskTitle, task.Title);

            var pendingFlags = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                pendingFlags |= PendingIntentFlags.Immutable;
            }

            var pendingIntent = PendingIntent.GetBroadcast(context, requestCode, intent, pendingFlags);
            if (pendingIntent == null) return;

            var triggerEpochMillis = new DateTimeOffset(triggerTime.ToLocalTime()).ToUnixTimeMilliseconds();

            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                if (alarmManager.CanScheduleExactAlarms())
                {
                    alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerEpochMillis, pendingIntent);
                }
                else
                {
                    alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerEpochMillis, pendingIntent);
                }
            }
            else if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                alarmManager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerEpochMillis, pendingIntent);
            }
            else
            {
                alarmManager.SetExact(AlarmType.RtcWakeup, triggerEpochMillis, pendingIntent);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[TaskAlarmScheduler] ScheduleAlarm error: {ex.Message}");
        }
    }

    public static void CancelAlarm(Context context, Guid taskId)
    {
        try
        {
            var alarmManager = (AlarmManager?)context.GetSystemService(Context.AlarmService);
            if (alarmManager == null) return;

            var requestCode = Math.Abs(taskId.GetHashCode());
            var intent = new Intent(context, typeof(TaskReminderReceiver));
            intent.SetAction(ActionTaskAlarm);

            var pendingFlags = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                pendingFlags |= PendingIntentFlags.Immutable;
            }

            var pendingIntent = PendingIntent.GetBroadcast(context, requestCode, intent, pendingFlags);
            if (pendingIntent != null)
            {
                alarmManager.Cancel(pendingIntent);
                pendingIntent.Cancel();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[TaskAlarmScheduler] CancelAlarm error: {ex.Message}");
        }
    }

    public static void RescheduleAll(Context context)
    {
        _ = Task.Run(async () =>
        {
            try
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
                var leadMinutes = settings.NotificationLeadTimeMinutes;
                bool hasActiveTasks = false;

                foreach (var task in allTasks)
                {
                    if (task.IsCompleted)
                    {
                        CancelAlarm(context, task.Id);
                        continue;
                    }

                    DateTime? targetAlarmTime = null;

                    if (settings.NotifyOnTaskReminder && task.ReminderAt.HasValue)
                    {
                        targetAlarmTime = task.ReminderAt.Value.AddMinutes(-leadMinutes);
                    }
                    else if (settings.NotifyOnTaskDueDate && task.DueDate.HasValue)
                    {
                        var dueDate = task.DueDate.Value;
                        targetAlarmTime = dueDate.TimeOfDay == TimeSpan.Zero ? dueDate.Date.AddHours(9) : dueDate;
                    }

                    if (targetAlarmTime.HasValue && targetAlarmTime.Value > now)
                    {
                        ScheduleAlarm(context, task, targetAlarmTime.Value);
                        hasActiveTasks = true;
                    }
                    else if (task.DueDate.HasValue && task.DueDate.Value.Date <= today)
                    {
                        // Task is due today or overdue
                        hasActiveTasks = true;
                    }
                    else if (task.ReminderAt.HasValue && task.ReminderAt.Value <= now)
                    {
                        // Reminder has already arrived
                        hasActiveTasks = true;
                    }
                    else
                    {
                        CancelAlarm(context, task.Id);
                    }
                }

                // If repeating notifications are enabled or there are active tasks due today/overdue,
                // schedule the background periodic check alarm.
                if (hasActiveTasks)
                {
                    int intervalMinutes = settings.NotificationRepeatIntervalMinutes > 0
                        ? settings.NotificationRepeatIntervalMinutes
                        : 15;
                    SchedulePeriodicCheck(context, intervalMinutes);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[TaskAlarmScheduler] RescheduleAll error: {ex.Message}");
            }
        });
    }
}
