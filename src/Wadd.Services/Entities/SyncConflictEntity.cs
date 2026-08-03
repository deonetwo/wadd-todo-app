using SQLite;
using Wadd.Core.Models;

namespace Wadd.Services.Entities;

[Table("SyncConflict")]
public class SyncConflictEntity
{
    [PrimaryKey]
    public Guid Id { get; set; }

    public string TableName { get; set; } = "TodoItem";

    [Indexed]
    public Guid RecordId { get; set; }

    public string LocalVersionJson { get; set; } = string.Empty;

    public string CloudVersionJson { get; set; } = string.Empty;

    public DateTime LocalUpdatedAt { get; set; }

    public DateTime CloudUpdatedAt { get; set; }

    public string OriginatingDeviceId { get; set; } = string.Empty;

    public string ConflictingFieldsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Indexed]
    public int Status { get; set; } // 0 = Unresolved, 1 = Resolved

    public DateTime? ResolvedAt { get; set; }

    public int? ResolutionType { get; set; } // 0 = KeepLocal, 1 = KeepCloud, 2 = ManualMerge

    public string? ResolvedVersionJson { get; set; }

    public SyncConflict ToDomain()
    {
        return new SyncConflict
        {
            Id = Id,
            TableName = TableName,
            RecordId = RecordId,
            LocalVersionJson = LocalVersionJson,
            CloudVersionJson = CloudVersionJson,
            LocalUpdatedAt = LocalUpdatedAt,
            CloudUpdatedAt = CloudUpdatedAt,
            OriginatingDeviceId = OriginatingDeviceId,
            ConflictingFieldsJson = ConflictingFieldsJson,
            CreatedAt = CreatedAt,
            Status = (ConflictStatus)Status,
            ResolvedAt = ResolvedAt,
            ResolutionType = ResolutionType.HasValue ? (ConflictResolutionType)ResolutionType.Value : null,
            ResolvedVersionJson = ResolvedVersionJson
        };
    }

    public static SyncConflictEntity FromDomain(SyncConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        return new SyncConflictEntity
        {
            Id = conflict.Id == Guid.Empty ? Guid.NewGuid() : conflict.Id,
            TableName = conflict.TableName,
            RecordId = conflict.RecordId,
            LocalVersionJson = conflict.LocalVersionJson,
            CloudVersionJson = conflict.CloudVersionJson,
            LocalUpdatedAt = conflict.LocalUpdatedAt,
            CloudUpdatedAt = conflict.CloudUpdatedAt,
            OriginatingDeviceId = conflict.OriginatingDeviceId,
            ConflictingFieldsJson = conflict.ConflictingFieldsJson,
            CreatedAt = conflict.CreatedAt,
            Status = (int)conflict.Status,
            ResolvedAt = conflict.ResolvedAt,
            ResolutionType = conflict.ResolutionType.HasValue ? (int)conflict.ResolutionType.Value : null,
            ResolvedVersionJson = conflict.ResolvedVersionJson
        };
    }
}
