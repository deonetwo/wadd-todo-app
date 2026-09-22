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

    [Fact]
    public void GoalsViewModel_ClearNewGoalTargetDateCommand_ClearsTargetDate()
    {
        var vm = new Wadd.UI.ViewModels.GoalsViewModel(_goalService);
        Assert.False(vm.HasNewGoalTargetDate);
        Assert.Null(vm.NewGoalTargetDate);

        vm.NewGoalTargetDate = new DateTime(2026, 12, 31);
        Assert.True(vm.HasNewGoalTargetDate);
        Assert.NotNull(vm.NewGoalTargetDate);

        vm.ClearNewGoalTargetDateCommand.Execute(null);
        Assert.False(vm.HasNewGoalTargetDate);
        Assert.Null(vm.NewGoalTargetDate);
    }

    [Fact]
    public void LifeGoalItemViewModel_UpdateMilestonesSummary_UpdatesIsAchievedAndStatusBadgeText()
    {
        var model = new LifeGoal { Title = "Run Marathon", IsAchieved = false };
        var vm = new Wadd.UI.ViewModels.LifeGoalItemViewModel(model);

        var changedProps = new List<string>();
        vm.PropertyChanged += (s, e) => { if (e.PropertyName != null) changedProps.Add(e.PropertyName); };

        // 1 of 2 completed -> In Progress
        vm.UpdateMilestonesSummary(1, 2);
        Assert.False(vm.IsAchieved);
        Assert.Equal("In Progress", vm.StatusBadgeText);
        Assert.Equal("50% Done", vm.StatusText);
        Assert.Contains(nameof(vm.StatusBadgeText), changedProps);

        changedProps.Clear();

        // 2 of 2 completed -> Achieved
        vm.UpdateMilestonesSummary(2, 2);
        Assert.True(vm.IsAchieved);
        Assert.Equal("Achieved", vm.StatusBadgeText);
        Assert.Equal("Achieved", vm.StatusText);
        Assert.Contains(nameof(vm.IsAchieved), changedProps);
        Assert.Contains(nameof(vm.StatusBadgeText), changedProps);

        changedProps.Clear();

        // Uncheck one -> In Progress again
        vm.UpdateMilestonesSummary(1, 2);
        Assert.False(vm.IsAchieved);
        Assert.Equal("In Progress", vm.StatusBadgeText);
        Assert.Equal("50% Done", vm.StatusText);
        Assert.Contains(nameof(vm.IsAchieved), changedProps);
        Assert.Contains(nameof(vm.StatusBadgeText), changedProps);
    }

    [Fact]
    public async Task GoalsViewModel_ToggleMilestone_UpdatesGoalAchievedStatusAndStatusBadgeText()
    {
        var goal = await _goalService.SaveGoalAsync(new LifeGoal { Title = "Launch Project", IsAchieved = false });
        var m1 = await _goalService.SaveMilestoneAsync(new GoalMilestone { GoalId = goal.Id, Title = "Step 1", IsCompleted = false, OrderIndex = 0 });
        var m2 = await _goalService.SaveMilestoneAsync(new GoalMilestone { GoalId = goal.Id, Title = "Step 2", IsCompleted = false, OrderIndex = 1 });

        var vm = new Wadd.UI.ViewModels.GoalsViewModel(_goalService);
        await vm.LoadAllGoalsAsync();

        var goalVm = vm.Goals.First(g => g.Id == goal.Id);
        vm.SelectedGoal = goalVm;

        for (int i = 0; i < 50 && vm.CurrentMilestones.Count < 2; i++)
        {
            await Task.Delay(20);
        }

        Assert.Equal(2, vm.CurrentMilestones.Count);
        Assert.Equal("In Progress", goalVm.StatusBadgeText);
        Assert.False(goalVm.IsAchieved);

        // Check first milestone
        vm.CurrentMilestones[0].IsCompleted = true;
        await vm.ToggleMilestoneCommand.ExecuteAsync(vm.CurrentMilestones[0]);

        Assert.Equal("In Progress", goalVm.StatusBadgeText);
        Assert.False(goalVm.IsAchieved);

        // Check second milestone -> All milestones completed
        vm.CurrentMilestones[1].IsCompleted = true;
        await vm.ToggleMilestoneCommand.ExecuteAsync(vm.CurrentMilestones[1]);

        Assert.Equal("Achieved", goalVm.StatusBadgeText);
        Assert.True(goalVm.IsAchieved);

        // Verify persisted to DB
        var persisted = await _goalService.GetGoalByIdAsync(goal.Id);
        Assert.NotNull(persisted);
        Assert.True(persisted.IsAchieved);

        // Uncheck second milestone
        vm.CurrentMilestones[1].IsCompleted = false;
        await vm.ToggleMilestoneCommand.ExecuteAsync(vm.CurrentMilestones[1]);

        Assert.Equal("In Progress", goalVm.StatusBadgeText);
        Assert.False(goalVm.IsAchieved);

        persisted = await _goalService.GetGoalByIdAsync(goal.Id);
        Assert.NotNull(persisted);
        Assert.False(persisted.IsAchieved);
    }
}
