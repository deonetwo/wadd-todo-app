namespace Wadd.Core.Helpers;

public static class AppDataHelper
{
    public static string GetWaddDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("WADD_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            Directory.CreateDirectory(overrideDir);
            return overrideDir;
        }

        var defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wadd");
        Directory.CreateDirectory(defaultDir);
        return defaultDir;
    }

    public static string GetWaddFilePath(string fileName)
    {
        return Path.Combine(GetWaddDirectory(), fileName);
    }

    public static string GetLogsDirectory()
    {
        var logsDir = Path.Combine(GetWaddDirectory(), "Logs");
        Directory.CreateDirectory(logsDir);
        return logsDir;
    }

    public static string GetCurrentLogFilePath()
    {
        return Path.Combine(GetLogsDirectory(), $"wadd-{DateTime.Now:yyyy-MM-dd}.log");
    }
}
