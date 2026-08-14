using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Wadd.Services;
using Wadd.UI.ViewModels;
using Wadd.UI.Views;

namespace Wadd.UI;

public partial class App : Application
{
    public static IServiceProvider? Services { get; private set; }

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
            mainWindow.Closing += (s, e) => SaveThemeAndSettings();
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += (s, e) => SaveThemeAndSettings();
            desktop.Exit += (s, e) => SaveThemeAndSettings();
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
