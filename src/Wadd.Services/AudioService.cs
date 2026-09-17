using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;
using Wadd.Core.Logging;

namespace Wadd.Services;

/// <summary>
/// High-performance audio playback service for task completion sounds and notification chimes.
/// </summary>
public class AudioService : IAudioService
{
    private const string CompletedSoundName = "completed.mp3";
    private const string NotificationSoundName = "notification.mp3";

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    public void PlayCompletedSound()
    {
        try
        {
            var settings = AppSettingsHelper.LoadSettings();
            if (!settings.PlayTaskCompletedSound)
            {
                return;
            }

            PlaySound(CompletedSoundName);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("AudioService", "Failed to trigger completed sound", ex);
        }
    }

    public void PlayNotificationSound()
    {
        try
        {
            var settings = AppSettingsHelper.LoadSettings();
            if (!settings.PlayNotificationSound)
            {
                return;
            }

            PlaySound(NotificationSoundName);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("AudioService", "Failed to trigger notification sound", ex);
        }
    }

    public void PlaySound(string soundFileName)
    {
        if (string.IsNullOrWhiteSpace(soundFileName)) return;

        try
        {
            var filePath = EnsureSoundFileOnDisk(soundFileName);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                AppLogger.LogWarning("AudioService", $"Sound file not found on disk: {soundFileName}");
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                PlayWindowsAudio(filePath);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("AudioService", $"Error playing sound '{soundFileName}'", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void PlayWindowsAudio(string filePath)
    {
        // Execute immediately in thread pool with zero process creation overhead
        Task.Run(async () =>
        {
            object? player = null;
            try
            {
                var wmpType = Type.GetTypeFromProgID("WMPlayer.OCX");
                if (wmpType != null)
                {
                    player = Activator.CreateInstance(wmpType);
                    if (player != null)
                    {
                        var settings = wmpType.InvokeMember("settings", BindingFlags.GetProperty, null, player, null);
                        settings?.GetType().InvokeMember("volume", BindingFlags.SetProperty, null, settings, new object[] { 100 });

                        string fullPath = Path.GetFullPath(filePath);
                        wmpType.InvokeMember("URL", BindingFlags.SetProperty, null, player, new object[] { fullPath });
                        var controls = wmpType.InvokeMember("controls", BindingFlags.GetProperty, null, player, null);
                        controls?.GetType().InvokeMember("play", BindingFlags.InvokeMethod, null, controls, null);

                        // Keep COM player alive for duration of the sound effect (3 seconds)
                        await Task.Delay(3000);
                    }
                }
                else
                {
                    MessageBeep(0);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning("AudioService", $"Error playing Windows audio for {filePath}", ex);
                try
                {
                    MessageBeep(0);
                }
                catch { }
            }
            finally
            {
                if (player != null && Marshal.IsComObject(player))
                {
                    try
                    {
                        Marshal.FinalReleaseComObject(player);
                    }
                    catch { }
                }
            }
        });
    }

    public static string? EnsureSoundFileOnDisk(string soundFileName)
    {
        try
        {
            var soundsDir = Path.Combine(AppDataHelper.GetWaddDirectory(), "Sounds");
            Directory.CreateDirectory(soundsDir);

            var targetPath = Path.Combine(soundsDir, soundFileName);

            // Check source candidates from application directory, Assets folder, and workspace
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            string?[] candidates =
            [
                Path.Combine(appDir, "Assets", soundFileName),
                Path.Combine(appDir, soundFileName),
                Path.Combine(Directory.GetCurrentDirectory(), "src", "Wadd.UI", "Assets", soundFileName),
                Path.Combine(Directory.GetCurrentDirectory(), soundFileName)
            ];

            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate) && new FileInfo(candidate).Length > 0)
                {
                    var sourceInfo = new FileInfo(candidate);
                    var targetInfo = File.Exists(targetPath) ? new FileInfo(targetPath) : null;

                    // Automatically update if target is missing, file size differs, or source is newer
                    if (targetInfo == null || targetInfo.Length != sourceInfo.Length || sourceInfo.LastWriteTimeUtc > targetInfo.LastWriteTimeUtc)
                    {
                        File.Copy(candidate, targetPath, true);
                    }

                    return targetPath;
                }
            }

            return File.Exists(targetPath) && new FileInfo(targetPath).Length > 0 ? targetPath : null;
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("AudioService", $"Error ensuring sound file on disk: {soundFileName}", ex);
            return null;
        }
    }
}
