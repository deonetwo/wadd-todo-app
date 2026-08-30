using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;

namespace Wadd.UI.ViewModels;

public partial class CategoryFilterItemViewModel : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;

    public CategoryFilterItemViewModel(string name, bool isSelected)
    {
        Name = name;
        _isSelected = isSelected;
    }
}

public partial class GoalsViewModel : ViewModelBase
{
    private readonly IGoalService _goalService;

    public ObservableCollection<LifeGoalItemViewModel> Goals { get; } = new();
    public ObservableCollection<LifeGoalItemViewModel> FilteredGoals { get; } = new();
    public ObservableCollection<GoalMilestoneItemViewModel> CurrentMilestones { get; } = new();
    public ObservableCollection<JournalEntryItemViewModel> CurrentJournalEntries { get; } = new();
    public ObservableCollection<JournalEntryItemViewModel> AllJournalEntries { get; } = new();
    public ObservableCollection<CategoryFilterItemViewModel> Categories { get; } = new();



    public bool HasCategories => Categories.Count > 1;

    [ObservableProperty]
    private LifeGoalItemViewModel? _selectedGoal;

    partial void OnSelectedGoalChanged(LifeGoalItemViewModel? value)
    {
        foreach (var g in Goals)
        {
            g.IsSelected = (value != null && g.Id == value.Id);
        }
        _ = LoadMilestonesAndJournalForSelectedGoalAsync();
        OnPropertyChanged(nameof(HasSelectedGoal));
    }

    public bool HasSelectedGoal => SelectedGoal != null;

    [ObservableProperty]
    private string _selectedCategoryFilter = "All";

    partial void OnSelectedCategoryFilterChanged(string value)
    {
        ApplyCategoryFilter();
        SyncCategoryFilterSelection();
    }

    [ObservableProperty]
    private bool _isCreatingGoal;

    [ObservableProperty]
    private bool _isCreatingJournal;

    [ObservableProperty]
    private bool _isCompact;

    [ObservableProperty]
    private bool _isJournalCollapsibleExpanded = true;

    [ObservableProperty]
    private bool _isJournalBottomSheetOpen;

    [ObservableProperty]
    private bool _isMobileDetailViewOpen;

    [ObservableProperty]
    private int _mobileSelectedTab; // 0 = Overview, 1 = Checklist, 2 = Journal

    // New Goal Form Properties
    [ObservableProperty]
    private string _newGoalTitle = string.Empty;

    [ObservableProperty]
    private string _newGoalDescription = string.Empty;

    [ObservableProperty]
    private string _newGoalCategory = "Personal";

    [ObservableProperty]
    private DateTime? _newGoalTargetDate;

    // New Milestone Form Property
    [ObservableProperty]
    private string _newMilestoneTitle = string.Empty;

    // New Journal Form Properties
    [ObservableProperty]
    private string _newJournalTitle = string.Empty;

    [ObservableProperty]
    private string _newJournalContent = string.Empty;



    public GoalsViewModel(IGoalService goalService)
    {
        _goalService = goalService ?? throw new ArgumentNullException(nameof(goalService));
        _ = InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        await LoadAllGoalsAsync();
    }

    public async Task LoadAllGoalsAsync()
    {
        Goals.Clear();
        var rawGoals = await _goalService.GetGoalsAsync();
        foreach (var g in rawGoals.OrderByDescending(x => x.UpdatedAt))
        {
            var milestones = (await _goalService.GetMilestonesForGoalAsync(g.Id)).ToList();
            int completed = milestones.Count(m => m.IsCompleted);
            int total = milestones.Count;

            var vm = new LifeGoalItemViewModel(g);
            vm.UpdateMilestonesSummary(completed, total);
            Goals.Add(vm);
        }

        UpdateCategories();
        ApplyCategoryFilter();

        if (SelectedGoal == null)
        {
            SelectedGoal = FilteredGoals.FirstOrDefault();
        }
        else
        {
            foreach (var g in Goals)
            {
                g.IsSelected = g.Id == SelectedGoal.Id;
            }
        }

        await LoadAllJournalEntriesAsync();
    }

    private void ApplyCategoryFilter()
    {
        FilteredGoals.Clear();
        var query = Goals.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SelectedCategoryFilter) && !SelectedCategoryFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(g => g.Category.Equals(SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var g in query)
        {
            FilteredGoals.Add(g);
        }

        if (SelectedGoal != null && !FilteredGoals.Contains(SelectedGoal))
        {
            SelectedGoal = FilteredGoals.FirstOrDefault();
        }
    }

    private void UpdateCategories()
    {
        var activeCategories = Goals
            .Select(g => g.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c) && !c.Equals("Uncategorized", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();

        Categories.Clear();
        bool isAllSelected = string.IsNullOrWhiteSpace(SelectedCategoryFilter) || SelectedCategoryFilter.Equals("All", StringComparison.OrdinalIgnoreCase);
        Categories.Add(new CategoryFilterItemViewModel("All", isAllSelected));

        bool isUncategorizedSelected = !string.IsNullOrWhiteSpace(SelectedCategoryFilter) && SelectedCategoryFilter.Equals("Uncategorized", StringComparison.OrdinalIgnoreCase);
        Categories.Add(new CategoryFilterItemViewModel("Uncategorized", isUncategorizedSelected));

        foreach (var cat in activeCategories)
        {
            bool isSelected = cat.Equals(SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase);
            Categories.Add(new CategoryFilterItemViewModel(cat, isSelected));
        }

        OnPropertyChanged(nameof(HasCategories));

        if (!string.IsNullOrWhiteSpace(SelectedCategoryFilter) &&
            !SelectedCategoryFilter.Equals("All", StringComparison.OrdinalIgnoreCase) &&
            !Categories.Any(c => c.Name.Equals(SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedCategoryFilter = "All";
        }
        else
        {
            SyncCategoryFilterSelection();
        }
    }

    private void SyncCategoryFilterSelection()
    {
        foreach (var item in Categories)
        {
            item.IsSelected = item.Name.Equals(SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase);
        }
    }

    [RelayCommand]
    private void SelectGoal(LifeGoalItemViewModel? goal)
    {
        if (goal == null) return;
        SelectedGoal = goal;
        IsMobileDetailViewOpen = true;
    }

    [RelayCommand]
    private void CloseMobileDetail()
    {
        IsMobileDetailViewOpen = false;
    }

    [RelayCommand]
    private void SetCategoryFilter(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) return;
        SelectedCategoryFilter = category;
    }

    [RelayCommand]
    private void OpenCreateGoalDialog()
    {
        NewGoalTitle = string.Empty;
        NewGoalDescription = string.Empty;
        NewGoalCategory = string.Empty;
        NewGoalTargetDate = null;
        IsCreatingGoal = true;
    }

    [RelayCommand]
    private void CancelCreateGoal()
    {
        IsCreatingGoal = false;
    }

    [RelayCommand]
    private async Task SaveNewGoalAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGoalTitle)) return;

        try
        {
            var title = NewGoalTitle.Trim();
            if (title.Length > 100) title = title.Substring(0, 100).Trim();

            var desc = string.IsNullOrWhiteSpace(NewGoalDescription) ? null : NewGoalDescription.Trim();
            if (desc != null && desc.Length > 500) desc = desc.Substring(0, 500).Trim();

            var category = string.IsNullOrWhiteSpace(NewGoalCategory) ? "Uncategorized" : NewGoalCategory.Trim();
            if (category.Length > 24) category = category.Substring(0, 24).Trim();

            var goal = new LifeGoal
            {
                Title = title,
                Description = desc,
                Category = category,
                TargetDate = NewGoalTargetDate?.Date
            };

            var saved = await _goalService.SaveGoalAsync(goal);
            var vm = new LifeGoalItemViewModel(saved);
            Goals.Insert(0, vm);

            SelectedCategoryFilter = "All";
            UpdateCategories();
            ApplyCategoryFilter();

            SelectedGoal = vm;
            IsCreatingGoal = false;
            IsMobileDetailViewOpen = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoalsViewModel] Error saving goal: {ex}");
        }
    }

    [RelayCommand]
    private async Task DeleteGoalAsync(LifeGoalItemViewModel? goalVm)
    {
        var target = goalVm ?? SelectedGoal;
        if (target == null) return;

        await _goalService.DeleteGoalAsync(target.Id);
        Goals.Remove(target);
        UpdateCategories();
        ApplyCategoryFilter();

        if (SelectedGoal == target)
        {
            SelectedGoal = FilteredGoals.FirstOrDefault();
        }

        if (FilteredGoals.Count == 0)
        {
            IsMobileDetailViewOpen = false;
        }
    }

    [RelayCommand]
    private async Task ToggleGoalAchievedAsync(LifeGoalItemViewModel? goalVm)
    {
        var target = goalVm ?? SelectedGoal;
        if (target == null) return;

        bool newAchievedState = !target.IsAchieved;

        if (target == SelectedGoal && CurrentMilestones.Count > 0)
        {
            foreach (var m in CurrentMilestones)
            {
                if (m.IsCompleted != newAchievedState)
                {
                    m.IsCompleted = newAchievedState;
                    await _goalService.SaveMilestoneAsync(m.Model);
                }
            }
            int total = CurrentMilestones.Count;
            int completed = newAchievedState ? total : 0;

            target.Model.IsAchieved = newAchievedState;
            target.UpdateMilestonesSummary(completed, total);
        }
        else
        {
            var milestones = (await _goalService.GetMilestonesForGoalAsync(target.Id)).ToList();
            int total = milestones.Count;
            int completed = newAchievedState ? total : 0;

            if (total > 0)
            {
                foreach (var m in milestones)
                {
                    if (m.IsCompleted != newAchievedState)
                    {
                        m.IsCompleted = newAchievedState;
                        await _goalService.SaveMilestoneAsync(m);
                    }
                }
            }

            target.Model.IsAchieved = newAchievedState;
            target.UpdateMilestonesSummary(completed, total);
        }

        await _goalService.SaveGoalAsync(target.Model);
        OnPropertyChanged(nameof(SelectedGoal));
    }

    #region Milestone Commands

    private async Task LoadMilestonesAndJournalForSelectedGoalAsync()
    {
        CurrentMilestones.Clear();
        CurrentJournalEntries.Clear();

        if (SelectedGoal == null) return;

        var milestones = await _goalService.GetMilestonesForGoalAsync(SelectedGoal.Id);
        int completed = 0;
        int total = 0;

        foreach (var m in milestones)
        {
            total++;
            if (m.IsCompleted) completed++;
            CurrentMilestones.Add(new GoalMilestoneItemViewModel(m));
        }

        SelectedGoal.UpdateMilestonesSummary(completed, total);

        var journalEntries = await _goalService.GetJournalEntriesAsync(SelectedGoal.Id);
        foreach (var j in journalEntries)
        {
            CurrentJournalEntries.Add(new JournalEntryItemViewModel(j, SelectedGoal.Title));
        }
    }

    [RelayCommand]
    private async Task AddMilestoneAsync()
    {
        if (SelectedGoal == null || string.IsNullOrWhiteSpace(NewMilestoneTitle)) return;

        var title = NewMilestoneTitle.Trim();
        if (title.Length > 100) title = title.Substring(0, 100).Trim();

        var milestone = new GoalMilestone
        {
            GoalId = SelectedGoal.Id,
            Title = title,
            OrderIndex = CurrentMilestones.Count,
            IsCompleted = false
        };

        var saved = await _goalService.SaveMilestoneAsync(milestone);
        CurrentMilestones.Add(new GoalMilestoneItemViewModel(saved));
        NewMilestoneTitle = string.Empty;

        UpdateSelectedGoalProgress();
    }

    [RelayCommand]
    private async Task ToggleMilestoneAsync(GoalMilestoneItemViewModel? milestoneVm)
    {
        if (milestoneVm == null || SelectedGoal == null) return;

        await _goalService.SaveMilestoneAsync(milestoneVm.Model);
        UpdateSelectedGoalProgress();
    }

    [RelayCommand]
    private async Task DeleteMilestoneAsync(GoalMilestoneItemViewModel? milestoneVm)
    {
        if (milestoneVm == null || SelectedGoal == null) return;

        await _goalService.DeleteMilestoneAsync(milestoneVm.Id);
        CurrentMilestones.Remove(milestoneVm);

        UpdateSelectedGoalProgress();
    }

    private void UpdateSelectedGoalProgress()
    {
        if (SelectedGoal == null) return;

        int total = CurrentMilestones.Count;
        int completed = CurrentMilestones.Count(m => m.IsCompleted);

        SelectedGoal.UpdateMilestonesSummary(completed, total);
        _ = _goalService.SaveGoalAsync(SelectedGoal.Model);
    }

    #endregion

    #region Reflection Journal Commands

    public async Task LoadAllJournalEntriesAsync()
    {
        AllJournalEntries.Clear();
        var rawEntries = await _goalService.GetJournalEntriesAsync();

        var goalsMap = Goals.ToDictionary(g => g.Id, g => g.Title);

        foreach (var entry in rawEntries)
        {
            string? goalTitle = !string.IsNullOrEmpty(entry.GoalId) && goalsMap.TryGetValue(entry.GoalId, out var t) ? t : null;
            AllJournalEntries.Add(new JournalEntryItemViewModel(entry, goalTitle));
        }
    }

    [RelayCommand]
    private void OpenCreateJournalForm()
    {
        NewJournalTitle = string.Empty;
        NewJournalContent = string.Empty;
        IsCreatingJournal = true;
        if (IsCompact)
        {
            IsJournalBottomSheetOpen = true;
        }
    }

    [RelayCommand]
    private void OpenJournalBottomSheet()
    {
        OpenCreateJournalForm();
    }

    [RelayCommand]
    private void CloseJournalBottomSheet()
    {
        IsCreatingJournal = false;
        IsJournalBottomSheetOpen = false;
    }

    [RelayCommand]
    private void ToggleJournalCollapsible()
    {
        IsJournalCollapsibleExpanded = !IsJournalCollapsibleExpanded;
    }

    [RelayCommand]
    private void CancelCreateJournal()
    {
        IsCreatingJournal = false;
        IsJournalBottomSheetOpen = false;
    }

    [RelayCommand]
    private async Task SaveJournalEntryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewJournalTitle) && string.IsNullOrWhiteSpace(NewJournalContent)) return;

        var title = string.IsNullOrWhiteSpace(NewJournalTitle) ? "Reflection" : NewJournalTitle.Trim();
        if (title.Length > 100) title = title.Substring(0, 100).Trim();

        var content = NewJournalContent.Trim();
        if (content.Length > 2000) content = content.Substring(0, 2000).Trim();

        var entry = new JournalEntry
        {
            GoalId = SelectedGoal?.Id,
            Title = title,
            Content = content,
            EntryDate = DateTime.UtcNow
        };

        var saved = await _goalService.SaveJournalEntryAsync(entry);

        var vm = new JournalEntryItemViewModel(saved, SelectedGoal?.Title);
        CurrentJournalEntries.Insert(0, vm);
        AllJournalEntries.Insert(0, vm);

        IsCreatingJournal = false;
        IsJournalBottomSheetOpen = false;
    }

    [RelayCommand]
    private async Task DeleteJournalEntryAsync(JournalEntryItemViewModel? entryVm)
    {
        if (entryVm == null) return;

        await _goalService.DeleteJournalEntryAsync(entryVm.Id);
        CurrentJournalEntries.Remove(entryVm);
        AllJournalEntries.Remove(entryVm);
    }

    #endregion
}
