using System;
using System.Collections.Generic;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;

namespace Wadd.Core.Helpers;

/// <summary>
/// Helper for Android notification channel ID generation, deterministic hashing of channel settings,
/// and synchronization/cleanup of stale and legacy channels.
/// </summary>
public static class AndroidNotificationChannelHelper
{
    public const string ChannelIdPrefix = "wadd_task_reminders_v3_";
    public const string ChannelName = "Wadd Task Reminders";
    public const string ChannelDesc = "Task due dates and reminder alerts";

    public static readonly string[] LegacyChannelIds =
    {
        "wadd_task_reminders",
        "wadd_task_reminders_v2",
        "wadd_task_reminders_v2_silent"
    };

    /// <summary>
    /// Builds a deterministic channel ID that encodes AndroidHighPriorityChannel
    /// and AndroidVibration so changes to either setting route to a fresh channel.
    /// Notification sound is retired from channel encoding; Android uses default system sound.
    /// </summary>
    public static string BuildChannelId(AppSettingsData settings) =>
        $"{ChannelIdPrefix}{(settings.AndroidHighPriorityChannel ? "h1" : "h0")}_" +
        $"{(settings.AndroidVibration ? "v1" : "v0")}";

    /// <summary>
    /// Synchronizes notification channels:
    /// 1. Deletes legacy channel IDs ("wadd_task_reminders", "wadd_task_reminders_v2", "wadd_task_reminders_v2_silent").
    /// 2. Deletes stale v3 channel IDs that do not match the currently active target channel ID.
    /// 3. Ensures the target channel ID is created with current settings.
    /// </summary>
    public static void SyncChannels(IAndroidNotificationChannelManager manager, AppSettingsData settings)
    {
        var targetChannelId = BuildChannelId(settings);

        // 1. Delete legacy channels
        foreach (var legacyId in LegacyChannelIds)
        {
            try
            {
                manager.DeleteNotificationChannel(legacyId);
            }
            catch { }
        }

        // 2. Delete stale v3 channels that don't match the current target
        try
        {
            var existingIds = manager.GetNotificationChannelIds();
            if (existingIds != null)
            {
                foreach (var existingId in existingIds)
                {
                    if (string.IsNullOrEmpty(existingId)) continue;

                    if (existingId.StartsWith(ChannelIdPrefix, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(existingId, targetChannelId, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            manager.DeleteNotificationChannel(existingId);
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        // 3. Create or ensure target channel
        try
        {
            manager.CreateNotificationChannel(
                targetChannelId,
                ChannelName,
                ChannelDesc,
                settings.AndroidHighPriorityChannel,
                settings.AndroidVibration);
        }
        catch { }
    }
}
