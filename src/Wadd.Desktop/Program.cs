using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Wadd.UI;

namespace Wadd.Desktop;

internal static class Program
{
    private const string MutexName = @"Local\Wadd_Desktop_SingleInstance_Mutex_98a72b";
    private const string EventName = @"Local\Wadd_Desktop_BringToFront_Event_98a72b";

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _singleInstanceEvent;
    private static RegisteredWaitHandle? _registeredWaitHandle;

    [STAThread]
    public static void Main(string[] args)
    {
        var enUSInfo = new System.Globalization.CultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = enUSInfo;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = enUSInfo;
        System.Threading.Thread.CurrentThread.CurrentCulture = enUSInfo;
        System.Threading.Thread.CurrentThread.CurrentUICulture = enUSInfo;

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

        bool isNewInstance;
        try
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out isNewInstance);
        }
        catch (AbandonedMutexException)
        {
            isNewInstance = true;
        }

        if (!isNewInstance)
        {
            // Another instance is already running. Signal it to restore/focus its window and exit immediately.
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    if (EventWaitHandle.TryOpenExisting(EventName, out var existingEvent))
                    {
                        existingEvent.Set();
                        existingEvent.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"[SingleInstance] Failed to signal existing instance: {ex.Message}");
                }
            }
            return;
        }

        try
        {
            _singleInstanceEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            _registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(
                _singleInstanceEvent,
                (state, timedOut) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (Application.Current is App app)
                        {
                            app.ShowMainWindow();
                        }
                    });
                },
                null,
                Timeout.Infinite,
                false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[SingleInstance] Failed to register bring-to-front event: {ex.Message}");
        }

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
        finally
        {
            _registeredWaitHandle?.Unregister(null);
            _singleInstanceEvent?.Dispose();
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch { }
            _singleInstanceMutex?.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
