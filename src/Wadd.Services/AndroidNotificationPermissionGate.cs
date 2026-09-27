using System;
using System.Threading.Tasks;
using Wadd.Core.Logging;

namespace Wadd.Services;

/// <summary>
/// Handles Android 13+ (API 33+) runtime notification permission checks before posting native notifications.
/// Ensures that permission requests are properly awaited rather than fired-and-forgotten.
/// </summary>
public static class AndroidNotificationPermissionGate
{
    /// <summary>
    /// Evaluates whether a notification can be posted.
    /// If running on Android 33+ and permission is not yet granted, awaits the permission request.
    /// Returns true if permission is granted or not required; returns false if permission was denied.
    /// </summary>
    public static async Task<bool> ShouldProceedWithNotificationAsync(
        bool isAndroid33OrHigher,
        bool isPermissionGranted,
        Func<Task<bool>> requestPermissionAsync)
    {
        if (!isAndroid33OrHigher || isPermissionGranted)
        {
            return true;
        }

        try
        {
            var granted = await requestPermissionAsync();
            if (!granted)
            {
                AppLogger.LogWarning("AndroidNotificationService", "POST_NOTIFICATIONS permission not granted; skipping notification.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogError("AndroidNotificationService", "Error requesting notification permission", ex);
            return false;
        }
    }
}
