namespace Wadd.Core.Interfaces;

/// <summary>
/// Service interface for synchronizing todo data with remote services.
/// </summary>
public interface ISyncService
{
    bool IsSignedIn { get; }
    string? UserEmail { get; }
    string? UserName { get; }
    string GoogleClientId { get; set; }

    Task<bool> SignInAsync(CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);
    Task<bool> SyncAsync(CancellationToken cancellationToken = default);
}
