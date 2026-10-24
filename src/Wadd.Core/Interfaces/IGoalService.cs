using Wadd.Core.Models;

namespace Wadd.Core.Interfaces;

/// <summary>
/// Service interface for managing Life Goals and Milestones.
/// </summary>
public interface IGoalService
{
    // Life Goals
    Task<IEnumerable<LifeGoal>> GetGoalsAsync(CancellationToken cancellationToken = default);
    Task<LifeGoal?> GetGoalByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<LifeGoal> SaveGoalAsync(LifeGoal goal, CancellationToken cancellationToken = default);
    Task<bool> DeleteGoalAsync(string id, CancellationToken cancellationToken = default);

    // Goal Milestones
    Task<IEnumerable<GoalMilestone>> GetMilestonesForGoalAsync(string goalId, CancellationToken cancellationToken = default);
    Task<GoalMilestone> SaveMilestoneAsync(GoalMilestone milestone, CancellationToken cancellationToken = default);
    Task<bool> ToggleMilestoneAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> DeleteMilestoneAsync(string id, CancellationToken cancellationToken = default);
    Task ReorderMilestonesAsync(IEnumerable<GoalMilestone> milestones, CancellationToken cancellationToken = default);

    // Dynamic Calculation Logic
    double CalculateGoalProgress(IEnumerable<GoalMilestone> milestones);

    // Sync Support (Raw Queries & Direct Batch Upserts)
    Task<IEnumerable<LifeGoal>> GetAllGoalsRawAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<GoalMilestone>> GetAllMilestonesRawAsync(CancellationToken cancellationToken = default);
    Task BatchDirectUpsertGoalsAsync(IEnumerable<LifeGoal> goals, CancellationToken cancellationToken = default);
    Task BatchDirectUpsertMilestonesAsync(IEnumerable<GoalMilestone> milestones, CancellationToken cancellationToken = default);
    Task ClearAllLocalDataAsync(CancellationToken cancellationToken = default);
}
