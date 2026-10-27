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
    string GoogleClientSecret { get; set; }
    string FirebaseApiKey { get; set; }
    string FirebaseProjectId { get; set; }

    bool HasChangesApplied { get; }
    bool IsSubscriptionExpired { get; }
    string? SubscriptionPlan { get; }
    int? SubscriptionDaysRemaining { get; }
    string? SubscriptionUpgradeUrl { get; }

    int UnresolvedConflictCount { get; }
    event EventHandler? ConflictCountChanged;
    event EventHandler? AuthStateChanged;
    Func<string, string, Task<bool>>? ConfirmAccountSwitchAsync { get; set; }

    Task<bool> SignInAsync(CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);
    Task<bool> SyncAsync(CancellationToken cancellationToken = default);
    Task<bool> SyncAsync(bool forceRefreshSubscription, CancellationToken cancellationToken = default);
    Task RefreshConflictCountAsync(CancellationToken cancellationToken = default);
    void OpenUpgradePage();
}
