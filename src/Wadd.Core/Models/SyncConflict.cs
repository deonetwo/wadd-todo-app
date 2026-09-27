namespace Wadd.Core.Models;

public enum ConflictStatus
{
    Unresolved,
    Resolved
}

public enum ConflictResolutionType
{
    KeepLocal,
    KeepCloud,
    ManualMerge
}

public class SyncConflict
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TableName { get; set; } = "TodoItem";
    public Guid RecordId { get; set; }
    public string LocalVersionJson { get; set; } = string.Empty;
    public string CloudVersionJson { get; set; } = string.Empty;
    public DateTime LocalUpdatedAt { get; set; }
    public DateTime CloudUpdatedAt { get; set; }
    public string OriginatingDeviceId { get; set; } = string.Empty;
    public string ConflictingFieldsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ConflictStatus Status { get; set; } = ConflictStatus.Unresolved;

    // History fields
    public DateTime? ResolvedAt { get; set; }
    public ConflictResolutionType? ResolutionType { get; set; }
    public string? ResolvedVersionJson { get; set; }
}
