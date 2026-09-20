using System.Threading.Tasks;
using Wadd.Core.Interfaces;

namespace Wadd.Services;

/// <summary>
/// Fallback no-op notification service for platforms without native notification integration.
/// </summary>
public class NoOpNotificationService : INotificationService
{
    public bool IsSupported => false;
    public Task<bool> RequestPermissionAsync() => Task.FromResult(false);
    public Task ShowNotificationAsync(string title, string message, string? tag = null) => Task.CompletedTask;
    public Task CancelNotificationAsync(string tag) => Task.CompletedTask;
}
