namespace Wadd.Core.Models;

public enum SyncOperation
{
    Insert,
    Update,
    Delete
}

public class SyncLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TableName { get; set; } = "TodoItem";
    public Guid RecordId { get; set; }
    public SyncOperation Operation { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string DeviceId { get; set; } = string.Empty;
    public bool Synced { get; set; }
    public long Revision { get; set; }
}
