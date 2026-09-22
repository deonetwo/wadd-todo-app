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
    private readonly IAiGoalService _aiGoalService;
    private readonly IAudioService? _audioService;

    public event Action? DataMutated;

    private void NotifyDataMutated()
    {
        DataMutated?.Invoke();
    }

    public ObservableCollection<LifeGoalItemViewModel> Goals { get; } = new();
    public ObservableCollection<LifeGoalItemViewModel> FilteredGoals { get; } = new();
    public ObservableCollection<GoalMilestoneItemViewModel> CurrentMilestones { get; } = new();
    public ObservableCollection<JournalEntryItemViewModel> CurrentJournalEntries { get; } = new();
    public ObservableCollection<JournalEntryItemViewModel> AllJournalEntries { get; } = new();
    public ObservableCollection<CategoryFilterItemViewModel> Categories { get; } = new();
    public ObservableCollection<string> PendingAiMilestones { get; } = new();

    public bool HasPendingAiMilestones => PendingAiMilestones.Count > 0;

    [ObservableProperty]
    private bool _isAiGeneratingGoal;

    [ObservableProperty]
    private bool _isAiGeneratingMilestones;

    [ObservableProperty]
    private bool _isAiGeneratingJournal;

    [ObservableProperty]
    private string _aiStatusMessage = string.Empty;

    public bool HasCategories => Categories.Count > 1;

    [ObservableProperty]
    private LifeGoalItemViewModel? _selectedGoal;

    partial void OnSelectedGoalChanged(LifeGoalItemViewModel? value)
    {
        foreach (var g in Goals.ToList())
        {
            if (g != null)
            {
                g.IsSelected = (value != null && g.Id == value.Id);
            }
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
    private bool _isCompact = OperatingSystem.IsAndroid();

    [ObservableProperty]
    private bool _isJournalCollapsibleExpanded = true;

    [ObservableProperty]
    private bool _isMilestoneAiChoiceModalOpen;

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

    public bool HasNewGoalTargetDate => NewGoalTargetDate.HasValue;

    partial void OnNewGoalTargetDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(HasNewGoalTargetDate));
    }

    [RelayCommand]
    private void ClearNewGoalTargetDate()
    {
        NewGoalTargetDate = null;
    }

    // New Milestone Form Property
    [ObservableProperty]
    private string _newMilestoneTitle = string.Empty;

    // New Journal Form Properties
    [ObservableProperty]
    private string _newJournalTitle = string.Empty;

    [ObservableProperty]
    private string _newJournalContent = string.Empty;

    public GoalsViewModel(IGoalService goalService)
        : this(goalService, new Wadd.Services.AiGoalService(new System.Net.Http.HttpClient()), null)
    {
    }

    public GoalsViewModel(IGoalService goalService, IAiGoalService aiGoalService, IAudioService? audioService = null)
    {
        _goalService = goalService ?? throw new ArgumentNullException(nameof(goalService));
        _aiGoalService = aiGoalService ?? throw new ArgumentNullException(nameof(aiGoalService));
        _audioService = audioService;
        _ = InitializeAsync();
    }

    public Func<string, string, string, string, Task<bool>>? ConfirmDeleteRequested { get; set; }

    private async Task<bool> PromptDeleteConfirmationAsync(string title, string message, string itemName, string itemDetails = "")
    {
        if (ConfirmDeleteRequested != null)
        {
            return await ConfirmDeleteRequested(title, message, itemName, itemDetails);
        }
        return true;
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
        var activeCategories = (Goals ?? Enumerable.Empty<LifeGoalItemViewModel>())
            .Where(g => g != null)
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
        if (Categories == null) return;
        var snapshot = Categories.ToList();
        foreach (var item in snapshot)
        {
            if (item != null)
            {
                item.IsSelected = !string.IsNullOrEmpty(SelectedCategoryFilter) && string.Equals(item.Name, SelectedCategoryFilter, StringComparison.OrdinalIgnoreCase);
            }
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

    [ObservableProperty]
    private string _newPendingMilestoneTitle = string.Empty;

    [RelayCommand]
    private void OpenCreateGoalDialog()
    {
        NewGoalTitle = string.Empty;
        NewGoalDescription = string.Empty;
        NewGoalCategory = string.Empty;
        NewGoalTargetDate = null;
        NewPendingMilestoneTitle = string.Empty;
        PendingAiMilestones.Clear();
        OnPropertyChanged(nameof(HasPendingAiMilestones));
        IsAiGeneratingGoal = false;
        AiStatusMessage = string.Empty;
        IsCreatingGoal = true;
    }

    [RelayCommand]
    private void CancelCreateGoal()
    {
        NewPendingMilestoneTitle = string.Empty;
        PendingAiMilestones.Clear();
        OnPropertyChanged(nameof(HasPendingAiMilestones));
        IsAiGeneratingGoal = false;
        AiStatusMessage = string.Empty;
        IsCreatingGoal = false;
    }

    [RelayCommand]
    private void AddPendingMilestone()
    {
        if (string.IsNullOrWhiteSpace(NewPendingMilestoneTitle)) return;
        var title = NewPendingMilestoneTitle.Trim();
        if (title.Length > 100) title = title.Substring(0, 100).Trim();
        PendingAiMilestones.Add(title);
        NewPendingMilestoneTitle = string.Empty;
        OnPropertyChanged(nameof(HasPendingAiMilestones));
    }

    [RelayCommand]
    private void DeletePendingMilestone(string? milestone)
    {
        if (string.IsNullOrWhiteSpace(milestone)) return;
        PendingAiMilestones.Remove(milestone);
        OnPropertyChanged(nameof(HasPendingAiMilestones));
    }

    [RelayCommand]
    private void MovePendingMilestoneUp(string? milestone)
    {
        if (string.IsNullOrWhiteSpace(milestone)) return;
        int index = PendingAiMilestones.IndexOf(milestone);
        if (index > 0)
        {
            PendingAiMilestones.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MovePendingMilestoneDown(string? milestone)
    {
        if (string.IsNullOrWhiteSpace(milestone)) return;
        int index = PendingAiMilestones.IndexOf(milestone);
        if (index >= 0 && index < PendingAiMilestones.Count - 1)
        {
            PendingAiMilestones.Move(index, index + 1);
        }
    }

    public Action<string, NotificationBubbleType?>? StatusNotificationRequested { get; set; }

    private void NotifyStatus(string message, NotificationBubbleType? type = null)
    {
        StatusNotificationRequested?.Invoke(message, type);
    }

    [RelayCommand]
    private async Task AutoFillGoalWithAiAsync()
    {
        if (IsAiGeneratingGoal) return;

        var prompt = string.IsNullOrWhiteSpace(NewGoalTitle) ? "Achieve meaningful personal breakthrough" : NewGoalTitle.Trim();

        try
        {
            IsAiGeneratingGoal = true;
            var draftingMsg = "Drafting goal plan & milestones...";
            AiStatusMessage = draftingMsg;
            NotifyStatus(draftingMsg, NotificationBubbleType.Info);

            var existingSteps = PendingAiMilestones.ToList();
            if (!string.IsNullOrWhiteSpace(NewPendingMilestoneTitle))
            {
                existingSteps.Add(NewPendingMilestoneTitle.Trim());
            }

            var result = await _aiGoalService.GenerateGoalDetailsAsync(
                goalTitleOrPrompt: NewGoalTitle,
                category: NewGoalCategory,
                targetDate: NewGoalTargetDate,
                description: NewGoalDescription,
                existingMilestones: existingSteps.Count > 0 ? existingSteps : null,
                newMilestoneDraft: NewPendingMilestoneTitle);

            if (!string.IsNullOrWhiteSpace(result.Title))
            {
                NewGoalTitle = result.Title;
            }
            if (!string.IsNullOrWhiteSpace(result.Category))
            {
                NewGoalCategory = result.Category;
            }
            if (result.TargetDate.HasValue && !NewGoalTargetDate.HasValue)
            {
                NewGoalTargetDate = result.TargetDate;
            }
            if (!string.IsNullOrWhiteSpace(result.Description))
            {
                NewGoalDescription = result.Description;
            }

            PendingAiMilestones.Clear();
            if (result.SuggestedMilestones != null && result.SuggestedMilestones.Count > 0)
            {
                foreach (var m in result.SuggestedMilestones)
                {
                    if (!string.IsNullOrWhiteSpace(m) && !PendingAiMilestones.Contains(m.Trim()))
                    {
                        PendingAiMilestones.Add(m.Trim());
                    }
                }
            }
            OnPropertyChanged(nameof(HasPendingAiMilestones));
            if (result.IsLiveAi)
            {
                var msg = $"Generated with Live AI ({result.SourceLabel})";
                AiStatusMessage = msg;
                NotifyStatus(msg, NotificationBubbleType.Success);
            }
            else
            {
                var msg = "Generated with Smart Offline Engine";
                AiStatusMessage = msg;
                NotifyStatus(msg, NotificationBubbleType.Info);
            }
        }
        catch (Exception ex)
        {
            var msg = $"Could not auto-generate: {ex.Message}";
            AiStatusMessage = msg;
            NotifyStatus(msg, NotificationBubbleType.Error);
            Wadd.Core.Logging.AppLogger.LogError("GoalsViewModel", "AutoFillGoalWithAi error", ex);
        }
        finally
        {
            IsAiGeneratingGoal = false;
        }
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

            // Save any pending AI-generated milestones
            if (PendingAiMilestones.Count > 0)
            {
                int order = 0;
                foreach (var milestoneTitle in PendingAiMilestones)
                {
                    if (!string.IsNullOrWhiteSpace(milestoneTitle))
                    {
                        await _goalService.SaveMilestoneAsync(new GoalMilestone
                        {
                            GoalId = saved.Id,
                            Title = milestoneTitle.Trim(),
                            OrderIndex = order++,
                            IsCompleted = false
                        });
                    }
                }
                PendingAiMilestones.Clear();
                OnPropertyChanged(nameof(HasPendingAiMilestones));
            }

            var vm = new LifeGoalItemViewModel(saved);
            var milestones = (await _goalService.GetMilestonesForGoalAsync(saved.Id)).ToList();
            vm.UpdateMilestonesSummary(milestones.Count(m => m.IsCompleted), milestones.Count);

            Goals.Insert(0, vm);

            SelectedCategoryFilter = "All";
            UpdateCategories();
            ApplyCategoryFilter();

            SelectedGoal = vm;
            IsCreatingGoal = false;
            IsMobileDetailViewOpen = true;
            NotifyDataMutated();
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

        var lm = Wadd.UI.Localization.LocalizationManager.Instance;
        var title = lm["Dialog_Delete_Goal_Title"];
        var msg = lm["Dialog_Delete_Goal_Msg"];
        var itemName = target.Title;
        var itemDetails = !string.IsNullOrWhiteSpace(target.Category) ? $"Category: {target.Category}" : string.Empty;

        var confirmed = await PromptDeleteConfirmationAsync(title, msg, itemName, itemDetails);
        if (!confirmed) return;

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
        NotifyDataMutated();
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

        if (newAchievedState)
        {
            _audioService?.PlayCompletedSound();
        }
        NotifyDataMutated();
    }

    #region Milestone Commands

    private int _loadMilestonesVersion;

    private async Task LoadMilestonesAndJournalForSelectedGoalAsync()
    {
        var targetGoal = SelectedGoal;
        if (targetGoal == null)
        {
            CurrentMilestones.Clear();
            CurrentJournalEntries.Clear();
            return;
        }

        int currentVersion = Interlocked.Increment(ref _loadMilestonesVersion);

        var milestones = (await _goalService.GetMilestonesForGoalAsync(targetGoal.Id)).ToList();
        var journalEntries = (await _goalService.GetJournalEntriesAsync(targetGoal.Id)).ToList();

        if (currentVersion != _loadMilestonesVersion || SelectedGoal?.Id != targetGoal.Id)
        {
            return;
        }

        CurrentMilestones.Clear();
        CurrentJournalEntries.Clear();

        int completed = 0;
        int total = 0;

        foreach (var m in milestones)
        {
            total++;
            if (m.IsCompleted) completed++;
            CurrentMilestones.Add(new GoalMilestoneItemViewModel(m));
        }

        targetGoal.UpdateMilestonesSummary(completed, total);

        foreach (var j in journalEntries)
        {
            CurrentJournalEntries.Add(new JournalEntryItemViewModel(j, targetGoal.Title));
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
        NotifyDataMutated();
    }

    [RelayCommand]
    private async Task ToggleMilestoneAsync(GoalMilestoneItemViewModel? milestoneVm)
    {
        if (milestoneVm == null || SelectedGoal == null) return;

        bool isCompleting = milestoneVm.IsCompleted;
        if (isCompleting)
        {
            _audioService?.PlayCompletedSound();
        }

        await _goalService.SaveMilestoneAsync(milestoneVm.Model);
        UpdateSelectedGoalProgress();
        NotifyDataMutated();
    }

    [RelayCommand]
    private async Task MoveMilestoneUpAsync(GoalMilestoneItemViewModel? milestoneVm)
    {
        if (milestoneVm == null || SelectedGoal == null) return;
        int index = CurrentMilestones.IndexOf(milestoneVm);
        if (index > 0)
        {
            CurrentMilestones.Move(index, index - 1);
            for (int i = 0; i < CurrentMilestones.Count; i++)
            {
                CurrentMilestones[i].Model.OrderIndex = i;
                await _goalService.SaveMilestoneAsync(CurrentMilestones[i].Model);
            }
            NotifyDataMutated();
        }
    }

    [RelayCommand]
    private async Task MoveMilestoneDownAsync(GoalMilestoneItemViewModel? milestoneVm)
    {
        if (milestoneVm == null || SelectedGoal == null) return;
        int index = CurrentMilestones.IndexOf(milestoneVm);
        if (index >= 0 && index < CurrentMilestones.Count - 1)
        {
            CurrentMilestones.Move(index, index + 1);
            for (int i = 0; i < CurrentMilestones.Count; i++)
            {
                CurrentMilestones[i].Model.OrderIndex = i;
                await _goalService.SaveMilestoneAsync(CurrentMilestones[i].Model);
            }
            NotifyDataMutated();
        }
    }

    [RelayCommand]
    private async Task DeleteMilestoneAsync(GoalMilestoneItemViewModel? milestoneVm)
    {
        if (milestoneVm == null || SelectedGoal == null) return;

        var lm = Wadd.UI.Localization.LocalizationManager.Instance;
        var title = lm["Dialog_Delete_Milestone_Title"];
        var msg = lm["Dialog_Delete_Milestone_Msg"];
        var itemName = milestoneVm.Title;

        var confirmed = await PromptDeleteConfirmationAsync(title, msg, itemName);
        if (!confirmed) return;

        await _goalService.DeleteMilestoneAsync(milestoneVm.Id);
        CurrentMilestones.Remove(milestoneVm);

        UpdateSelectedGoalProgress();
        NotifyDataMutated();
    }

    private void UpdateSelectedGoalProgress()
    {
        if (SelectedGoal == null) return;

        bool wasAchieved = SelectedGoal.IsAchieved;
        int total = CurrentMilestones.Count;
        int completed = CurrentMilestones.Count(m => m.IsCompleted);

        SelectedGoal.UpdateMilestonesSummary(completed, total);
        _ = _goalService.SaveGoalAsync(SelectedGoal.Model);
        OnPropertyChanged(nameof(SelectedGoal));

        if (!wasAchieved && SelectedGoal.IsAchieved)
        {
            _audioService?.PlayCompletedSound();
        }
    }

    [RelayCommand]
    private async Task GenerateMilestonesWithAiAsync()
    {
        if (SelectedGoal == null || IsAiGeneratingMilestones) return;

        if (CurrentMilestones.Count == 0)
        {
            await ReplaceAllMilestonesWithAiAsync();
        }
        else
        {
            IsMilestoneAiChoiceModalOpen = true;
        }
    }

    [RelayCommand]
    private void CloseMilestoneAiChoiceModal()
    {
        IsMilestoneAiChoiceModalOpen = false;
    }

    [RelayCommand]
    private async Task AppendNextMilestonesWithAiAsync()
    {
        if (SelectedGoal == null || IsAiGeneratingMilestones) return;

        try
        {
            IsAiGeneratingMilestones = true;
            IsMilestoneAiChoiceModalOpen = false;

            var existingTitles = CurrentMilestones.Select(m => m.Title).ToList();
            var draftStep = !string.IsNullOrWhiteSpace(NewMilestoneTitle) ? NewMilestoneTitle.Trim() : null;

            var suggested = await _aiGoalService.GenerateMilestonesAsync(
                goalTitle: SelectedGoal.Title,
                category: SelectedGoal.Category,
                description: SelectedGoal.Description,
                existingMilestones: existingTitles,
                targetDate: SelectedGoal.TargetDate,
                newMilestoneDraft: draftStep);

            if (suggested != null && suggested.Count > 0)
            {
                int order = CurrentMilestones.Count;
                foreach (var s in suggested)
                {
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        var trimmed = s.Trim();
                        // Extra deduplication guard against identical titles
                        if (CurrentMilestones.Any(m => m.Title.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        var milestone = new GoalMilestone
                        {
                            GoalId = SelectedGoal.Id,
                            Title = trimmed,
                            OrderIndex = order++,
                            IsCompleted = false
                        };
                        var saved = await _goalService.SaveMilestoneAsync(milestone);
                        CurrentMilestones.Add(new GoalMilestoneItemViewModel(saved));
                    }
                }
                UpdateSelectedGoalProgress();
                NotifyDataMutated();
            }
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("GoalsViewModel", "AppendNextMilestonesWithAi error", ex);
        }
        finally
        {
            IsAiGeneratingMilestones = false;
        }
    }

    [RelayCommand]
    private async Task ReplaceAllMilestonesWithAiAsync()
    {
        if (SelectedGoal == null || IsAiGeneratingMilestones) return;

        try
        {
            IsAiGeneratingMilestones = true;
            IsMilestoneAiChoiceModalOpen = false;

            var draftStep = !string.IsNullOrWhiteSpace(NewMilestoneTitle) ? NewMilestoneTitle.Trim() : null;

            // 1. Delete existing milestones from DB
            var existing = CurrentMilestones.ToList();
            foreach (var m in existing)
            {
                await _goalService.DeleteMilestoneAsync(m.Id);
            }
            CurrentMilestones.Clear();

            // 2. Generate brand new comprehensive roadmap from scratch
            var suggested = await _aiGoalService.GenerateMilestonesAsync(
                goalTitle: SelectedGoal.Title,
                category: SelectedGoal.Category,
                description: SelectedGoal.Description,
                existingMilestones: null,
                targetDate: SelectedGoal.TargetDate,
                newMilestoneDraft: draftStep);

            if (suggested != null && suggested.Count > 0)
            {
                int order = 0;
                foreach (var s in suggested)
                {
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        var milestone = new GoalMilestone
                        {
                            GoalId = SelectedGoal.Id,
                            Title = s.Trim(),
                            OrderIndex = order++,
                            IsCompleted = false
                        };
                        var saved = await _goalService.SaveMilestoneAsync(milestone);
                        CurrentMilestones.Add(new GoalMilestoneItemViewModel(saved));
                    }
                }
                UpdateSelectedGoalProgress();
                NotifyDataMutated();
            }
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("GoalsViewModel", "ReplaceAllMilestonesWithAi error", ex);
        }
        finally
        {
            IsAiGeneratingMilestones = false;
        }
    }

    #endregion

    #region Reflection Journal Commands

    public async Task LoadAllJournalEntriesAsync()
    {
        AllJournalEntries.Clear();
        var rawEntries = await _goalService.GetJournalEntriesAsync();

        var goalsMap = Goals
            .Where(g => g != null && !string.IsNullOrEmpty(g.Id))
            .GroupBy(g => g.Id)
            .ToDictionary(g => g.Key, g => g.First().Title ?? string.Empty);

        foreach (var entry in rawEntries)
        {
            if (entry == null) continue;
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
    private async Task GenerateJournalDraftWithAiAsync()
    {
        if (SelectedGoal == null || IsAiGeneratingJournal) return;

        try
        {
            IsAiGeneratingJournal = true;
            int completed = CurrentMilestones.Count(m => m.IsCompleted);
            int total = CurrentMilestones.Count;
            var recentMilestone = CurrentMilestones.LastOrDefault(m => m.IsCompleted)?.Title;
            var allMilestonesList = CurrentMilestones.Select(m => $"[{(m.IsCompleted ? "Completed" : "Pending")}] {m.Title}").ToList();
            var draftUserContent = !string.IsNullOrWhiteSpace(NewJournalContent) ? NewJournalContent : NewJournalTitle;

            var draft = await _aiGoalService.GenerateJournalPromptAsync(
                goalTitle: SelectedGoal.Title,
                completedSteps: completed,
                totalSteps: total,
                recentMilestone: recentMilestone,
                category: SelectedGoal.Category,
                description: SelectedGoal.Description,
                allMilestones: allMilestonesList,
                currentJournalDraft: draftUserContent);

            if (draft != null)
            {
                NewJournalTitle = draft.Title;
                NewJournalContent = draft.Content;
                IsCreatingJournal = true;
                if (IsCompact)
                {
                    IsJournalBottomSheetOpen = true;
                }
            }
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogError("GoalsViewModel", "GenerateJournalDraftWithAi error", ex);
        }
        finally
        {
            IsAiGeneratingJournal = false;
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
        NotifyDataMutated();
    }

    [RelayCommand]
    private async Task DeleteJournalEntryAsync(JournalEntryItemViewModel? entryVm)
    {
        if (entryVm == null) return;

        var lm = Wadd.UI.Localization.LocalizationManager.Instance;
        var title = lm["Dialog_Delete_Journal_Title"];
        var msg = lm["Dialog_Delete_Journal_Msg"];
        var itemName = entryVm.EntryDate.ToString("D");
        var itemDetails = !string.IsNullOrWhiteSpace(entryVm.Content)
            ? (entryVm.Content.Length > 80 ? entryVm.Content.Substring(0, 80) + "..." : entryVm.Content)
            : string.Empty;

        var confirmed = await PromptDeleteConfirmationAsync(title, msg, itemName, itemDetails);
        if (!confirmed) return;

        await _goalService.DeleteJournalEntryAsync(entryVm.Id);
        CurrentJournalEntries.Remove(entryVm);
        AllJournalEntries.Remove(entryVm);
        NotifyDataMutated();
    }

    #endregion
}
