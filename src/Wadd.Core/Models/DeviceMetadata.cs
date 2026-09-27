namespace Wadd.Core.Models;

public class DeviceMetadata
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;
}
