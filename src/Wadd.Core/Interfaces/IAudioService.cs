namespace Wadd.Core.Interfaces;

/// <summary>
/// Service responsible for playing custom sound effects and notification chimes.
/// </summary>
public interface IAudioService
{
    /// <summary>
    /// Plays the sound effect when a task, milestone, or goal is completed.
    /// </summary>
    void PlayCompletedSound();

    /// <summary>
    /// Plays the sound effect when a notification, reminder, or alert shows up.
    /// </summary>
    void PlayNotificationSound();

    /// <summary>
    /// Plays a custom sound file by filename (e.g. "completed.mp3").
    /// </summary>
    void PlaySound(string soundFileName);
}
