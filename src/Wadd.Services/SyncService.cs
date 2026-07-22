using Wadd.Core.Interfaces;

namespace Wadd.Services;

public class SyncService : ISyncService
{
    public Task<bool> SyncAsync(CancellationToken cancellationToken = default)
    {
        // Placeholder for remote synchronization engine logic
        return Task.FromResult(true);
    }
}
