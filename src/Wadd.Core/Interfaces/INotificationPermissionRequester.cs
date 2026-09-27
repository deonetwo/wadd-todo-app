using System.Threading.Tasks;

namespace Wadd.Core.Interfaces;

/// <summary>
/// Abstraction for requesting runtime notification permissions (e.g. Android 13+ POST_NOTIFICATIONS).
/// </summary>
public interface INotificationPermissionRequester
{
    Task<bool> RequestNotificationPermissionAsync();
}
