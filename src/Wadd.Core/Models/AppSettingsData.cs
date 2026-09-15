using Wadd.Core.Enums;

namespace Wadd.Core.Models;

public class AppSettingsData
{
    public string TasksViewLayout { get; set; } = "Standard";
    public string UpcomingTasksRange { get; set; } = "All";
    public bool ShowNotePreviewsInList { get; set; } = true;
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;

    // Desktop System Tray & Auto-Startup Settings
    public bool AutoStartOnBoot { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool EnableTrayIcon { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;

    // Notification Settings (Cross-Platform & Platform-Specific)
    public bool EnableNotifications { get; set; } = true;
    public bool NotifyOnTaskReminder { get; set; } = true;
    public bool NotifyOnOverdueTasks { get; set; } = true;
    public bool NotifyOnTaskDueDate { get; set; } = true;
    public int NotificationLeadTimeMinutes { get; set; } = 0;
    public int NotificationRepeatIntervalMinutes { get; set; } = 0;
    public bool PlayNotificationSound { get; set; } = true;

    // Windows Desktop Notification Settings
    public bool WindowsToastNotifications { get; set; } = true;
    public bool WindowsNotificationIncludeNotes { get; set; } = true;

    // Android Mobile Notification Settings
    public bool AndroidVibration { get; set; } = true;
    public bool AndroidHighPriorityChannel { get; set; } = true;
    public bool AndroidStickyReminders { get; set; } = false;

    // AI Integration Settings
    public string AiProvider { get; set; } = "Gemini";
    public string AiApiKey { get; set; } = string.Empty;
    public string AiCustomBaseUrl { get; set; } = string.Empty;
    public string AiCustomModel { get; set; } = string.Empty;

    // Backwards compatibility property
    public string GeminiApiKey
    {
        get => string.IsNullOrWhiteSpace(AiApiKey) ? string.Empty : AiApiKey;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                AiApiKey = value;
            }
        }
    }
}

