using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.Content;
using Android.Widget;
using Wadd.Core.Enums;
using Wadd.Core.Models;
using Wadd.Services;

namespace Wadd.Android;

[Service(Permission = "android.permission.BIND_REMOTEVIEWS", Exported = false)]
public class TodayTasksWidgetService : RemoteViewsService
{
    public override IRemoteViewsFactory OnGetViewFactory(Intent? intent)
    {
        return new TodayTasksRemoteViewsFactory(ApplicationContext ?? this);
    }
}

public class TodayTasksRemoteViewsFactory : Java.Lang.Object, RemoteViewsService.IRemoteViewsFactory
{
    private readonly Context _context;
    private List<TodoItem> _todayTasks = new();

    public TodayTasksRemoteViewsFactory(Context context)
    {
        _context = context;
    }

    public void OnCreate()
    {
    }

    public void OnDataSetChanged()
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
            var dbPath = Wadd.Core.Helpers.AppDataHelper.GetWaddFilePath("wadd.db");
            using var conn = new SQLite.SQLiteConnection(dbPath);
            conn.CreateTable<Wadd.Services.Entities.TodoItemEntity>();

            var entities = conn.Table<Wadd.Services.Entities.TodoItemEntity>()
                .Where(x => !x.IsDeleted && !x.IsCompleted)
                .ToList();

            var today = DateTime.Today;

            _todayTasks = entities
                .Select(e => e.ToDomain())
                .Where(t => (t.DueDate.HasValue && t.DueDate.Value.Date <= today) || !t.DueDate.HasValue)
                .OrderBy(t => t.DueDate.HasValue ? t.DueDate.Value : DateTime.MaxValue)
                .ThenBy(t => t.Title)
                .Take(20)
                .ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error fetching tasks for widget: {ex.Message}");
            _todayTasks = new List<TodoItem>();
        }
    }

    public void OnDestroy()
    {
        _todayTasks.Clear();
    }

    public int Count => _todayTasks.Count;

    public RemoteViews? GetViewAt(int position)
    {
        if (position < 0 || position >= _todayTasks.Count) return null;

        var task = _todayTasks[position];
        return TodayTasksWidgetProvider.BuildTaskRemoteItemView(_context, task);
    }

    public RemoteViews? LoadingView => null;

    public int ViewTypeCount => 1;

    public long GetItemId(int position) => position;

    public bool HasStableIds => true;
}
