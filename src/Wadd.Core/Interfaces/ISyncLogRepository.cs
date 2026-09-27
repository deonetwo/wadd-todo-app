using Wadd.Core.Models;

namespace Wadd.Core.Interfaces;

public interface ISyncLogRepository
{
    Task AddLogAsync(SyncLog log, CancellationToken cancellationToken = default);
    Task<IEnumerable<SyncLog>> GetPendingLogsAsync(CancellationToken cancellationToken = default);
    Task MarkLogsAsSyncedAsync(IEnumerable<Guid> logIds, CancellationToken cancellationToken = default);
    Task<long> GetHighestRevisionAsync(CancellationToken cancellationToken = default);
}
