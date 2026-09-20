using System;
using System.Threading;
using Android.Appwidget;
using Android.Content;
using Android.Runtime;
using AndroidX.Work;
using Microsoft.Extensions.DependencyInjection;
using Wadd.Core.Interfaces;
using Wadd.Core.Logging;
using Wadd.Services;

namespace Wadd.Android;

[Register("com.wadd.todoapp.SyncWorker")]
public class SyncWorker : Worker
{
    public const string WorkName = "WaddPeriodicSync";

    public SyncWorker(Context context, WorkerParameters workerParams)
        : base(context, workerParams)
    {
    }

    public override Result DoWork()
    {
        AppLogger.LogInfo("WorkManager", "Starting Android background periodic sync...");
        try
        {
            var services = ServiceCollectionExtensions.GetOrCreateServiceProvider();
            var syncService = services.GetService<ISyncService>();

            if (syncService == null || !syncService.IsSignedIn)
            {
                AppLogger.LogInfo("WorkManager", "User is not signed in; skipping background sync.");
                return Result.InvokeSuccess();
            }

            // Headless sync execution
            var syncTask = syncService.SyncAsync(CancellationToken.None);
            var success = syncTask.GetAwaiter().GetResult();

            if (success)
            {
                AppLogger.LogInfo("WorkManager", "Background periodic sync completed successfully.");

                // Notify today tasks widget so home-screen reflects synced data
                try
                {
                    var appWidgetManager = AppWidgetManager.GetInstance(ApplicationContext);
                    if (appWidgetManager != null)
                    {
                        var componentName = new ComponentName(ApplicationContext, Java.Lang.Class.FromType(typeof(TodayTasksWidgetProvider)));
                        var appWidgetIds = appWidgetManager.GetAppWidgetIds(componentName);
                        if (appWidgetIds != null && appWidgetIds.Length > 0)
                        {
                            appWidgetManager.NotifyAppWidgetViewDataChanged(appWidgetIds, Resource.Id.widget_task_list);
                            TodayTasksWidgetProvider.TriggerRefresh(ApplicationContext);
                        }
                    }
                }
                catch (Exception widgetEx)
                {
                    AppLogger.LogWarning("WorkManager", $"Failed to notify widget after background sync: {widgetEx.Message}");
                }

                // Reschedule alarms if needed
                try
                {
                    TaskAlarmScheduler.RescheduleAll(ApplicationContext);
                }
                catch (Exception alarmEx)
                {
                    AppLogger.LogWarning("WorkManager", $"Failed to reschedule alarms after background sync: {alarmEx.Message}");
                }

                return Result.InvokeSuccess();
            }

            AppLogger.LogWarning("WorkManager", "Background sync reported unsuccessful run. Requesting retry.");
            return Result.InvokeRetry();
        }
        catch (Exception ex)
        {
            AppLogger.LogError("WorkManager", $"Background sync failed with exception: {ex.Message}", ex);
            return Result.InvokeRetry();
        }
    }
}
