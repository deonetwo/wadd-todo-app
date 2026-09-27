using System.Collections.Generic;

namespace Wadd.Core.Interfaces;

/// <summary>
/// Abstraction for Android NotificationChannel management, allowing channel synchronization
/// and cleanup logic to be unit tested without requiring the Android runtime.
/// </summary>
public interface IAndroidNotificationChannelManager
{
    /// <summary>
    /// Gets the IDs of all existing notification channels.
    /// </summary>
    IEnumerable<string> GetNotificationChannelIds();

    /// <summary>
    /// Deletes the specified notification channel by ID.
    /// </summary>
    void DeleteNotificationChannel(string channelId);

    /// <summary>
    /// Creates or updates a notification channel with the specified parameters.
    /// </summary>
    void CreateNotificationChannel(string channelId, string channelName, string channelDesc, bool highPriority, bool vibration);
}
