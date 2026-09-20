using Microsoft.Extensions.DependencyInjection;
using Wadd.Core.Interfaces;

namespace Wadd.Services;

public static class ServiceCollectionExtensions
{
    private static IServiceProvider? _sharedServiceProvider;
    private static readonly object _providerLock = new();

    public static IServiceProvider GetOrCreateServiceProvider()
    {
        if (_sharedServiceProvider != null) return _sharedServiceProvider;
        lock (_providerLock)
        {
            if (_sharedServiceProvider != null) return _sharedServiceProvider;
            var services = new ServiceCollection();
            services.AddWaddServices();
            if (OperatingSystem.IsAndroid())
            {
                var androidAuthType = Type.GetType("Wadd.Android.AndroidGoogleAuthService, Wadd.Android")
                                      ?? System.Reflection.Assembly.GetEntryAssembly()?.GetType("Wadd.Android.AndroidGoogleAuthService");
                if (androidAuthType != null)
                {
                    services.AddSingleton(typeof(INativeGoogleAuthService), androidAuthType);
                }

                var androidNotifType = Type.GetType("Wadd.Android.AndroidNotificationService, Wadd.Android")
                                      ?? System.Reflection.Assembly.GetEntryAssembly()?.GetType("Wadd.Android.AndroidNotificationService");
                if (androidNotifType != null)
                {
                    services.AddSingleton(typeof(INotificationService), androidNotifType);
                }

                var androidAudioType = Type.GetType("Wadd.Android.AndroidAudioService, Wadd.Android")
                                      ?? System.Reflection.Assembly.GetEntryAssembly()?.GetType("Wadd.Android.AndroidAudioService");
                if (androidAudioType != null)
                {
                    services.AddSingleton(typeof(IAudioService), androidAudioType);
                }
            }
            _sharedServiceProvider = services.BuildServiceProvider();
            return _sharedServiceProvider;
        }
    }

    public static void SetSharedServiceProvider(IServiceProvider provider)
    {
        lock (_providerLock)
        {
            _sharedServiceProvider = provider;
        }
    }

    public static IServiceCollection AddWaddServices(this IServiceCollection services)
    {
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<ConflictResolutionEngine>();
        services.AddSingleton<ITodoService, SQLiteTodoService>();
        services.AddSingleton<ISyncLogRepository>(sp => ((SQLiteTodoService)sp.GetRequiredService<ITodoService>()).SyncLogRepository);
        services.AddSingleton<IConflictRepository>(sp => new SQLiteConflictRepository(((SQLiteTodoService)sp.GetRequiredService<ITodoService>()).DatabaseConnection));
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<INativeGoogleAuthService, DesktopNativeGoogleAuthService>();
        services.AddSingleton<ITracingService, TracingService>();
        services.AddSingleton<IGoalService>(sp => new SQLiteGoalService(((SQLiteTodoService)sp.GetRequiredService<ITodoService>()).DatabaseConnection));
        services.AddSingleton<ISyncService, GoogleDriveSyncService>();
        services.AddSingleton<IExportService, ExcelExportService>();
        services.AddSingleton<IStartupService, WindowsStartupService>();
        services.AddSingleton<IAiGoalService, AiGoalService>();
        services.AddSingleton<IAudioService, AudioService>();
        services.AddSingleton<INotificationService, WindowsNotificationService>();

        return services;
    }
}
