using System;
using System.Reflection;
using System.Threading.Tasks;
using Wadd.Core.Interfaces;

namespace Wadd.Services;

/// <summary>
/// Cross-platform Windows notification service base and in-app event broker.
/// When running on Windows Desktop, the full native Action Center implementation
/// from Wadd.Windows is registered into DI.
/// </summary>
public class WindowsNotificationService : INotificationService
{
    public static event Action<string, string>? NotificationTriggered;

    public static void InvokeNotificationTriggered(string title, string message)
    {
        NotificationTriggered?.Invoke(title, message);
    }

    public static string? EnsureLogoFileOnDisk()
    {
        var winType = Type.GetType("Wadd.Windows.WindowsNotificationService, Wadd.Windows");
        var method = winType?.GetMethod("EnsureLogoFileOnDisk", BindingFlags.Public | BindingFlags.Static);
        return method?.Invoke(null, null) as string;
    }

    public bool IsSupported => OperatingSystem.IsWindows();

    public Task<bool> RequestPermissionAsync() => Task.FromResult(true);

    public Task ShowNotificationAsync(string title, string message, string? tag = null)
    {
        InvokeNotificationTriggered(title, message);
        return Task.CompletedTask;
    }

    public Task CancelNotificationAsync(string tag) => Task.CompletedTask;
}
