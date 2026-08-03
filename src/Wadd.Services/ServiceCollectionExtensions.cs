using Microsoft.Extensions.DependencyInjection;
using Wadd.Core.Interfaces;

namespace Wadd.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWaddServices(this IServiceCollection services)
    {
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<ConflictResolutionEngine>();
        services.AddSingleton<ITodoService, SQLiteTodoService>();
        services.AddSingleton<ISyncLogRepository>(sp => ((SQLiteTodoService)sp.GetRequiredService<ITodoService>()).SyncLogRepository);
        services.AddSingleton<IConflictRepository>(sp => new SQLiteConflictRepository(((SQLiteTodoService)sp.GetRequiredService<ITodoService>()).DatabaseConnection));
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ISyncService, GoogleDriveSyncService>();
        services.AddSingleton<IExportService, ExcelExportService>();
        services.AddSingleton<ITracingService, TracingService>();

        return services;
    }
}
