using System;
using Android.Content;
using Android.Media;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;
using Wadd.Core.Logging;

namespace Wadd.Android;

/// <summary>
/// Android-native audio playback service that uses MediaPlayer with raw resources
/// bundled in the APK, replacing the Windows-only AudioService on Android.
/// </summary>
public class AndroidAudioService : IAudioService
{
    public void PlayCompletedSound()
    {
        try
        {
            var settings = AppSettingsHelper.LoadSettings();
            if (!settings.PlayTaskCompletedSound) return;

            PlayRawResource(Resource.Raw.completed);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("AndroidAudioService", "Failed to play completed sound", ex);
        }
    }

    public void PlayNotificationSound()
    {
        try
        {
            var settings = AppSettingsHelper.LoadSettings();
            if (!settings.PlayNotificationSound) return;

            PlayRawResource(Resource.Raw.notification);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("AndroidAudioService", "Failed to play notification sound", ex);
        }
    }

    public void PlaySound(string soundFileName)
    {
        if (string.IsNullOrWhiteSpace(soundFileName)) return;

        try
        {
            var resourceId = soundFileName.ToLowerInvariant() switch
            {
                "completed.mp3" => Resource.Raw.completed,
                "notification.mp3" => Resource.Raw.notification,
                _ => 0
            };

            if (resourceId != 0)
            {
                PlayRawResource(resourceId);
            }
            else
            {
                AppLogger.LogWarning("AndroidAudioService", $"Unknown sound file requested: {soundFileName}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("AndroidAudioService", $"Failed to play sound '{soundFileName}'", ex);
        }
    }

    private static void PlayRawResource(int resourceId)
    {
        var context = MainActivity.Instance ?? global::Android.App.Application.Context;
        if (context == null)
        {
            AppLogger.LogWarning("AndroidAudioService", "No Android context available for audio playback");
            return;
        }

        // Fire-and-forget on a thread-pool thread so we never block the UI thread.
        // MediaPlayer is created, started, and released entirely within this lambda.
        System.Threading.Tasks.Task.Run(() =>
        {
            MediaPlayer? player = null;
            try
            {
                player = MediaPlayer.Create(context, resourceId);
                if (player == null)
                {
                    AppLogger.LogWarning("AndroidAudioService", $"MediaPlayer.Create returned null for resource {resourceId}");
                    return;
                }

                player.Completion += (_, _) => player.Release();
                player.Start();
                // player.Release() is called inside the Completion event handler above;
                // we intentionally do NOT release here so playback can finish.
                player = null;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning("AndroidAudioService", $"Error in MediaPlayer for resource {resourceId}", ex);
                try { player?.Release(); } catch { }
            }
        });
    }
}
