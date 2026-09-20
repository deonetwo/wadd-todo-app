using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.OS;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;

namespace Wadd.Android;

/// <summary>
/// Native Android notification service implementing INotificationService via Android NotificationManager and NotificationChannel.
/// </summary>
public class AndroidNotificationService : INotificationService
{
    private const string ChannelId = "wadd_task_reminders_v2";
    private const string ChannelName = "Wadd Task Reminders";
    private const string ChannelDesc = "Task due dates and reminder alerts";

    private const string SilentChannelId = "wadd_task_reminders_v2_silent";
    private const string SilentChannelName = "Wadd Task Reminders (Silent)";

    public const int TasksSummaryNotificationId = NotificationTagHelper.TasksSummaryNotificationId;
    public static int GetNotificationId(string? tag) => NotificationTagHelper.GetNotificationId(tag);

    internal static Func<Task<bool>>? PermissionRequesterOverride { get; set; }
    internal static Action<Context, string, string, string?, AppSettingsData>? PostNativeNotificationOverride { get; set; }

    public bool IsSupported => true;

    public Task<bool> RequestPermissionAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var activity = MainActivity.Instance;
            if (activity != null)
            {
                return activity.RequestNotificationPermissionAsync();
            }
        }
        return Task.FromResult(true);
    }

    public async Task ShowNotificationAsync(string title, string message, string? tag = null)
    {
        var settings = AppSettingsHelper.LoadSettings();
        if (!settings.EnableNotifications)
        {
            return;
        }

        var context = MainActivity.Instance ?? Application.Context;
        if (context == null)
        {
            return;
        }

        if (PermissionRequesterOverride != null)
        {
            var granted = await PermissionRequesterOverride();
            if (!granted)
            {
                Wadd.Core.Logging.AppLogger.LogWarning("AndroidNotificationService", "POST_NOTIFICATIONS permission not granted; skipping notification.");
                return;
            }
        }
        else if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var activity = MainActivity.Instance;
            if (activity != null && activity.CheckSelfPermission("android.permission.POST_NOTIFICATIONS") != Permission.Granted)
            {
                var granted = await activity.RequestNotificationPermissionAsync();
                if (!granted)
                {
                    Wadd.Core.Logging.AppLogger.LogWarning("AndroidNotificationService", "POST_NOTIFICATIONS permission not granted; skipping notification.");
                    return;
                }
            }
        }

        if (PostNativeNotificationOverride != null)
        {
            PostNativeNotificationOverride(context, title, message, tag, settings);
        }
        else
        {
            PostNativeNotification(context, title, message, tag, settings);
        }
    }

    public static void PostNativeNotification(Context context, string title, string message, string? tag, AppSettingsData settings)
    {
        try
        {
            var notificationManager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
            if (notificationManager == null)
            {
                return;
            }

            var targetChannelId = settings.PlayNotificationSound ? ChannelId : SilentChannelId;
            var targetChannelName = settings.PlayNotificationSound ? ChannelName : SilentChannelName;

            // Ensure notification channel exists (API 26+)
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                try
                {
                    // Clean up legacy channel if it exists
                    notificationManager.DeleteNotificationChannel("wadd_task_reminders");
                }
                catch { }

                var importance = settings.AndroidHighPriorityChannel
                    ? NotificationImportance.High
                    : NotificationImportance.Default;

                var channel = new NotificationChannel(targetChannelId, targetChannelName, importance)
                {
                    Description = ChannelDesc
                };

                if (settings.PlayNotificationSound)
                {
                    var soundUri = global::Android.Net.Uri.Parse($"{ContentResolver.SchemeAndroidResource}://{context.PackageName}/{Resource.Raw.notification}");
                    var audioAttrs = new AudioAttributes.Builder()
                        .SetUsage(AudioUsageKind.NotificationEvent)?
                        .SetContentType(AudioContentType.Sonification)?
                        .Build();
                    channel.SetSound(soundUri, audioAttrs);
                }
                else
                {
                    channel.SetSound(null, null);
                }

                channel.EnableVibration(settings.AndroidVibration);
                if (settings.AndroidVibration)
                {
                    channel.SetVibrationPattern(new long[] { 0, 250, 100, 250 });
                }

                notificationManager.CreateNotificationChannel(channel);
            }

            // Create intent to bring MainActivity to foreground on tap
            var intent = new Intent(context, typeof(MainActivity));
            intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            var pendingFlags = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                pendingFlags |= PendingIntentFlags.Immutable;
            }

            var pendingIntent = PendingIntent.GetActivity(
                context,
                0,
                intent,
                pendingFlags);

            var builder = new Notification.Builder(context, targetChannelId)
                .SetContentTitle(title)
                .SetContentText(message)
                .SetStyle(new Notification.BigTextStyle().BigText(message))
                .SetSmallIcon(Resource.Drawable.icon)
                .SetContentIntent(pendingIntent)
                .SetAutoCancel(!settings.AndroidStickyReminders)
                .SetOngoing(settings.AndroidStickyReminders);

            try
            {
                var largeIcon = global::Android.Graphics.BitmapFactory.DecodeResource(context.Resources, Resource.Drawable.icon);
                if (largeIcon != null)
                {
                    builder.SetLargeIcon(largeIcon);
                }
            }
            catch (Exception ex)
            {
                Wadd.Core.Logging.AppLogger.LogWarning("AndroidNotificationService", "Error loading large icon", ex);
            }

            var notificationId = NotificationTagHelper.GetNotificationId(tag);
            notificationManager.Notify(tag, notificationId, builder.Build());
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("AndroidNotificationService", "Error posting notification", ex);
        }
    }

    public Task CancelNotificationAsync(string tag)
    {
        var context = MainActivity.Instance ?? Application.Context;
        if (context == null || string.IsNullOrWhiteSpace(tag))
        {
            return Task.CompletedTask;
        }

        try
        {
            var notificationManager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
            var notificationId = NotificationTagHelper.GetNotificationId(tag);
            notificationManager?.Cancel(tag, notificationId);

            if (Guid.TryParse(tag, out var taskId))
            {
                TaskAlarmScheduler.CancelAlarm(context, taskId);
            }
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("AndroidNotificationService", "Error cancelling notification", ex);
        }

        return Task.CompletedTask;
    }
}
