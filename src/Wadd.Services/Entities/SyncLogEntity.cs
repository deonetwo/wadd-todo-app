using SQLite;
using Wadd.Core.Models;

namespace Wadd.Services.Entities;

[Table("SyncLog")]
public class SyncLogEntity
{
    [PrimaryKey]
    public Guid Id { get; set; }

    public string TableName { get; set; } = "TodoItem";

    [Indexed]
    public Guid RecordId { get; set; }

    public int Operation { get; set; } // 0 = Insert, 1 = Update, 2 = Delete

    public string PayloadJson { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string DeviceId { get; set; } = string.Empty;

    [Indexed]
    public bool Synced { get; set; }

    [Indexed]
    public long Revision { get; set; }

    public SyncLog ToDomain()
    {
        return new SyncLog
        {
            Id = Id,
            TableName = TableName,
            RecordId = RecordId,
            Operation = (SyncOperation)Operation,
            PayloadJson = PayloadJson,
            Timestamp = Timestamp,
            DeviceId = DeviceId,
            Synced = Synced,
            Revision = Revision
        };
    }

    public static SyncLogEntity FromDomain(SyncLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        return new SyncLogEntity
        {
            Id = log.Id == Guid.Empty ? Guid.NewGuid() : log.Id,
            TableName = log.TableName,
            RecordId = log.RecordId,
            Operation = (int)log.Operation,
            PayloadJson = log.PayloadJson,
            Timestamp = log.Timestamp,
            DeviceId = log.DeviceId,
            Synced = log.Synced,
            Revision = log.Revision
        };
    }
}
