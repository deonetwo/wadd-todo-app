using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
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
    public const string ChannelName = AndroidNotificationChannelHelper.ChannelName;
    public const string ChannelDesc = AndroidNotificationChannelHelper.ChannelDesc;

    public static string BuildChannelId(AppSettingsData settings) =>
        AndroidNotificationChannelHelper.BuildChannelId(settings);

    public const int TasksSummaryNotificationId = NotificationTagHelper.TasksSummaryNotificationId;
    public static int GetNotificationId(string? tag) => NotificationTagHelper.GetNotificationId(tag);

    internal static Func<Task<bool>>? PermissionRequesterOverride { get; set; }
    internal static Action<Context, string, string, string?, AppSettingsData>? PostNativeNotificationOverride { get; set; }
    internal static IAndroidNotificationChannelManager? ChannelManagerOverride { get; set; }

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

            var targetChannelId = BuildChannelId(settings);

            // Ensure notification channel exists and stale channels are cleaned up (API 26+)
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O || ChannelManagerOverride != null)
            {
                var channelManager = ChannelManagerOverride ?? new AndroidNotificationChannelManagerAdapter(notificationManager, context);
                AndroidNotificationChannelHelper.SyncChannels(channelManager, settings);
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

/// <summary>
/// Adapts Android's native NotificationManager and NotificationChannel API to IAndroidNotificationChannelManager.
/// </summary>
internal class AndroidNotificationChannelManagerAdapter : IAndroidNotificationChannelManager
{
    private readonly NotificationManager _manager;
    private readonly Context _context;

    public AndroidNotificationChannelManagerAdapter(NotificationManager manager, Context context)
    {
        _manager = manager;
        _context = context;
    }

    public IEnumerable<string> GetNotificationChannelIds()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return Array.Empty<string>();
        var channels = _manager.NotificationChannels;
        if (channels == null) return Array.Empty<string>();

        var ids = new List<string>();
        foreach (var channel in channels)
        {
            if (!string.IsNullOrEmpty(channel?.Id))
            {
                ids.Add(channel.Id);
            }
        }
        return ids;
    }

    public void DeleteNotificationChannel(string channelId)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            _manager.DeleteNotificationChannel(channelId);
        }
    }

    public void CreateNotificationChannel(string channelId, string channelName, string channelDesc, bool highPriority, bool vibration)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var importance = highPriority
            ? NotificationImportance.High
            : NotificationImportance.Default;

        var channel = new NotificationChannel(channelId, channelName, importance)
        {
            Description = channelDesc
        };

        // Omitting SetSound leaves the channel with Android's system default notification sound URI.

        channel.EnableVibration(vibration);
        if (vibration)
        {
            channel.SetVibrationPattern(new long[] { 0, 250, 100, 250 });
        }

        _manager.CreateNotificationChannel(channel);
    }
}

