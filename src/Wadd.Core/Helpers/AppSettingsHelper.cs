using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wadd.Core.Models;

namespace Wadd.Core.Helpers;

public static class AppSettingsHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettingsData LoadSettings()
    {
        try
        {
            var filePath = AppDataHelper.GetWaddFilePath("app_settings.json");
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var settings = JsonSerializer.Deserialize<AppSettingsData>(json, JsonOptions);
                if (settings != null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("AppSettingsHelper", "Error loading settings", ex);
        }

        return new AppSettingsData();
    }

    public static void SaveSettings(AppSettingsData settings)
    {
        try
        {
            var filePath = AppDataHelper.GetWaddFilePath("app_settings.json");
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("AppSettingsHelper", "Error saving settings", ex);
        }
    }
}
