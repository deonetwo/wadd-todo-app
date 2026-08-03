using Wadd.Core.Models;

namespace Wadd.Core.Interfaces;

public interface IConflictRepository
{
    Task AddConflictAsync(SyncConflict conflict, CancellationToken cancellationToken = default);
    Task<IEnumerable<SyncConflict>> GetUnresolvedConflictsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<SyncConflict>> GetConflictHistoryAsync(CancellationToken cancellationToken = default);
    Task ResolveConflictAsync(Guid conflictId, ConflictResolutionType resolutionType, string resolvedVersionJson, CancellationToken cancellationToken = default);
}
