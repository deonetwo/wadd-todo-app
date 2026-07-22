using Microsoft.Extensions.DependencyInjection;
using Wadd.Core.Interfaces;

namespace Wadd.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWaddServices(this IServiceCollection services)
    {
        // Core & Infrastructure Services
        services.AddSingleton<ITodoService, SQLiteTodoService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ISyncService, SyncService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<ITracingService, TracingService>();

        return services;
    }
}
