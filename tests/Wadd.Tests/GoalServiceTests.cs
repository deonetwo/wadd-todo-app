using Wadd.Core.Models;
using Wadd.Services;
using Xunit;

namespace Wadd.Tests;

public class GoalServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SQLiteGoalService _goalService;

    public GoalServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"wadd_goal_test_{Guid.NewGuid():N}.db");
        _goalService = new SQLiteGoalService(_testDbPath);
    }

    public void Dispose()
    {
        if (File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { }
        }
    }

    [Fact]
    public async Task SaveGoalAsync_CreatesAndRetrievesGoal()
    {
        var goal = new LifeGoal
        {
            Title = "Master Avalonia UI",
            Description = "Build impressive cross-platform desktop & mobile apps.",
            Category = "Career",
            TargetDate = DateTime.Today.AddMonths(3)
        };

        var saved = await _goalService.SaveGoalAsync(goal);
        Assert.NotNull(saved.Id);
        Assert.Equal("Master Avalonia UI", saved.Title);

        var retrieved = await _goalService.GetGoalByIdAsync(saved.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Career", retrieved.Category);
        Assert.False(retrieved.IsDeleted);
    }

    [Fact]
    public async Task CalculateGoalProgress_CalculatesCorrectPercentage()
    {
        var milestones = new List<GoalMilestone>
        {
            new GoalMilestone { Title = "Step 1", IsCompleted = true },
            new GoalMilestone { Title = "Step 2", IsCompleted = true },
            new GoalMilestone { Title = "Step 3", IsCompleted = false },
            new GoalMilestone { Title = "Step 4", IsCompleted = false }
        };

        double progress = _goalService.CalculateGoalProgress(milestones);
        Assert.Equal(50.0, progress);
    }

    [Fact]
    public async Task ToggleMilestoneAsync_UpdatesCompletionState()
    {
        var goal = await _goalService.SaveGoalAsync(new LifeGoal { Title = "Health Goal" });
        var milestone = await _goalService.SaveMilestoneAsync(new GoalMilestone
        {
            GoalId = goal.Id,
            Title = "Drink 2L Water Daily",
            IsCompleted = false
        });

        Assert.False(milestone.IsCompleted);

        var success = await _goalService.ToggleMilestoneAsync(milestone.Id);
        Assert.True(success);

        var milestones = (await _goalService.GetMilestonesForGoalAsync(goal.Id)).ToList();
        Assert.Single(milestones);
        Assert.True(milestones[0].IsCompleted);
    }

    [Fact]
    public async Task SaveJournalEntry_CreatesAndFetchesByGoalId()
    {
        var goal = await _goalService.SaveGoalAsync(new LifeGoal { Title = "Financial Freedom" });
        var entry = new JournalEntry
        {
            GoalId = goal.Id,
            Title = "Monthly Savings Review",
            Content = "Saved 20% of income this month."
        };

        var saved = await _goalService.SaveJournalEntryAsync(entry);
        Assert.NotNull(saved.Id);

        var entries = (await _goalService.GetJournalEntriesAsync(goal.Id)).ToList();
        Assert.Single(entries);
        Assert.Equal("Monthly Savings Review", entries[0].Title);
    }

    [Fact]
    public async Task DeleteGoalAsync_PerformsSoftDeleteOnGoalAndMilestones()
    {
        var goal = await _goalService.SaveGoalAsync(new LifeGoal { Title = "Temporary Goal" });
        await _goalService.SaveMilestoneAsync(new GoalMilestone { GoalId = goal.Id, Title = "Milestone 1" });

        var deleted = await _goalService.DeleteGoalAsync(goal.Id);
        Assert.True(deleted);

        var fetched = await _goalService.GetGoalByIdAsync(goal.Id);
        Assert.Null(fetched);

        var activeGoals = (await _goalService.GetGoalsAsync()).ToList();
        Assert.DoesNotContain(activeGoals, g => g.Id == goal.Id);

        var activeMilestones = (await _goalService.GetMilestonesForGoalAsync(goal.Id)).ToList();
        Assert.Empty(activeMilestones);
    }
}
