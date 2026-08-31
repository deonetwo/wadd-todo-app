using System;
using System.IO;
using Wadd.Core.Enums;
using Wadd.Core.Helpers;
using Wadd.Core.Models;
using Xunit;

namespace Wadd.Tests;

[Collection("AppSettingsTests")]
public class ThemePersistenceTests
{
    [Fact]
    public void AppSettingsData_DefaultThemeMode_IsSystem()
    {
        var settings = new AppSettingsData();
        Assert.Equal(ThemeMode.System, settings.ThemeMode);
        Assert.Equal("Standard", settings.TasksViewLayout);
        Assert.True(settings.ShowNotePreviewsInList);
    }

    [Theory]
    [InlineData(ThemeMode.Dark)]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.System)]
    public void AppSettingsHelper_SaveAndLoadSettings_PersistsThemeMode(ThemeMode themeMode)
    {
        // Set temp directory for isolated test execution
        var tempPath = Path.Combine(Path.GetTempPath(), "WaddTest_" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("WADD_DATA_DIR", tempPath);

        try
        {
            var settings = new AppSettingsData
            {
                ThemeMode = themeMode,
                TasksViewLayout = "Focus",
                ShowNotePreviewsInList = false
            };

            AppSettingsHelper.SaveSettings(settings);

            var loaded = AppSettingsHelper.LoadSettings();

            Assert.Equal(themeMode, loaded.ThemeMode);
            Assert.Equal("Focus", loaded.TasksViewLayout);
            Assert.False(loaded.ShowNotePreviewsInList);
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
}
