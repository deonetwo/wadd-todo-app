using System;
using System.IO;
using System.Text.Json;
using Wadd.Core.Enums;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Xunit;

namespace Wadd.Tests;

public class StartupAndTraySettingsTests
{
    [Fact]
    public void AppSettingsData_DefaultValues_AreCorrect()
    {
        var settings = new AppSettingsData();

        Assert.False(settings.AutoStartOnBoot);
        Assert.False(settings.StartMinimized);
        Assert.True(settings.EnableTrayIcon);
        Assert.True(settings.MinimizeToTray);
        Assert.True(settings.CloseToTray);
    }

    [Fact]
    public void AppSettingsHelper_SaveAndLoadSettings_PersistsStartupAndTraySettings()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "WaddTest_" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("WADD_DATA_DIR", tempPath);

        try
        {
            var settings = new AppSettingsData
            {
                ThemeMode = ThemeMode.Dark,
                TasksViewLayout = "Focus",
                UpcomingTasksRange = "Next7Days",
                ShowNotePreviewsInList = false,
                AutoStartOnBoot = true,
                StartMinimized = true,
                EnableTrayIcon = true,
                MinimizeToTray = false,
                CloseToTray = true
            };

            AppSettingsHelper.SaveSettings(settings);

            var loaded = AppSettingsHelper.LoadSettings();

            Assert.Equal(ThemeMode.Dark, loaded.ThemeMode);
            Assert.Equal("Focus", loaded.TasksViewLayout);
            Assert.Equal("Next7Days", loaded.UpcomingTasksRange);
            Assert.False(loaded.ShowNotePreviewsInList);
            Assert.True(loaded.AutoStartOnBoot);
            Assert.True(loaded.StartMinimized);
            Assert.True(loaded.EnableTrayIcon);
            Assert.False(loaded.MinimizeToTray);
            Assert.True(loaded.CloseToTray);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WADD_DATA_DIR", null);
            if (Directory.Exists(tempPath))
            {
                try { Directory.Delete(tempPath, true); } catch { }
            }
        }
    }

    [Fact]
    public void AppSettingsData_JsonSerialization_PreservesAllFields()
    {
        var original = new AppSettingsData
        {
            AutoStartOnBoot = true,
            StartMinimized = false,
            EnableTrayIcon = false,
            MinimizeToTray = true,
            CloseToTray = false
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<AppSettingsData>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.AutoStartOnBoot);
        Assert.False(deserialized.StartMinimized);
        Assert.False(deserialized.EnableTrayIcon);
        Assert.True(deserialized.MinimizeToTray);
        Assert.False(deserialized.CloseToTray);
    }

    private class MockStartupService : IStartupService
    {
        public bool IsSupported { get; set; } = true;
        public bool AutoStartEnabled { get; set; }
        public bool StartMinimizedFlag { get; set; }

        public bool IsAutoStartEnabled() => AutoStartEnabled;

        public void SetAutoStart(bool enable, bool startMinimized = false)
        {
            AutoStartEnabled = enable;
            StartMinimizedFlag = startMinimized;
        }
    }

    [Fact]
    public void MockStartupService_TogglesCorrectly()
    {
        var service = new MockStartupService();
        Assert.False(service.IsAutoStartEnabled());

        service.SetAutoStart(true, startMinimized: true);
        Assert.True(service.IsAutoStartEnabled());
        Assert.True(service.StartMinimizedFlag);

        service.SetAutoStart(false);
        Assert.False(service.IsAutoStartEnabled());
    }
}
