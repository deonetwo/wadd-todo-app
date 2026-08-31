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
}

