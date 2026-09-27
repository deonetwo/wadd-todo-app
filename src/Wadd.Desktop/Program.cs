using System;
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
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Wadd.Core.Logging.AppLogger.LogCritical("Program", "Fatal AppDomain unhandled exception", ex);
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Wadd.Core.Logging.AppLogger.LogError("Program", "Unobserved task exception", e.Exception);
            e.SetObserved();
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
                    Wadd.Core.Logging.AppLogger.LogWarning("SingleInstance", "Failed to signal existing instance", ex);
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
            Wadd.Core.Logging.AppLogger.LogError("SingleInstance", "Failed to register bring-to-front event", ex);
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogCritical("Program", "Fatal desktop startup crash", ex);
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
