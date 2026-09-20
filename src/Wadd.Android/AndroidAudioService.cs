using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Android.Content;
using Android.Media;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;
using Wadd.Core.Logging;

namespace Wadd.Android;

/// <summary>
/// High-performance, low-latency audio playback service for Android using SoundPool.
/// Pre-loads short UI sounds into uncompressed memory buffers for instant, glitch-free playback
/// without the overhead, latency, or mediaserver exhaustion of MediaPlayer.
/// </summary>
public class AndroidAudioService : IAudioService, IDisposable
{
    private readonly object _initLock = new();
    private SoundPool? _soundPool;
    private int _completedSoundId;
    private int _notificationSoundId;
    private readonly ConcurrentDictionary<int, bool> _loadedSounds = new();
    private bool _isDisposed;

    public AndroidAudioService()
    {
        InitializeSoundPool();
    }

    private void InitializeSoundPool()
    {
        lock (_initLock)
        {
            if (_soundPool != null || _isDisposed) return;

            try
            {
                var context = MainActivity.Instance ?? global::Android.App.Application.Context;
                if (context == null)
                {
                    AppLogger.LogWarning("AndroidAudioService", "No Android context available to initialize SoundPool");
                    return;
                }

                var audioAttrs = new AudioAttributes.Builder()
                    .SetUsage(AudioUsageKind.AssistanceSonification)?
                    .SetContentType(AudioContentType.Sonification)?
                    .Build();

                var builder = new SoundPool.Builder();
                builder.SetMaxStreams(4);

                if (audioAttrs != null)
                {
                    builder.SetAudioAttributes(audioAttrs);
                }

                var pool = builder.Build();
                if (pool == null)
                {
                    AppLogger.LogWarning("AndroidAudioService", "SoundPool.Builder returned null");
                    return;
                }

                pool.LoadComplete += (s, e) =>
                {
                    if (e.Status == 0) // 0 indicates success in SoundPool.OnLoadCompleteListener
                    {
                        _loadedSounds[e.SampleId] = true;
                    }
                    else
                    {
                        AppLogger.LogWarning("AndroidAudioService", $"Sound sample {e.SampleId} failed to load with status {e.Status}");
                    }
                };

                _completedSoundId = pool.Load(context, Resource.Raw.completed, 1);
                _notificationSoundId = pool.Load(context, Resource.Raw.notification, 1);

                _soundPool = pool;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("AndroidAudioService", "Failed to initialize SoundPool", ex);
            }
        }
    }

    public void PlayCompletedSound()
    {
        try
        {
            var settings = AppSettingsHelper.LoadSettings();
            if (!settings.PlayTaskCompletedSound) return;

            PlaySample(_completedSoundId, Resource.Raw.completed);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("AndroidAudioService", "Failed to trigger completed sound", ex);
        }
    }

    public void PlayNotificationSound()
    {
        try
        {
            var settings = AppSettingsHelper.LoadSettings();
            if (!settings.PlayNotificationSound) return;

            PlaySample(_notificationSoundId, Resource.Raw.notification);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("AndroidAudioService", "Failed to trigger notification sound", ex);
        }
    }

    public void PlaySound(string soundFileName)
    {
        if (string.IsNullOrWhiteSpace(soundFileName)) return;

        try
        {
            switch (soundFileName.ToLowerInvariant())
            {
                case "completed.mp3":
                    PlayCompletedSound();
                    break;
                case "notification.mp3":
                    PlayNotificationSound();
                    break;
                default:
                    AppLogger.LogWarning("AndroidAudioService", $"Unknown sound file requested: {soundFileName}");
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning("AndroidAudioService", $"Failed to play sound '{soundFileName}'", ex);
        }
    }

    private void PlaySample(int soundId, int rawResourceId)
    {
        if (_isDisposed) return;

        if (_soundPool == null)
        {
            InitializeSoundPool();
        }

        var pool = _soundPool;
        if (pool == null || soundId == 0) return;

        if (_loadedSounds.TryGetValue(soundId, out var loaded) && loaded)
        {
            pool.Play(soundId, 1.0f, 1.0f, 1, 0, 1.0f);
        }
        else
        {
            // If invoked before async decoding completes (e.g. immediately on first app frame), wait briefly
            Task.Run(async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(50);
                    if (_loadedSounds.TryGetValue(soundId, out var isReady) && isReady && !_isDisposed)
                    {
                        pool.Play(soundId, 1.0f, 1.0f, 1, 0, 1.0f);
                        break;
                    }
                }
            });
        }
    }

    public void Dispose()
    {
        lock (_initLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                _soundPool?.Release();
                _soundPool = null;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning("AndroidAudioService", "Error releasing SoundPool", ex);
            }
        }
    }
}
