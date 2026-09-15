using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;

namespace Wadd.Services;

/// <summary>
/// Windows desktop notification service supporting native Action Center toast alerts,
/// audio chimes, and in-app notification dispatch.
/// </summary>
public class WindowsNotificationService : INotificationService
{
    public static event Action<string, string>? NotificationTriggered;

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
            NotificationTriggered?.Invoke(title, message);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[WindowsNotificationService] Error triggering in-app notification: {ex.Message}");
        }

        // 2. Dispatch native Windows toast banner
        if (OperatingSystem.IsWindows())
        {
            await Task.Run(() => DispatchNativeWindowsToast(title, message, settings.PlayNotificationSound));
        }
    }

    public Task CancelNotificationAsync(string tag)
    {
        // Native Windows PowerShell toasts are self-expiring; no-op
        return Task.CompletedTask;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
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
            Trace.WriteLine($"[WindowsNotificationService] Could not play notification chime: {ex.Message}");
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
            Trace.WriteLine($"[WindowsNotificationService] Could not prepare logo on disk: {ex.Message}");
        }

        return null;
    }

    private static void DispatchNativeWindowsToast(string title, string message, bool playSound)
    {
        try
        {
            var logoPath = EnsureLogoFileOnDisk();

            // Escape special XML characters for safety
            var safeTitle = System.Security.SecurityElement.Escape(title) ?? "Wadd Reminder";
            var safeMessage = System.Security.SecurityElement.Escape(message) ?? string.Empty;

            var audioXml = playSound ? string.Empty : "<audio silent=\"true\"/>";

            // The logo is displayed at the top header via the AUMID IconUri registry setting;
            // no bottom/body image is added per user requirement.
            var toastXml = $"<toast><visual><binding template=\"ToastGeneric\"><text>{safeTitle}</text><text>{safeMessage}</text></binding></visual>{audioXml}</toast>";

            // PowerShell script using WinRT ToastNotificationManager with fresh AUMID registration and fallback
            var escapedLogoPath = logoPath?.Replace("'", "''") ?? string.Empty;
            var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null

$aumids = @('Wadd.Todo', 'Wadd.TodoApp')
foreach ($id in $aumids) {{
    $reg = ""HKCU:\Software\Classes\AppUserModelId\$id""
    if (!(Test-Path $reg)) {{ New-Item -Path $reg -Force | Out-Null }}
    Set-ItemProperty -Path $reg -Name 'DisplayName' -Value 'Wadd ToDo'
    if ('{escapedLogoPath}') {{ Set-ItemProperty -Path $reg -Name 'IconUri' -Value '{escapedLogoPath}' }}
    Set-ItemProperty -Path $reg -Name 'ShowInSettings' -Value 1 -Type DWord
}}

$xmlString = @'
{toastXml}
'@

$xml = [Windows.Data.Xml.Dom.XmlDocument]::new()
$xml.LoadXml($xmlString)
$toast = [Windows.UI.Notifications.ToastNotification]::new($xml)

try {{
    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Wadd.Todo').Show($toast)
}} catch {{
    try {{
        [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('Wadd.TodoApp').Show($toast)
    }} catch {{
        $fallbackAumid = '{{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}}\WindowsPowerShell\v1.0\powershell.exe'
        [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($fallbackAumid).Show($toast)
    }}
}}
";

            var encodedCommand = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -EncodedCommand {encodedCommand}",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var stdErr = process.StandardError.ReadToEnd();
                process.WaitForExit(3000);
                if (!string.IsNullOrWhiteSpace(stdErr))
                {
                    Trace.WriteLine($"[WindowsNotificationService] Toast script error: {stdErr}");
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[WindowsNotificationService] Error sending native Windows toast: {ex.Message}");
        }
    }
}
