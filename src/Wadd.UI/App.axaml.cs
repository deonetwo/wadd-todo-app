using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Wadd.Services;
using Wadd.UI.ViewModels;
using Wadd.UI.Views;

namespace Wadd.UI;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }
    private TrayIcon? _trayIcon;
    private bool _isExplicitExit;

    public override void Initialize()
    {
        var enUSInfo = new System.Globalization.CultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = enUSInfo;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = enUSInfo;
        System.Threading.Thread.CurrentThread.CurrentCulture = enUSInfo;
        System.Threading.Thread.CurrentThread.CurrentUICulture = enUSInfo;

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Wadd.Core.Logging.AppLogger.LogCritical("AppDomain", "Unhandled domain exception occurred", ex);
            }
            else
            {
                Wadd.Core.Logging.AppLogger.LogCritical("AppDomain", $"Unhandled exception object: {e.ExceptionObject}");
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Wadd.Core.Logging.AppLogger.LogError("TaskScheduler", "Unobserved task exception occurred", e.Exception);
            e.SetObserved();
        };

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection();
        collection.AddWaddServices();

        if (OperatingSystem.IsAndroid())
        {
            var androidAuthType = Type.GetType("Wadd.Android.AndroidGoogleAuthService, Wadd.Android")
                                  ?? System.Reflection.Assembly.GetEntryAssembly()?.GetType("Wadd.Android.AndroidGoogleAuthService");
            if (androidAuthType != null)
            {
                collection.AddSingleton(typeof(Wadd.Core.Interfaces.INativeGoogleAuthService), androidAuthType);
            }

            var androidNotifType = Type.GetType("Wadd.Android.AndroidNotificationService, Wadd.Android")
                                  ?? System.Reflection.Assembly.GetEntryAssembly()?.GetType("Wadd.Android.AndroidNotificationService");
            if (androidNotifType != null)
            {
                collection.AddSingleton(typeof(Wadd.Core.Interfaces.INotificationService), androidNotifType);
            }

            var androidAudioType = Type.GetType("Wadd.Android.AndroidAudioService, Wadd.Android")
                                  ?? System.Reflection.Assembly.GetEntryAssembly()?.GetType("Wadd.Android.AndroidAudioService");
            if (androidAudioType != null)
            {
                collection.AddSingleton(typeof(Wadd.Core.Interfaces.IAudioService), androidAudioType);
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            var winNotifType = Type.GetType("Wadd.Windows.WindowsNotificationService, Wadd.Windows")
                              ?? System.Reflection.Assembly.GetEntryAssembly()?.GetType("Wadd.Windows.WindowsNotificationService");
            if (winNotifType != null)
            {
                collection.AddSingleton(typeof(Wadd.Core.Interfaces.INotificationService), winNotifType);
            }
        }

        collection.AddTransient<MainViewModel>();

        Services = collection.BuildServiceProvider();
        ServiceCollectionExtensions.SetSharedServiceProvider(Services);

        var mainViewModel = Services.GetRequiredService<MainViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            // Check if launched with --autostart or --minimized
            var isAutoStart = desktop.Args != null &&
                (desktop.Args.Contains("--autostart") || desktop.Args.Contains("--minimized"));

            if (isAutoStart && mainViewModel.StartMinimized)
            {
                mainWindow.WindowState = WindowState.Minimized;
                EventHandler? onInitialOpened = null;
                onInitialOpened = (s, e) =>
                {
                    mainWindow.Opened -= onInitialOpened;
                    Dispatcher.UIThread.Post(() =>
                    {
                        mainWindow.Hide();
                        mainWindow.WindowState = WindowState.Normal;
                    });
                };
                mainWindow.Opened += onInitialOpened;
            }

            mainWindow.Closing += (s, e) =>
            {
                SaveThemeAndSettings();
                if (!_isExplicitExit && mainViewModel.CloseToTray)
                {
                    e.Cancel = true;
                    mainWindow.Hide();
                    mainWindow.WindowState = WindowState.Normal;
                }
                else if (!_isExplicitExit && !mainViewModel.CloseToTray)
                {
                    ExitApplication(desktop);
                }
            };

            mainWindow.PropertyChanged += (s, e) =>
            {
                if (e.Property == Window.WindowStateProperty &&
                    mainWindow.WindowState == WindowState.Minimized &&
                    mainWindow.IsVisible &&
                    mainViewModel.MinimizeToTray)
                {
                    mainWindow.Hide();
                    mainWindow.WindowState = WindowState.Normal;
                }
            };

            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += (s, e) => SaveThemeAndSettings();
            desktop.Exit += (s, e) => SaveThemeAndSettings();

            SetupSystemTray(desktop, mainViewModel);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView
            {
                DataContext = mainViewModel
            };
        }

        AppDomain.CurrentDomain.ProcessExit += (s, e) => SaveThemeAndSettings();

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupSystemTray(IClassicDesktopStyleApplicationLifetime desktop, MainViewModel viewModel)
    {
        try
        {
            _trayIcon = new TrayIcon
            {
                ToolTipText = "Wadd - ToDo Application",
                IsVisible = viewModel.EnableTrayIcon
            };

            try
            {
                var uri = new Uri("avares://Wadd.UI/Assets/logo.ico");
                _trayIcon.Icon = new WindowIcon(AssetLoader.Open(uri));
            }
            catch (Exception ex)
            {
                Wadd.Core.Logging.AppLogger.LogWarning("App", "Error loading tray icon", ex);
            }

            _trayIcon.Clicked += (s, e) => ShowMainWindow(desktop);

            var menu = new NativeMenu();

            var openItem = new NativeMenuItem("Open Wadd ToDo");
            openItem.Click += (s, e) => ShowMainWindow(desktop);
            menu.Add(openItem);

            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += (s, e) => ExitApplication(desktop);
            menu.Add(exitItem);

            _trayIcon.Menu = menu;

            var trayIcons = new TrayIcons { _trayIcon };
            TrayIcon.SetIcons(this, trayIcons);

            viewModel.TrayIconVisibilityChanged += (s, isVisible) =>
            {
                if (_trayIcon != null)
                {
                    _trayIcon.IsVisible = isVisible;
                }
            };
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("App", "Error setting up system tray", ex);
        }
    }

    public void ShowMainWindow(IClassicDesktopStyleApplicationLifetime? desktop = null)
    {
        var targetLifetime = desktop ?? ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (targetLifetime?.MainWindow is Window window)
        {
            window.WindowState = WindowState.Normal;
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
            window.Focus();
        }
    }

    public void ExitApplication(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _isExplicitExit = true;
        SaveThemeAndSettings();
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
        }
        desktop.Shutdown();
    }

    public static void SaveThemeAndSettings()
    {
        try
        {
            var themeService = Services?.GetService<Wadd.Core.Interfaces.IThemeService>();
            themeService?.SaveTheme();
        }
        catch { }
    }
}
