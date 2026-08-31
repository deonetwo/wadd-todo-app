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
        }

        collection.AddTransient<MainViewModel>();

        Services = collection.BuildServiceProvider();

        var mainViewModel = Services.GetRequiredService<MainViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
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
                mainWindow.Opened += (s, e) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        mainWindow.Hide();
                    });
                };
            }

            mainWindow.Closing += (s, e) =>
            {
                SaveThemeAndSettings();
                if (!_isExplicitExit && mainViewModel.CloseToTray)
                {
                    e.Cancel = true;
                    mainWindow.Hide();
                }
            };

            mainWindow.PropertyChanged += (s, e) =>
            {
                if (e.Property == Window.WindowStateProperty &&
                    mainWindow.WindowState == WindowState.Minimized &&
                    mainViewModel.MinimizeToTray)
                {
                    mainWindow.Hide();
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
                System.Diagnostics.Trace.WriteLine($"[App] Error loading tray icon: {ex.Message}");
            }

            _trayIcon.Clicked += (s, e) => ShowMainWindow(desktop);

            var menu = new NativeMenu();

            var openItem = new NativeMenuItem("Open Wadd");
            openItem.Click += (s, e) => ShowMainWindow(desktop);
            menu.Add(openItem);

            menu.Add(new NativeMenuItemSeparator());

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
            System.Diagnostics.Trace.WriteLine($"[App] Error setting up system tray: {ex.Message}");
        }
    }

    public void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow is Window window)
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }
            window.Activate();
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
