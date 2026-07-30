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
        collection.AddTransient<MainViewModel>();

        Services = collection.BuildServiceProvider();

        var mainViewModel = Services.GetRequiredService<MainViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView
            {
                DataContext = mainViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
