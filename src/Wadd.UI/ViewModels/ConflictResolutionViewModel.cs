using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Wadd.Services;

namespace Wadd.UI.ViewModels;

public partial class FieldDiffViewModel : ObservableObject
{
    public string FieldName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string LocalValue { get; set; } = string.Empty;
    public string CloudValue { get; set; } = string.Empty;
    public bool IsDifferent { get; set; }

    [ObservableProperty]
    private bool _useLocal = true;

    [ObservableProperty]
    private bool _useCloud = false;

    partial void OnUseLocalChanged(bool value)
    {
        if (value) UseCloud = false;
    }

    partial void OnUseCloudChanged(bool value)
    {
        if (value) UseLocal = false;
    }
}

public partial class ConflictItemViewModel : ObservableObject
{
    public SyncConflict Conflict { get; }
    public TodoItem LocalItem { get; }
    public TodoItem CloudItem { get; }
    public string RecordName => string.IsNullOrWhiteSpace(LocalItem.Title) ? (CloudItem.Title ?? "Untitled Task") : LocalItem.Title;
    public List<string> ConflictingFields { get; }
    public ObservableCollection<FieldDiffViewModel> FieldDiffs { get; } = new();

    public ConflictItemViewModel(SyncConflict conflict)
    {
        Conflict = conflict;
        LocalItem = JsonSerializer.Deserialize<TodoItem>(conflict.LocalVersionJson) ?? new TodoItem();
        CloudItem = JsonSerializer.Deserialize<TodoItem>(conflict.CloudVersionJson) ?? new TodoItem();
        ConflictingFields = JsonSerializer.Deserialize<List<string>>(conflict.ConflictingFieldsJson) ?? new List<string>();

        BuildFieldDiffs();
    }

    private void BuildFieldDiffs()
    {
        AddFieldDiff(nameof(TodoItem.Title), "Task Title", LocalItem.Title, CloudItem.Title);
        AddFieldDiff(nameof(TodoItem.Description), "Description", LocalItem.Description, CloudItem.Description);
        AddFieldDiff(nameof(TodoItem.IsCompleted), "Status", LocalItem.IsCompleted ? "Completed" : "Active", CloudItem.IsCompleted ? "Completed" : "Active");
        AddFieldDiff(nameof(TodoItem.Priority), "Priority", LocalItem.Priority.ToString(), CloudItem.Priority.ToString());
        AddFieldDiff(nameof(TodoItem.DueDate), "Due Date", LocalItem.DueDate?.ToString("MMM d, yyyy") ?? "None", CloudItem.DueDate?.ToString("MMM d, yyyy") ?? "None");
        AddFieldDiff(nameof(TodoItem.ReminderAt), "Reminder", LocalItem.ReminderAt?.ToString("MMM d, yyyy h:mm tt") ?? "None", CloudItem.ReminderAt?.ToString("MMM d, yyyy h:mm tt") ?? "None");
    }

    private void AddFieldDiff(string name, string displayName, string localVal, string cloudVal)
    {
        var isDiff = !string.Equals(localVal, cloudVal, StringComparison.Ordinal);
        var isConflicting = ConflictingFields.Contains(name);

        // Only display fields that actually differ between versions
        if (isDiff || isConflicting)
        {
            FieldDiffs.Add(new FieldDiffViewModel
            {
                FieldName = name,
                DisplayName = displayName,
                LocalValue = string.IsNullOrWhiteSpace(localVal) ? "(Empty)" : localVal,
                CloudValue = string.IsNullOrWhiteSpace(cloudVal) ? "(Empty)" : cloudVal,
                IsDifferent = true,
                UseLocal = true,
                UseCloud = false
            });
        }
    }

    public TodoItem BuildMergedItem()
    {
        var merged = new TodoItem
        {
            Id = LocalItem.Id,
            CreatedAt = LocalItem.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            Version = Math.Max(LocalItem.Version, CloudItem.Version) + 1
        };

        foreach (var diff in FieldDiffs)
        {
            switch (diff.FieldName)
            {
                case nameof(TodoItem.Title):
                    merged.Title = diff.UseLocal ? LocalItem.Title : CloudItem.Title;
                    break;
                case nameof(TodoItem.Description):
                    merged.Description = diff.UseLocal ? LocalItem.Description : CloudItem.Description;
                    break;
                case nameof(TodoItem.IsCompleted):
                    merged.IsCompleted = diff.UseLocal ? LocalItem.IsCompleted : CloudItem.IsCompleted;
                    break;
                case nameof(TodoItem.Priority):
                    merged.Priority = diff.UseLocal ? LocalItem.Priority : CloudItem.Priority;
                    break;
                case nameof(TodoItem.DueDate):
                    merged.DueDate = diff.UseLocal ? LocalItem.DueDate : CloudItem.DueDate;
                    break;
                case nameof(TodoItem.ReminderAt):
                    merged.ReminderAt = diff.UseLocal ? LocalItem.ReminderAt : CloudItem.ReminderAt;
                    break;
            }
        }

        return merged;
    }
}

public partial class ConflictResolutionViewModel : ObservableObject
{
    private readonly IConflictRepository _conflictRepository;
    private readonly ITodoService _todoService;
    private readonly ISyncService _syncService;

    public ObservableCollection<ConflictItemViewModel> Conflicts { get; } = new();

    [ObservableProperty]
    private ConflictItemViewModel? _selectedConflict;

    [ObservableProperty]
    private int _initialTotalCount;

    [ObservableProperty]
    private int _resolvedCount;

    [ObservableProperty]
    private bool _isConfirmationVisible;

    [ObservableProperty]
    private string _confirmationMessage = string.Empty;

    private Action? _pendingBatchAction;

    public event EventHandler? OnCompleted;

    public int CurrentIndex => Conflicts.Count > 0 && SelectedConflict != null ? Conflicts.IndexOf(SelectedConflict) + 1 : 0;
    public int RemainingCount => Conflicts.Count;
    public double ProgressPercentage => InitialTotalCount > 0 ? ((double)ResolvedCount / InitialTotalCount) * 100.0 : 0.0;
    public string ProgressText => InitialTotalCount > 0 ? $"Task {Math.Min(ResolvedCount + 1, InitialTotalCount)} of {InitialTotalCount}" : "All clear";

    public ConflictResolutionViewModel(IConflictRepository conflictRepository, ITodoService todoService, ISyncService syncService)
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
        
        InitialTotalCount = list.Count;
        ResolvedCount = 0;

        foreach (var conflict in list)
        {
            Conflicts.Add(new ConflictItemViewModel(conflict));
        }

        SelectedConflict = Conflicts.FirstOrDefault();
        UpdateProgressProperties();
    }

    private void UpdateProgressProperties()
    {
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(ProgressPercentage));
        OnPropertyChanged(nameof(ProgressText));
    }

    [RelayCommand]
    private async Task KeepLocalAsync()
    {
        if (SelectedConflict == null) return;
        var resolvedItem = SelectedConflict.LocalItem;
        resolvedItem.Version = Math.Max(SelectedConflict.LocalItem.Version, SelectedConflict.CloudItem.Version) + 1;
        resolvedItem.UpdatedAt = DateTime.UtcNow;
        var resolvedJson = JsonSerializer.Serialize(resolvedItem);
        await ResolveSingleAsync(SelectedConflict, ConflictResolutionType.KeepLocal, resolvedJson, resolvedItem);
    }

    [RelayCommand]
    private async Task KeepCloudAsync()
    {
        if (SelectedConflict == null) return;
        var resolvedItem = SelectedConflict.CloudItem;
        resolvedItem.Version = Math.Max(SelectedConflict.LocalItem.Version, SelectedConflict.CloudItem.Version) + 1;
        resolvedItem.UpdatedAt = DateTime.UtcNow;
        var resolvedJson = JsonSerializer.Serialize(resolvedItem);
        await ResolveSingleAsync(SelectedConflict, ConflictResolutionType.KeepCloud, resolvedJson, resolvedItem);
    }

    [RelayCommand]
    private async Task ApplySelectionAsync()
    {
        if (SelectedConflict == null) return;
        var resolvedItem = SelectedConflict.BuildMergedItem();
        resolvedItem.Version = Math.Max(SelectedConflict.LocalItem.Version, SelectedConflict.CloudItem.Version) + 1;
        resolvedItem.UpdatedAt = DateTime.UtcNow;
        var resolvedJson = JsonSerializer.Serialize(resolvedItem);
        await ResolveSingleAsync(SelectedConflict, ConflictResolutionType.ManualMerge, resolvedJson, resolvedItem);
    }

    private async Task ResolveSingleAsync(ConflictItemViewModel item, ConflictResolutionType resolutionType, string resolvedJson, TodoItem resolvedItem)
    {
        await _conflictRepository.ResolveConflictAsync(item.Conflict.Id, resolutionType, resolvedJson);

        if (_todoService is SQLiteTodoService sqliteSvc)
        {
            await sqliteSvc.DirectUpsertWithLogAsync(resolvedItem);
        }

        Conflicts.Remove(item);
        ResolvedCount++;

        await _syncService.RefreshConflictCountAsync();

        if (Conflicts.Count > 0)
        {
            SelectedConflict = Conflicts.FirstOrDefault();
            UpdateProgressProperties();
        }
        else
        {
            SelectedConflict = null;
            UpdateProgressProperties();
            OnCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void PromptAcceptAllLocal()
    {
        ConfirmationMessage = $"Are you sure you want to keep versions from This Device for all {Conflicts.Count} remaining task(s)?";
        _pendingBatchAction = async () =>
        {
            var remaining = Conflicts.ToList();
            foreach (var item in remaining)
            {
                var resolvedItem = item.LocalItem;
                resolvedItem.Version = Math.Max(item.LocalItem.Version, item.CloudItem.Version) + 1;
                resolvedItem.UpdatedAt = DateTime.UtcNow;
                var resolvedJson = JsonSerializer.Serialize(resolvedItem);
                await ResolveSingleAsync(item, ConflictResolutionType.KeepLocal, resolvedJson, resolvedItem);
            }
        };
        IsConfirmationVisible = true;
    }

    [RelayCommand]
    private void PromptAcceptAllCloud()
    {
        ConfirmationMessage = $"Are you sure you want to keep versions from Other Device for all {Conflicts.Count} remaining task(s)?";
        _pendingBatchAction = async () =>
        {
            var remaining = Conflicts.ToList();
            foreach (var item in remaining)
            {
                var resolvedItem = item.CloudItem;
                resolvedItem.Version = Math.Max(item.LocalItem.Version, item.CloudItem.Version) + 1;
                resolvedItem.UpdatedAt = DateTime.UtcNow;
                var resolvedJson = JsonSerializer.Serialize(resolvedItem);
                await ResolveSingleAsync(item, ConflictResolutionType.KeepCloud, resolvedJson, resolvedItem);
            }
        };
        IsConfirmationVisible = true;
    }

    [RelayCommand]
    private async Task ConfirmBatchActionAsync()
    {
        IsConfirmationVisible = false;
        if (_pendingBatchAction != null)
        {
            var action = _pendingBatchAction;
            _pendingBatchAction = null;
            action.Invoke();
        }
    }

    [RelayCommand]
    private void CancelBatchAction()
    {
        IsConfirmationVisible = false;
        _pendingBatchAction = null;
    }
}
