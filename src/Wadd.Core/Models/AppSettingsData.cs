using Wadd.Core.Enums;

namespace Wadd.Core.Models;

public class AppSettingsData
{
    public string TasksViewLayout { get; set; } = "Standard";
    public string UpcomingTasksRange { get; set; } = "All";
    public bool ShowNotePreviewsInList { get; set; } = true;
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
}

