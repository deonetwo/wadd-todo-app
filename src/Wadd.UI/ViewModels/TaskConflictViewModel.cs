using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Wadd.Services;

namespace Wadd.UI.ViewModels;

public partial class TaskConflictItemViewModel : ObservableObject
{
    public SyncConflict Conflict { get; }
    public TodoItem LocalItem { get; }
    public TodoItem CloudItem { get; }

    public string TaskTitle => string.IsNullOrWhiteSpace(LocalItem.Title) ? (CloudItem.Title ?? "Untitled Task") : LocalItem.Title;
    public string LocalUpdatedTime => FormatTime(Conflict.LocalUpdatedAt);
    public string CloudUpdatedTime => FormatTime(Conflict.CloudUpdatedAt);

    public bool IsLocalDeleted => LocalItem.IsDeleted;
    public bool IsCloudDeleted => CloudItem.IsDeleted;

    public string LocalTaskName => string.IsNullOrWhiteSpace(LocalItem.Title) ? "(No Name)" : LocalItem.Title;
    public string CloudTaskName => string.IsNullOrWhiteSpace(CloudItem.Title) ? "(No Name)" : CloudItem.Title;

    public string LocalTaskStatus => LocalItem.IsCompleted ? "Completed" : "In Progress";
    public string CloudTaskStatus => CloudItem.IsCompleted ? "Completed" : "In Progress";

    public string LocalTaskPriority => LocalItem.Priority.ToString();
    public string CloudTaskPriority => CloudItem.Priority.ToString();

    [ObservableProperty]
    private bool _selectLocalVersion = true;

    [ObservableProperty]
    private bool _selectCloudVersion = false;

    partial void OnSelectLocalVersionChanged(bool value)
    {
        if (value) SelectCloudVersion = false;
    }

    partial void OnSelectCloudVersionChanged(bool value)
    {
        if (value) SelectLocalVersion = false;
    }

    public TaskConflictItemViewModel(SyncConflict conflict)
    {
        Conflict = conflict;
        LocalItem = JsonSerializer.Deserialize<TodoItem>(conflict.LocalVersionJson) ?? new TodoItem();
        CloudItem = JsonSerializer.Deserialize<TodoItem>(conflict.CloudVersionJson) ?? new TodoItem();
        ResetToDefaultSelection();
    }

    public void ResetToDefaultSelection()
    {
        SelectLocalVersion = true;
        SelectCloudVersion = false;
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
        var chosenSource = SelectLocalVersion ? LocalItem : CloudItem;
        return new TodoItem
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
            Version = Math.Max(LocalItem.Version, CloudItem.Version) + 1
        };
    }
}

public partial class TaskConflictViewModel : ObservableObject
{
    private readonly IConflictRepository _conflictRepository;
    private readonly ITodoService _todoService;
    private readonly ISyncService _syncService;

    public ObservableCollection<TaskConflictItemViewModel> Conflicts { get; } = new();

    [ObservableProperty]
    private TaskConflictItemViewModel? _currentItem;

    [ObservableProperty]
    private int _currentConflictIndex = 1;

    [ObservableProperty]
    private int _totalConflicts;

    public event EventHandler? OnCompleted;

    public string ConflictProgressText => TotalConflicts > 0 ? $"Reviewing Conflict {CurrentConflictIndex} of {TotalConflicts}" : "No conflicts";
    public bool CanGoPrevious => CurrentConflictIndex > 1;
    public bool CanContinue => SelectLocalVersion || SelectCloudVersion;

    public string TaskTitle => CurrentItem?.TaskTitle ?? string.Empty;
    public string LocalUpdatedTime => CurrentItem?.LocalUpdatedTime ?? string.Empty;
    public string CloudUpdatedTime => CurrentItem?.CloudUpdatedTime ?? string.Empty;
    public bool IsLocalDeleted => CurrentItem?.IsLocalDeleted ?? false;
    public bool IsCloudDeleted => CurrentItem?.IsCloudDeleted ?? false;
    public string LocalTaskName => CurrentItem?.LocalTaskName ?? string.Empty;
    public string CloudTaskName => CurrentItem?.CloudTaskName ?? string.Empty;
    public string LocalTaskStatus => CurrentItem?.LocalTaskStatus ?? string.Empty;
    public string CloudTaskStatus => CurrentItem?.CloudTaskStatus ?? string.Empty;
    public string LocalTaskPriority => CurrentItem?.LocalTaskPriority ?? string.Empty;
    public string CloudTaskPriority => CurrentItem?.CloudTaskPriority ?? string.Empty;

    public bool SelectLocalVersion
    {
        get => CurrentItem?.SelectLocalVersion ?? false;
        set
        {
            if (CurrentItem != null && CurrentItem.SelectLocalVersion != value)
            {
                CurrentItem.SelectLocalVersion = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectCloudVersion));
                OnPropertyChanged(nameof(CanContinue));
            }
        }
    }

    public bool SelectCloudVersion
    {
        get => CurrentItem?.SelectCloudVersion ?? false;
        set
        {
            if (CurrentItem != null && CurrentItem.SelectCloudVersion != value)
            {
                CurrentItem.SelectCloudVersion = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectLocalVersion));
                OnPropertyChanged(nameof(CanContinue));
            }
        }
    }

    public TaskConflictViewModel(IConflictRepository conflictRepository, ITodoService todoService, ISyncService syncService)
    {
        _conflictRepository = conflictRepository ?? throw new ArgumentNullException(nameof(conflictRepository));
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
    }

    public async Task LoadConflictsAsync()
    {
        Conflicts.Clear();
        var unresolved = await _conflictRepository.GetUnresolvedConflictsAsync();
        var list = unresolved.ToList();

        TotalConflicts = list.Count;
        CurrentConflictIndex = 1;

        foreach (var conflict in list)
        {
            Conflicts.Add(new TaskConflictItemViewModel(conflict));
        }

        CurrentItem = Conflicts.FirstOrDefault();
        if (CurrentItem != null)
        {
            CurrentItem.ResetToDefaultSelection();
        }
        NotifyAllProperties();
    }

    private void NotifyAllProperties()
    {
        OnPropertyChanged(nameof(ConflictProgressText));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(TaskTitle));
        OnPropertyChanged(nameof(LocalUpdatedTime));
        OnPropertyChanged(nameof(CloudUpdatedTime));
        OnPropertyChanged(nameof(IsLocalDeleted));
        OnPropertyChanged(nameof(IsCloudDeleted));
        OnPropertyChanged(nameof(LocalTaskName));
        OnPropertyChanged(nameof(CloudTaskName));
        OnPropertyChanged(nameof(LocalTaskStatus));
        OnPropertyChanged(nameof(CloudTaskStatus));
        OnPropertyChanged(nameof(LocalTaskPriority));
        OnPropertyChanged(nameof(CloudTaskPriority));
        OnPropertyChanged(nameof(SelectLocalVersion));
        OnPropertyChanged(nameof(SelectCloudVersion));
    }

    [RelayCommand]
    private async Task ApplyAndNextAsync()
    {
        if (CurrentItem == null || !CanContinue) return;

        var resolvedItem = CurrentItem.GetSelectedResolvedItem();
        var resolutionType = CurrentItem.SelectLocalVersion ? ConflictResolutionType.KeepLocal : ConflictResolutionType.KeepCloud;

        var resolvedJson = JsonSerializer.Serialize(resolvedItem);
        await _conflictRepository.ResolveConflictAsync(CurrentItem.Conflict.Id, resolutionType, resolvedJson);

        if (_todoService is SQLiteTodoService sqliteSvc)
        {
            await sqliteSvc.DirectUpsertWithLogAsync(resolvedItem);
        }

        Conflicts.Remove(CurrentItem);
        await _syncService.RefreshConflictCountAsync();

        if (Conflicts.Count > 0)
        {
            if (CurrentConflictIndex > Conflicts.Count) CurrentConflictIndex = Conflicts.Count;
            CurrentItem = Conflicts[CurrentConflictIndex - 1];
            CurrentItem.ResetToDefaultSelection();
            NotifyAllProperties();
        }
        else
        {
            CurrentItem = null;
            NotifyAllProperties();
            OnCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void Previous()
    {
        if (CanGoPrevious)
        {
            CurrentConflictIndex--;
            CurrentItem = Conflicts[CurrentConflictIndex - 1];
            CurrentItem.ResetToDefaultSelection();
            NotifyAllProperties();
        }
    }
}
