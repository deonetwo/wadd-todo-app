using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using DesktopNotifications.Windows;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Wadd.Tests")]

namespace Wadd.Windows;

/// <summary>
/// Windows desktop notification service supporting native Action Center toast alerts,
/// audio chimes, and in-app notification dispatch.
/// Uses DesktopNotificationsNet8.Windows for AUMID/Start Menu shortcut bootstrapping
/// and native WinRT projections (Windows.UI.Notifications) for toast dispatch.
/// </summary>
public class WindowsNotificationService : INotificationService
{
    public static event Action<string, string>? NotificationTriggered;
    public static event Action<string?>? ToastActivated;

    private static bool _isAumidRegistered;
    private static readonly object _aumidLock = new();

    internal static Action<string, string, bool, string?> ToastDispatcher { get; set; } = NativeWinRtToastDispatcher;
    internal static Action AlertSoundPlayer { get; set; } = PlayAlertSound;

    public bool IsSupported => OperatingSystem.IsWindows();

    public Task<bool> RequestPermissionAsync()
    {
        // Windows desktop applications do not require a runtime permission prompt
        return Task.FromResult(true);
    }

    public async Task ShowNotificationAsync(string title, string message, string? tag = null)
    {
        var settings = AppSettingsHelper.LoadSettings();
        if (!settings.EnableNotifications)
        {
            return;
        }

        // 1. Trigger in-app notification event for live UI display
        try
        {
            Wadd.Services.WindowsNotificationService.InvokeNotificationTriggered(title, message);
            NotificationTriggered?.Invoke(title, message);
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("WindowsNotificationService", "Error triggering in-app notification", ex);
        }

        // 2. Play custom notification sound (notification.mp3)
        if (settings.PlayNotificationSound)
        {
            try
            {
                var audioService = new Wadd.Services.AudioService();
                audioService.PlayNotificationSound();
            }
            catch (Exception ex)
            {
                Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Failed to play notification sound", ex);
            }
        }

        // 3. Dispatch native Windows toast banner (with silent audio so it does not collide with notification.mp3)
        if (OperatingSystem.IsWindows() && settings.WindowsToastNotifications)
        {
            await Task.Run(() => DispatchNativeWindowsToast(title, message, false, tag));
        }
    }

    public Task CancelNotificationAsync(string tag)
    {
        // Native Windows toasts are self-expiring; no-op
        return Task.CompletedTask;
    }

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    private static void PlayAlertSound()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                MessageBeep(0x40); // MB_ICONASTERISK (Windows chime)
            }
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Could not play notification chime", ex);
        }
    }

    public static string? EnsureLogoFileOnDisk()
    {
        try
        {
            var appDataSquare = AppDataHelper.GetWaddFilePath("logo_square.png");
            var appDataLegacy = AppDataHelper.GetWaddFilePath("logo.png");

            // 1. Check if logo_square.png already exists in LocalAppData and is non-empty
            if (File.Exists(appDataSquare) && new FileInfo(appDataSquare).Length > 0)
            {
                return Path.GetFullPath(appDataSquare);
            }

            // 2. Check application directory or Assets subfolder
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var localAsset = Path.Combine(appDir, "Assets", "logo_square.png");
            if (File.Exists(localAsset))
            {
                File.Copy(localAsset, appDataSquare, true);
                try { File.Copy(localAsset, appDataLegacy, true); } catch { }
                return Path.GetFullPath(appDataSquare);
            }

            // 3. Extract from Avalonia embedded asset resource
            try
            {
                var uri = new Uri("avares://Wadd.UI/Assets/logo_square.png");
                if (Avalonia.Platform.AssetLoader.Exists(uri))
                {
                    using var stream = Avalonia.Platform.AssetLoader.Open(uri);
                    using var fs = File.Create(appDataSquare);
                    stream.CopyTo(fs);
                    try { File.Copy(appDataSquare, appDataLegacy, true); } catch { }
                    return Path.GetFullPath(appDataSquare);
                }
            }
            catch { }

            // 4. Walk up directory tree to find repository source asset
            var dir = new DirectoryInfo(appDir);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Wadd.UI", "Assets", "logo_square.png");
                if (File.Exists(candidate))
                {
                    File.Copy(candidate, appDataSquare, true);
                    try { File.Copy(candidate, appDataLegacy, true); } catch { }
                    return Path.GetFullPath(appDataSquare);
                }
                dir = dir.Parent;
            }
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Could not prepare logo on disk", ex);
        }

        return null;
    }

    public static void EnsureAumidAndShortcutRegistered()
    {
        if (_isAumidRegistered || !OperatingSystem.IsWindows()) return;
        lock (_aumidLock)
        {
            if (_isAumidRegistered) return;
            try
            {
                var appCtx = WindowsApplicationContext.FromCurrentProcess("Wadd ToDo", "Wadd.Todo");
                using var mgr = new WindowsNotificationManager(appCtx);
                mgr.Initialize().GetAwaiter().GetResult();
                _isAumidRegistered = true;
            }
            catch (Exception ex)
            {
                Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Could not bootstrap AUMID and Start Menu shortcut", ex);
            }
        }
    }

    public static void DispatchNativeWindowsToast(string title, string message, bool playSound, string? tag = null)
    {
        try
        {
            ToastDispatcher(title, message, playSound, tag);
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Error sending native Windows toast", ex);
            try
            {
                AlertSoundPlayer();
            }
            catch (Exception soundEx)
            {
                Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Could not play fallback alert sound", soundEx);
            }
        }
    }

    public static ToastNotification CreateToastNotification(string title, string message, bool playSound, string? tag = null)
    {
        var safeTitle = System.Security.SecurityElement.Escape(title) ?? "Wadd Reminder";
        var safeMessage = System.Security.SecurityElement.Escape(message) ?? string.Empty;
        var audioXml = playSound ? string.Empty : "<audio silent=\"true\"/>";
        var toastXml = $"<toast launch=\"action=openApp\" scenario=\"reminder\"><visual><binding template=\"ToastGeneric\"><text>{safeTitle}</text><text>{safeMessage}</text></binding></visual>{audioXml}</toast>";

        var uniqueTag = string.IsNullOrWhiteSpace(tag) ? Guid.NewGuid().ToString() : tag;

        var xmlDoc = new XmlDocument();
        xmlDoc.LoadXml(toastXml);

        var toast = new ToastNotification(xmlDoc)
        {
            Tag = uniqueTag,
            Group = "WaddTasks",
            ExpirationTime = DateTimeOffset.Now.AddDays(2)
        };

        return toast;
    }

    private static void NativeWinRtToastDispatcher(string title, string message, bool playSound, string? tag = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _ = EnsureLogoFileOnDisk();
        EnsureAumidAndShortcutRegistered();

        var toast = CreateToastNotification(title, message, playSound, tag);
        toast.Activated += (sender, args) =>
        {
            try
            {
                var launchArgs = (args as ToastActivatedEventArgs)?.Arguments;
                ToastActivated?.Invoke(launchArgs);

                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        var appLifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                        if (appLifetime?.MainWindow is Window window)
                        {
                            window.WindowState = WindowState.Normal;
                            window.Show();
                            window.Activate();
                            window.Topmost = true;
                            window.Topmost = false;
                            window.Focus();
                        }
                    }
                    catch (Exception ex)
                    {
                        Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Failed to bring window to front on toast activation", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                Wadd.Core.Logging.AppLogger.LogWarning("WindowsNotificationService", "Error handling toast activation", ex);
            }
        };

        var notifier = ToastNotificationManager.CreateToastNotifier("Wadd.Todo");
        notifier.Show(toast);
    }
}
