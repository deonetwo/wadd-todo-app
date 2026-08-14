using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;
using System.Runtime.Versioning;
using Wadd.Core.Models;
using Wadd.Services;

namespace Wadd.Android;

[SupportedOSPlatform("android29.0")]
[BroadcastReceiver(Label = "Wadd Tasks Widget", Exported = true)]
[IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate, ActionToggleTask, ActionRefreshWidget, ActionOpenApp })]
[MetaData("android.appwidget.provider", Resource = "@xml/widget_today_tasks_info")]
public class TodayTasksWidgetProvider : AppWidgetProvider
{
    public const string ActionToggleTask = "com.wadd.todoapp.ACTION_TOGGLE_TASK";
    public const string ActionRefreshWidget = "com.wadd.todoapp.ACTION_REFRESH_WIDGET";
    public const string ActionOpenApp = "com.wadd.todoapp.ACTION_OPEN_APP";
    public const string ExtraTaskId = "extra_task_id";

    private static readonly object SyncLock = new();
    private static readonly Dictionary<Guid, DateTime> LastClickTimes = new();
    private static Guid _animatingTaskId = Guid.Empty;
    private static DateTime _animatingUntil = DateTime.MinValue;

    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context == null || appWidgetManager == null || appWidgetIds == null) return;

        foreach (var appWidgetId in appWidgetIds)
        {
            UpdateWidget(context, appWidgetManager, appWidgetId);
        }
        base.OnUpdate(context, appWidgetManager, appWidgetIds);
    }

    public static void UpdateWidget(Context context, AppWidgetManager appWidgetManager, int appWidgetId)
    {
        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_today_tasks);

        var todayStr = DateTime.Now.ToString("dddd, MMM d");
        views.SetTextViewText(Resource.Id.widget_subtitle, todayStr);
        views.SetEmptyView(Resource.Id.widget_task_list, Resource.Id.widget_empty_view);

        // Refresh Button action - Package explicitly scoped to Wadd
        var refreshIntent = new Intent(context, typeof(TodayTasksWidgetProvider));
        refreshIntent.SetAction(ActionRefreshWidget);
        refreshIntent.SetPackage(context.PackageName);
        refreshIntent.PutExtra(AppWidgetManager.ExtraAppwidgetId, appWidgetId);
        var pendingRefreshFlags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            pendingRefreshFlags |= PendingIntentFlags.Immutable;
        }
        var pendingRefreshIntent = PendingIntent.GetBroadcast(
            context,
            appWidgetId,
            refreshIntent,
            pendingRefreshFlags);
        views.SetOnClickPendingIntent(Resource.Id.widget_btn_refresh, pendingRefreshIntent);

        // Task Item fill-in intent template - Package explicitly scoped to Wadd
        var fillInTemplateIntent = new Intent(context, typeof(TodayTasksWidgetProvider));
        fillInTemplateIntent.SetPackage(context.PackageName);
        fillInTemplateIntent.PutExtra(AppWidgetManager.ExtraAppwidgetId, appWidgetId);
        var templateFlags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            templateFlags |= PendingIntentFlags.Mutable;
        }

        var pendingTemplateIntent = PendingIntent.GetBroadcast(
            context,
            appWidgetId,
            fillInTemplateIntent,
            templateFlags);
        views.SetPendingIntentTemplate(Resource.Id.widget_task_list, pendingTemplateIntent);

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var tasks = GetTodayTasks();
            var builder = new RemoteViews.RemoteCollectionItems.Builder();
            builder.SetHasStableIds(true);
            builder.SetViewTypeCount(1);

            for (int i = 0; i < tasks.Count; i++)
            {
                var task = tasks[i];
                var itemViews = BuildTaskRemoteItemView(context, task);
                builder.AddItem(i, itemViews);
            }

            views.SetRemoteAdapter(Resource.Id.widget_task_list, builder.Build());
        }
        else
        {
            var intent = new Intent(context, typeof(TodayTasksWidgetService));
            intent.SetPackage(context.PackageName);
            intent.PutExtra(AppWidgetManager.ExtraAppwidgetId, appWidgetId);
            intent.SetData(global::Android.Net.Uri.Parse(intent.ToUri(IntentUriType.Scheme)));
            views.SetRemoteAdapter(Resource.Id.widget_task_list, intent);
            appWidgetManager.NotifyAppWidgetViewDataChanged(appWidgetId, Resource.Id.widget_task_list);
        }

        appWidgetManager.UpdateAppWidget(appWidgetId, views);
    }

    public static RemoteViews BuildTaskRemoteItemView(Context context, TodoItem task)
    {
        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_task_item);
        
        var title = task.Title ?? string.Empty;
        if (title.Length > 60) title = title.Substring(0, 57) + "…";
        views.SetTextViewText(Resource.Id.widget_item_title, title);

        if (task.IsCompleted)
        {
            views.SetImageViewResource(Resource.Id.widget_item_checkbox, Resource.Drawable.widget_checkbox_checked);
            views.SetTextViewText(Resource.Id.widget_item_subtitle, "✓ Completed");
        }
        else
        {
            views.SetImageViewResource(Resource.Id.widget_item_checkbox, Resource.Drawable.widget_checkbox_unchecked);

            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(task.Category))
            {
                var cat = task.Category.Trim();
                if (cat.Length > 20) cat = cat.Substring(0, 18) + "…";
                details.Add(cat);
            }
            if (task.DueDate.HasValue)
            {
                if (task.DueDate.Value.Date < DateTime.Today)
                {
                    details.Add("Overdue");
                }
                else if (task.DueDate.Value.TimeOfDay != TimeSpan.Zero)
                {
                    details.Add(task.DueDate.Value.ToString("h:mm tt"));
                }
                else
                {
                    details.Add("Today");
                }
            }
            else
            {
                details.Add("No due date");
            }

            views.SetTextViewText(Resource.Id.widget_item_subtitle, string.Join(" • ", details));
        }

        // Checkbox click -> Scoped explicitly to Wadd package
        var toggleFillInIntent = new Intent();
        toggleFillInIntent.SetAction(ActionToggleTask);
        toggleFillInIntent.SetPackage(context.PackageName);
        toggleFillInIntent.PutExtra(ExtraTaskId, task.Id.ToString());
        views.SetOnClickFillInIntent(Resource.Id.widget_item_checkbox, toggleFillInIntent);

        // Task Item row body click -> Scoped explicitly to Wadd package
        var openFillInIntent = new Intent();
        openFillInIntent.SetAction(ActionOpenApp);
        openFillInIntent.SetPackage(context.PackageName);
        openFillInIntent.PutExtra(ExtraTaskId, task.Id.ToString());
        views.SetOnClickFillInIntent(Resource.Id.widget_item_container, openFillInIntent);

        return views;
    }

    public static List<TodoItem> GetTodayTasks()
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
            var dbPath = Wadd.Core.Helpers.AppDataHelper.GetWaddFilePath("wadd.db");
            using var conn = new SQLite.SQLiteConnection(dbPath);
            conn.CreateTable<Wadd.Services.Entities.TodoItemEntity>();

            List<Wadd.Services.Entities.TodoItemEntity> rawEntities;
            Guid currentAnimatingId;
            DateTime currentAnimatingUntil;

            lock (SyncLock)
            {
                currentAnimatingId = _animatingTaskId;
                currentAnimatingUntil = _animatingUntil;

                rawEntities = conn.Table<Wadd.Services.Entities.TodoItemEntity>()
                    .Where(x => !x.IsDeleted)
                    .ToList();
            }

            var now = DateTime.UtcNow;
            var today = DateTime.Today;

            return rawEntities
                .Select(e => e.ToDomain())
                .Where(t => !t.IsCompleted || (t.Id == currentAnimatingId && now < currentAnimatingUntil))
                .Where(t => (t.DueDate.HasValue && t.DueDate.Value.Date <= today) || !t.DueDate.HasValue)
                .OrderBy(t => t.IsCompleted ? 1 : 0)
                .ThenBy(t => t.DueDate.HasValue ? t.DueDate.Value : DateTime.MaxValue)
                .ThenBy(t => t.Title)
                .Take(20)
                .ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error fetching tasks for widget: {ex.Message}");
            return new List<TodoItem>();
        }
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        base.OnReceive(context, intent);

        if (context == null || intent == null) return;

        // Origin package security check for custom widget actions
        if (intent.Action == ActionToggleTask || intent.Action == ActionRefreshWidget || intent.Action == ActionOpenApp)
        {
            var targetPkg = intent.Package ?? intent.Component?.PackageName;
            if (!string.IsNullOrEmpty(targetPkg) && targetPkg != context.PackageName)
            {
                System.Diagnostics.Trace.WriteLine($"Security Warning: Ignored untrusted broadcast intent from package '{targetPkg}'");
                return;
            }
        }

        if (intent.Action == ActionToggleTask)
        {
            var taskIdStr = intent.GetStringExtra(ExtraTaskId);
            if (Guid.TryParse(taskIdStr, out var taskId))
            {
                lock (SyncLock)
                {
                    if (LastClickTimes.TryGetValue(taskId, out var lastClick) && (DateTime.UtcNow - lastClick).TotalMilliseconds < 450)
                    {
                        return;
                    }
                    LastClickTimes[taskId] = DateTime.UtcNow;
                }

                try
                {
                    SQLitePCL.Batteries_V2.Init();
                    var dbPath = Wadd.Core.Helpers.AppDataHelper.GetWaddFilePath("wadd.db");
                    using var conn = new SQLite.SQLiteConnection(dbPath);

                    Wadd.Services.Entities.TodoItemEntity? entity;
                    lock (SyncLock)
                    {
                        entity = conn.Table<Wadd.Services.Entities.TodoItemEntity>().FirstOrDefault(x => x.Id == taskId);
                        if (entity != null)
                        {
                            var willBeCompleted = !entity.IsCompleted;
                            entity.IsCompleted = willBeCompleted;
                            entity.CompletedAt = willBeCompleted ? DateTime.UtcNow : null;
                            entity.UpdatedAt = DateTime.UtcNow;
                            conn.Update(entity);
                        }
                    }

                    if (entity != null)
                    {
                        Wadd.Core.Helpers.WaddDatabaseNotifier.NotifyDataChanged();

                        if (entity.IsCompleted)
                        {
                            lock (SyncLock)
                            {
                                _animatingTaskId = entity.Id;
                                _animatingUntil = DateTime.UtcNow.AddMilliseconds(750);
                            }

                            // Phase 1: Render immediate green checkmark visual feedback [✓]
                            TriggerRefresh(context);

                            // Phase 2: Automatic transition out after 700ms animation window
                            var pendingResult = GoAsync();
                            System.Threading.Tasks.Task.Run(async () =>
                            {
                                try
                                {
                                    await System.Threading.Tasks.Task.Delay(700);
                                    lock (SyncLock)
                                    {
                                        if (_animatingTaskId == taskId)
                                        {
                                            _animatingTaskId = Guid.Empty;
                                            _animatingUntil = DateTime.MinValue;
                                        }
                                    }
                                    TriggerRefresh(context);
                                }
                                finally
                                {
                                    pendingResult?.Finish();
                                }
                            });
                            return;
                        }
                    }
                    TriggerRefresh(context);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"Error toggling task from widget: {ex.Message}");
                }
            }
        }
        else if (intent.Action == ActionOpenApp)
        {
            var mainIntent = new Intent(context, typeof(MainActivity));
            mainIntent.SetPackage(context.PackageName);
            mainIntent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            context.StartActivity(mainIntent);
        }
        else if (intent.Action == ActionRefreshWidget)
        {
            TriggerRefresh(context);
        }
    }

    public static void TriggerRefresh(Context context)
    {
        var appWidgetManager = AppWidgetManager.GetInstance(context);
        if (appWidgetManager == null) return;

        var componentName = new ComponentName(context, Java.Lang.Class.FromType(typeof(TodayTasksWidgetProvider)));
        var appWidgetIds = appWidgetManager.GetAppWidgetIds(componentName);

        if (appWidgetIds != null && appWidgetIds.Length > 0)
        {
            foreach (var id in appWidgetIds)
            {
                UpdateWidget(context, appWidgetManager, id);
            }
        }
    }
}
