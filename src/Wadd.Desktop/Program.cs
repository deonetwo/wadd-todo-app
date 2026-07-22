using Avalonia;
using Wadd.UI;

namespace Wadd.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.WriteAllText(logPath, $"[CRASH] {DateTime.Now}\nException: {ex?.ToString()}\n");
            Console.WriteLine($"[CRASH] {ex}");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.AppendAllText(logPath, $"[UNOBSERVED EXCEPTION] {DateTime.Now}\nException: {e.Exception}\n");
            Console.WriteLine($"[UNOBSERVED EXCEPTION] {e.Exception}");
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.WriteAllText(logPath, $"[FATAL CRASH] {DateTime.Now}\nException: {ex}\n");
            Console.WriteLine($"[FATAL CRASH] {ex}");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
