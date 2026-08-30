using Wadd.Core.Models;

namespace Wadd.Core.Interfaces;

/// <summary>
/// Service interface for managing Life Goals, Milestones, and Reflection Journal entries.
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

    // Journal Entries
    Task<IEnumerable<JournalEntry>> GetJournalEntriesAsync(string? goalId = null, CancellationToken cancellationToken = default);
    Task<JournalEntry> SaveJournalEntryAsync(JournalEntry entry, CancellationToken cancellationToken = default);
    Task<bool> DeleteJournalEntryAsync(string id, CancellationToken cancellationToken = default);

    // Dynamic Calculation Logic
    double CalculateGoalProgress(IEnumerable<GoalMilestone> milestones);
}
