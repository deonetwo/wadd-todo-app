using Wadd.Core.Interfaces;

namespace Wadd.Services;

public class SyncService : GoogleDriveSyncService
{
    public SyncService(ITodoService todoService, HttpClient? httpClient = null, string? webAppUrl = null)
        : base(todoService, httpClient, webAppUrl)
    {
    }

    public SyncService()
        : base(new SQLiteTodoService(), new HttpClient(), null)
    {
    }
}
