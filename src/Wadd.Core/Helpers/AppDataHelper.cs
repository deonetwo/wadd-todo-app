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
}
