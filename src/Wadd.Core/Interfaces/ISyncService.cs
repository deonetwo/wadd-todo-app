namespace Wadd.Core.Interfaces;

/// <summary>
/// Service interface for synchronizing todo data with remote services.
/// </summary>
public interface ISyncService
{
    Task<bool> SyncAsync(CancellationToken cancellationToken = default);
}
