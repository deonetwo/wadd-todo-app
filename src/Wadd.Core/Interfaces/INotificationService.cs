using System.Threading.Tasks;

namespace Wadd.Core.Interfaces;

/// <summary>
/// Cross-platform notification abstraction for Windows desktop and Android devices.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Gets whether native notification delivery is supported on the current platform.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Requests runtime notification permission on platforms that require explicit authorization (e.g. Android 13+).
    /// </summary>
    Task<bool> RequestPermissionAsync();

    /// <summary>
    /// Displays a notification to the user according to current user preferences.
    /// </summary>
    /// <param name="title">Notification title (e.g. task title or reminder heading).</param>
    /// <param name="message">Notification body text or notes preview.</param>
    /// <param name="tag">Optional notification identifier / tag for replacement or cancellation.</param>
    Task ShowNotificationAsync(string title, string message, string? tag = null);

    /// <summary>
    /// Cancels or removes an active notification by its tag or identifier.
    /// </summary>
    /// <param name="tag">The identifier tag of the notification to cancel.</param>
    Task CancelNotificationAsync(string tag);
}
