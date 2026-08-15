using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Wadd.Services;

namespace Wadd.UI.ViewModels;

public partial class ConflictDiffFieldViewModel : ObservableObject
{
    [ObservableProperty]
    private string _fieldName = string.Empty;

    [ObservableProperty]
    private string _localValue = string.Empty;

    [ObservableProperty]
    private string _cloudValue = string.Empty;

    [ObservableProperty]
    private bool _isDifferent;

    [ObservableProperty]
    private string _selectedValue = string.Empty;

    [ObservableProperty]
    private bool _useLocalValue = true;

    partial void OnUseLocalValueChanged(bool value)
    {
        SelectedValue = value ? LocalValue : CloudValue;
    }
}

public partial class SyncConflictItemViewModel : ObservableObject
{
    public SyncConflict Model { get; }

    public Guid Id => Model.Id;
    public string TableName => Model.TableName;
    public Guid RecordId => Model.RecordId;
    public string DisplayType => TableName switch { "TodoItemEntity" => "Task", "DateNoteEntity" => "Note", _ => "Item" };
    public string DisplayTitle => !string.IsNullOrWhiteSpace(LocalItem?.Title) ? LocalItem.Title : (!string.IsNullOrWhiteSpace(CloudItem?.Title) ? CloudItem.Title : "Task");
    public DateTime LocalUpdatedAt => Model.LocalUpdatedAt;
    public DateTime CloudUpdatedAt => Model.CloudUpdatedAt;
    public string OriginatingDeviceId => Model.OriginatingDeviceId;

    [ObservableProperty]
    private TodoItem? _localItem;

    [ObservableProperty]
    private TodoItem? _cloudItem;

    public ObservableCollection<ConflictDiffFieldViewModel> DiffFields { get; } = new();

    public SyncConflictItemViewModel(SyncConflict model)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        ParseVersions();
    }

    private void ParseVersions()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Model.LocalVersionJson))
                LocalItem = JsonSerializer.Deserialize<TodoItem>(Model.LocalVersionJson);

            if (!string.IsNullOrWhiteSpace(Model.CloudVersionJson))
                CloudItem = JsonSerializer.Deserialize<TodoItem>(Model.CloudVersionJson);

            List<string> conflictingFields = new();
            if (!string.IsNullOrWhiteSpace(Model.ConflictingFieldsJson))
            {
                conflictingFields = JsonSerializer.Deserialize<List<string>>(Model.ConflictingFieldsJson) ?? new();
            }

            if (LocalItem != null && CloudItem != null)
            {
                AddFieldDiff(nameof(TodoItem.Title), LocalItem.Title, CloudItem.Title, conflictingFields);
                AddFieldDiff(nameof(TodoItem.Description), LocalItem.Description, CloudItem.Description, conflictingFields);
                AddFieldDiff(nameof(TodoItem.IsCompleted), LocalItem.IsCompleted ? "Completed" : "Pending", CloudItem.IsCompleted ? "Completed" : "Pending", conflictingFields);
                AddFieldDiff(nameof(TodoItem.Priority), LocalItem.Priority.ToString(), CloudItem.Priority.ToString(), conflictingFields);
                AddFieldDiff(nameof(TodoItem.DueDate), LocalItem.DueDate?.ToString("MMM d, yyyy") ?? "None", CloudItem.DueDate?.ToString("MMM d, yyyy") ?? "None", conflictingFields);
                AddFieldDiff(nameof(TodoItem.ReminderAt), LocalItem.ReminderAt?.ToString("MMM d, yyyy h:mm tt") ?? "None", CloudItem.ReminderAt?.ToString("MMM d, yyyy h:mm tt") ?? "None", conflictingFields);
            }
        }
        catch { }
    }

    private void AddFieldDiff(string name, string localVal, string cloudVal, List<string> conflictingFields)
    {
        bool isDiff = localVal != cloudVal;
        DiffFields.Add(new ConflictDiffFieldViewModel
        {
            FieldName = name,
            LocalValue = localVal,
            CloudValue = cloudVal,
            IsDifferent = isDiff,
            UseLocalValue = true,
            SelectedValue = localVal
        });
    }
}

public partial class ConflictCenterViewModel : ViewModelBase
{
    private readonly IConflictRepository _conflictRepository;
    private readonly ITodoService _todoService;
    private readonly ISyncService _syncService;

    [ObservableProperty]
    private ObservableCollection<SyncConflictItemViewModel> _unresolvedConflicts = new();

    [ObservableProperty]
    private ObservableCollection<SyncConflict> _conflictHistory = new();

    [ObservableProperty]
    private SyncConflictItemViewModel? _selectedConflict;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ConflictCenterViewModel(IConflictRepository conflictRepository, ITodoService todoService, ISyncService syncService)
    {
        _conflictRepository = conflictRepository ?? throw new ArgumentNullException(nameof(conflictRepository));
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));

        _ = LoadConflictsAsync();
    }

    [RelayCommand]
    public async Task LoadConflictsAsync()
    {
        try
        {
            var active = await _conflictRepository.GetUnresolvedConflictsAsync();
            UnresolvedConflicts.Clear();
            foreach (var item in active)
            {
                UnresolvedConflicts.Add(new SyncConflictItemViewModel(item));
            }

            SelectedConflict = UnresolvedConflicts.FirstOrDefault();

            var history = await _conflictRepository.GetConflictHistoryAsync();
            ConflictHistory.Clear();
            foreach (var h in history)
            {
                ConflictHistory.Add(h);
            }

            await _syncService.RefreshConflictCountAsync();
            StatusMessage = UnresolvedConflicts.Count > 0 
                ? $"{UnresolvedConflicts.Count} unresolved conflict(s) require attention."
                : "No unresolved conflicts.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading conflicts: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task KeepLocalVersionAsync(SyncConflictItemViewModel? itemVm)
    {
        itemVm ??= SelectedConflict;
        if (itemVm?.LocalItem == null) return;

        try
        {
            if (_todoService is SQLiteTodoService sqliteSvc)
            {
                await sqliteSvc.DirectUpsertFromSyncAsync(itemVm.LocalItem);
            }
            else
            {
                await _todoService.UpdateTodoAsync(itemVm.LocalItem);
            }

            var json = JsonSerializer.Serialize(itemVm.LocalItem);
            await _conflictRepository.ResolveConflictAsync(itemVm.Id, ConflictResolutionType.KeepLocal, json);

            StatusMessage = "Resolved conflict: Kept device version.";
            await LoadConflictsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to resolve conflict: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task KeepCloudVersionAsync(SyncConflictItemViewModel? itemVm)
    {
        itemVm ??= SelectedConflict;
        if (itemVm?.CloudItem == null) return;

        try
        {
            if (_todoService is SQLiteTodoService sqliteSvc)
            {
                await sqliteSvc.DirectUpsertFromSyncAsync(itemVm.CloudItem);
            }
            else
            {
                await _todoService.UpdateTodoAsync(itemVm.CloudItem);
            }

            var json = JsonSerializer.Serialize(itemVm.CloudItem);
            await _conflictRepository.ResolveConflictAsync(itemVm.Id, ConflictResolutionType.KeepCloud, json);

            StatusMessage = "Resolved conflict: Kept cloud version.";
            await LoadConflictsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to resolve conflict: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ResolveCustomMergeAsync(SyncConflictItemViewModel? itemVm)
    {
        itemVm ??= SelectedConflict;
        if (itemVm?.LocalItem == null || itemVm.CloudItem == null) return;

        try
        {
            var merged = new TodoItem
            {
                Id = itemVm.LocalItem.Id,
                CreatedAt = itemVm.LocalItem.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                Version = Math.Max(itemVm.LocalItem.Version, itemVm.CloudItem.Version) + 1,
                IsDeleted = itemVm.LocalItem.IsDeleted
            };

            foreach (var diff in itemVm.DiffFields)
            {
                switch (diff.FieldName)
                {
                    case nameof(TodoItem.Title):
                        merged.Title = diff.UseLocalValue ? itemVm.LocalItem.Title : itemVm.CloudItem.Title;
                        break;
                    case nameof(TodoItem.Description):
                        merged.Description = diff.UseLocalValue ? itemVm.LocalItem.Description : itemVm.CloudItem.Description;
                        break;
                    case nameof(TodoItem.IsCompleted):
                        merged.IsCompleted = diff.UseLocalValue ? itemVm.LocalItem.IsCompleted : itemVm.CloudItem.IsCompleted;
                        break;
                    case nameof(TodoItem.Priority):
                        merged.Priority = diff.UseLocalValue ? itemVm.LocalItem.Priority : itemVm.CloudItem.Priority;
                        break;
                    case nameof(TodoItem.DueDate):
                        merged.DueDate = diff.UseLocalValue ? itemVm.LocalItem.DueDate : itemVm.CloudItem.DueDate;
                        break;
                    case nameof(TodoItem.ReminderAt):
                        merged.ReminderAt = diff.UseLocalValue ? itemVm.LocalItem.ReminderAt : itemVm.CloudItem.ReminderAt;
                        break;
                }
            }

            if (_todoService is SQLiteTodoService sqliteSvc)
            {
                await sqliteSvc.DirectUpsertFromSyncAsync(merged);
            }
            else
            {
                await _todoService.UpdateTodoAsync(merged);
            }

            var json = JsonSerializer.Serialize(merged);
            await _conflictRepository.ResolveConflictAsync(itemVm.Id, ConflictResolutionType.ManualMerge, json);

            StatusMessage = "Resolved conflict: Applied custom selection.";
            await LoadConflictsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to resolve custom merge: {ex.Message}";
        }
    }
}
