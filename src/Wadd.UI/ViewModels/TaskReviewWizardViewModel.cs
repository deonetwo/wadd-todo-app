using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Wadd.Services;

namespace Wadd.UI.ViewModels;

public partial class TaskReviewItemViewModel : ObservableObject
{
    public SyncConflict Conflict { get; }
    public TodoItem ItemA { get; }
    public TodoItem ItemB { get; }
    public string TaskTitle => string.IsNullOrWhiteSpace(ItemA.Title) ? (ItemB.Title ?? "Untitled Task") : ItemA.Title;

    public string QuestionPrompt { get; private set; } = "Which version is correct?";
    public string OptionADisplay { get; private set; } = string.Empty;
    public string OptionATime { get; private set; } = string.Empty;
    public string OptionBDisplay { get; private set; } = string.Empty;
    public string OptionBTime { get; private set; } = string.Empty;

    [ObservableProperty]
    private bool _isOptionAChecked = true;

    [ObservableProperty]
    private bool _isOptionBChecked = false;

    partial void OnIsOptionACheckedChanged(bool value)
    {
        if (value) IsOptionBChecked = false;
    }

    partial void OnIsOptionBCheckedChanged(bool value)
    {
        if (value) IsOptionAChecked = false;
    }

    public TaskReviewItemViewModel(SyncConflict conflict)
    {
        Conflict = conflict;
        var localItem = JsonSerializer.Deserialize<TodoItem>(conflict.LocalVersionJson) ?? new TodoItem();
        var cloudItem = JsonSerializer.Deserialize<TodoItem>(conflict.CloudVersionJson) ?? new TodoItem();

        // Put the most recent update as Option A (first choice, pre-selected by default)
        if (conflict.LocalUpdatedAt >= conflict.CloudUpdatedAt)
        {
            ItemA = localItem;
            ItemB = cloudItem;
            OptionATime = FormatTime(conflict.LocalUpdatedAt);
            OptionBTime = FormatTime(conflict.CloudUpdatedAt);
        }
        else
        {
            ItemA = cloudItem;
            ItemB = localItem;
            OptionATime = FormatTime(conflict.CloudUpdatedAt);
            OptionBTime = FormatTime(conflict.LocalUpdatedAt);
        }

        ConfigureCondition();
    }

    private void ConfigureCondition()
    {

        var titleDiffers = !string.Equals(ItemA.Title, ItemB.Title, StringComparison.Ordinal);
        var statusDiffers = ItemA.IsCompleted != ItemB.IsCompleted;
        var dueDiffers = ItemA.DueDate != ItemB.DueDate;
        var reminderDiffers = ItemA.ReminderAt != ItemB.ReminderAt;
        var deletedDiffers = ItemA.IsDeleted != ItemB.IsDeleted;
        var priorityDiffers = ItemA.Priority != ItemB.Priority;

        if (deletedDiffers)
        {
            QuestionPrompt = "Was this task deleted?";
            OptionADisplay = ItemA.IsDeleted ? "🗑️ Task Deleted" : GetFormattedItemDetails(ItemA);
            OptionBDisplay = ItemB.IsDeleted ? "🗑️ Task Deleted" : GetFormattedItemDetails(ItemB);
        }
        else if (titleDiffers)
        {
            QuestionPrompt = "Which task name is correct?";
            OptionADisplay = string.IsNullOrWhiteSpace(ItemA.Title) ? "(No Name)" : ItemA.Title;
            OptionBDisplay = string.IsNullOrWhiteSpace(ItemB.Title) ? "(No Name)" : ItemB.Title;
        }
        else if (statusDiffers)
        {
            QuestionPrompt = "Which status is correct?";
            OptionADisplay = ItemA.IsCompleted ? "Completed" : "Not completed";
            OptionBDisplay = ItemB.IsCompleted ? "Completed" : "Not completed";
        }
        else if (dueDiffers)
        {
            QuestionPrompt = "Which due date is correct?";
            OptionADisplay = ItemA.DueDate.HasValue ? $"📅 Due: {ItemA.DueDate.Value:MMM d, yyyy}" : "📅 No due date";
            OptionBDisplay = ItemB.DueDate.HasValue ? $"📅 Due: {ItemB.DueDate.Value:MMM d, yyyy}" : "📅 No due date";
        }
        else if (reminderDiffers)
        {
            QuestionPrompt = "Which reminder time is correct?";
            OptionADisplay = ItemA.ReminderAt.HasValue ? $"🔔 Reminder: {ItemA.ReminderAt.Value:MMM d, h:mm tt}" : "🔔 No reminder";
            OptionBDisplay = ItemB.ReminderAt.HasValue ? $"🔔 Reminder: {ItemB.ReminderAt.Value:MMM d, h:mm tt}" : "🔔 No reminder";
        }
        else if (priorityDiffers)
        {
            QuestionPrompt = "Which priority is correct?";
            OptionADisplay = $"Priority: {ItemA.Priority}";
            OptionBDisplay = $"Priority: {ItemB.Priority}";
        }
        else
        {
            QuestionPrompt = "Which version is correct?";
            OptionADisplay = GetFormattedItemDetails(ItemA);
            OptionBDisplay = GetFormattedItemDetails(ItemB);
        }
    }

    private static string GetFormattedItemDetails(TodoItem item)
    {
        if (item.IsDeleted) return "🗑️ Task Deleted";
        
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Title)) parts.Add($"Name: {item.Title}");
        parts.Add(item.IsCompleted ? "Status: Completed" : "Status: Not completed");
        if (item.DueDate.HasValue) parts.Add($"📅 Due: {item.DueDate.Value:MMM d, yyyy}");
        if (item.ReminderAt.HasValue) parts.Add($"🔔 Reminder: {item.ReminderAt.Value:MMM d, h:mm tt}");
        parts.Add($"Priority: {item.Priority}");

        return string.Join(" • ", parts);
    }

    private static string FormatTime(DateTime dt)
    {
        var local = dt.ToLocalTime();
        if (local.Date == DateTime.Now.Date)
        {
            return $"Updated {local:h:mm tt}";
        }
        return $"Updated {local:MMM d, h:mm tt}";
    }

    public TodoItem GetSelectedResolvedItem()
    {
        var chosenSource = IsOptionAChecked ? ItemA : ItemB;
        var resolved = new TodoItem
        {
            Id = chosenSource.Id,
            Title = chosenSource.Title,
            Description = chosenSource.Description,
            IsCompleted = chosenSource.IsCompleted,
            Priority = chosenSource.Priority,
            DueDate = chosenSource.DueDate,
            ReminderAt = chosenSource.ReminderAt,
            IsDeleted = chosenSource.IsDeleted,
            CreatedAt = chosenSource.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            Version = Math.Max(ItemA.Version, ItemB.Version) + 1
        };

        return resolved;
    }
}

public partial class TaskReviewWizardViewModel : ObservableObject
{
    private readonly IConflictRepository _conflictRepository;
    private readonly ITodoService _todoService;
    private readonly ISyncService _syncService;

    public ObservableCollection<TaskReviewItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private TaskReviewItemViewModel? _currentItem;

    [ObservableProperty]
    private int _currentIndex = 1;

    [ObservableProperty]
    private int _totalCount;

    public event EventHandler? OnCompleted;

    public string HeaderText => TotalCount > 0 ? $"Task {CurrentIndex} of {TotalCount}" : "No tasks to review";
    public double ProgressPercentage => TotalCount > 0 ? ((double)CurrentIndex / TotalCount) * 100.0 : 0.0;
    public bool HasPrevious => CurrentIndex > 1;
    public bool HasNext => CurrentIndex < TotalCount;
    public string ContinueButtonText => CurrentIndex >= TotalCount ? "Save & Continue" : "Continue";

    public TaskReviewWizardViewModel(IConflictRepository conflictRepository, ITodoService todoService, ISyncService syncService)
    {
        _conflictRepository = conflictRepository ?? throw new ArgumentNullException(nameof(conflictRepository));
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
    }

    public async Task LoadTasksForReviewAsync()
    {
        Items.Clear();
        var unresolved = await _conflictRepository.GetUnresolvedConflictsAsync();
        var list = unresolved.ToList();

        TotalCount = list.Count;
        CurrentIndex = 1;

        foreach (var conflict in list)
        {
            Items.Add(new TaskReviewItemViewModel(conflict));
        }

        CurrentItem = Items.FirstOrDefault();
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(ProgressPercentage));
        OnPropertyChanged(nameof(HasPrevious));
        OnPropertyChanged(nameof(HasNext));
        OnPropertyChanged(nameof(ContinueButtonText));
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        if (CurrentItem == null) return;
        var resolvedItem = CurrentItem.GetSelectedResolvedItem();
        var resolutionType = CurrentItem.IsOptionAChecked ? ConflictResolutionType.KeepLocal : ConflictResolutionType.KeepCloud;

        await ResolveCurrentItemAsync(resolutionType, resolvedItem);
    }

    [RelayCommand]
    private void PreviousTask()
    {
        if (HasPrevious)
        {
            CurrentIndex--;
            CurrentItem = Items[CurrentIndex - 1];
            UpdateProgress();
        }
    }

    [RelayCommand]
    private void NextTask()
    {
        if (HasNext)
        {
            CurrentIndex++;
            CurrentItem = Items[CurrentIndex - 1];
            UpdateProgress();
        }
    }

    private async Task ResolveCurrentItemAsync(ConflictResolutionType resolutionType, TodoItem resolvedItem)
    {
        if (CurrentItem == null) return;

        var resolvedJson = JsonSerializer.Serialize(resolvedItem);
        await _conflictRepository.ResolveConflictAsync(CurrentItem.Conflict.Id, resolutionType, resolvedJson);

        if (_todoService is SQLiteTodoService sqliteSvc)
        {
            await sqliteSvc.DirectUpsertWithLogAsync(resolvedItem);
        }

        Items.Remove(CurrentItem);
        await _syncService.RefreshConflictCountAsync();

        if (Items.Count > 0)
        {
            if (CurrentIndex > Items.Count) CurrentIndex = Items.Count;
            CurrentItem = Items[CurrentIndex - 1];
            UpdateProgress();
        }
        else
        {
            CurrentItem = null;
            UpdateProgress();
            OnCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
}
