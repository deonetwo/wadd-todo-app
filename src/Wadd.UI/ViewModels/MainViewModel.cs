using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wadd.Core.Enums;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Wadd.Services;

namespace Wadd.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ITodoService _todoService;
    private readonly IThemeService _themeService;
    private readonly ISyncService _syncService;
    private readonly IExportService _exportService;

    [ObservableProperty]
    private string _title = "Wadd - ToDo Application";

    [ObservableProperty]
    private string _statusMessage = "Ready (Local Mode)";

    [ObservableProperty]
    private ThemeMode _currentThemeMode = ThemeMode.System;

    [ObservableProperty]
    private string _currentThemeLabel = "System Default";

    [ObservableProperty]
    private string _newTaskTitle = string.Empty;

    [ObservableProperty]
    private string _newTaskDescription = string.Empty;

    [ObservableProperty]
    private bool _isCompact;

    [ObservableProperty]
    private bool _isSideMenuOpen;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _syncEndpointUrl = string.Empty;

    [ObservableProperty]
    private int _selectedNavIndex = 0;

    public bool IsTasksView => SelectedNavIndex == 0;
    public bool IsCalendarView => SelectedNavIndex == 1;
    public bool IsTracingView => SelectedNavIndex == 2;
    public bool IsSettingsView => SelectedNavIndex == 3;

    partial void OnSelectedNavIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTasksView));
        OnPropertyChanged(nameof(IsCalendarView));
        OnPropertyChanged(nameof(IsTracingView));
        OnPropertyChanged(nameof(IsSettingsView));
    }

    public ObservableCollection<TodoItemViewModel> TodoItems { get; } = new();

    public MainViewModel() : this(new SQLiteTodoService(), new ThemeService(), new SyncService(), new ExcelExportService())
    {
    }

    public MainViewModel(ITodoService todoService, IThemeService themeService, ISyncService syncService, IExportService exportService)
    {
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));

        _currentThemeMode = _themeService.CurrentTheme;
        UpdateThemeLabel();

        if (_syncService is GoogleDriveSyncService driveSync)
        {
            SyncEndpointUrl = driveSync.WebAppUrl;
        }

        _themeService.ThemeChanged += (_, mode) =>
        {
            CurrentThemeMode = mode;
            UpdateThemeLabel();
        };

        _ = LoadTodoItemsAsync();
    }

    [RelayCommand]
    private async Task LoadTodoItemsAsync()
    {
        try
        {
            var itemsList = (await _todoService.GetTodosAsync()).ToList();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TodoItems.Clear();
                foreach (var item in itemsList)
                {
                    TodoItems.Add(new TodoItemViewModel(item));
                }
                StatusMessage = $"Loaded {TodoItems.Count} tasks from local database.";
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Error loading tasks: {ex.Message}";
            });
        }
    }

    [RelayCommand]
    private async Task AddTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTaskTitle)) return;

        try
        {
            var newItem = new TodoItem
            {
                Title = NewTaskTitle.Trim(),
                Description = NewTaskDescription.Trim(),
                IsCompleted = false,
                Priority = TodoPriority.Medium,
                CreatedAt = DateTime.UtcNow
            };

            await _todoService.AddTodoAsync(newItem);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                NewTaskTitle = string.Empty;
                NewTaskDescription = string.Empty;
            });

            await LoadTodoItemsAsync();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Error adding task: {ex.Message}";
            });
        }
    }

    [RelayCommand]
    private async Task ToggleTodoAsync(TodoItemViewModel? itemVm)
    {
        if (itemVm == null) return;
        try
        {
            await _todoService.ToggleCompleteAsync(itemVm.Id);
            await LoadTodoItemsAsync();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Error toggling task: {ex.Message}";
            });
        }
    }

    [RelayCommand]
    private async Task DeleteTodoAsync(TodoItemViewModel? itemVm)
    {
        if (itemVm == null) return;
        try
        {
            await _todoService.DeleteTodoAsync(itemVm.Id);
            await LoadTodoItemsAsync();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Error deleting task: {ex.Message}";
            });
        }
    }

    [RelayCommand]
    private async Task ExportToExcelAsync()
    {
        try
        {
            StatusMessage = "Exporting tasks to Excel...";
            var localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var exportDir = Path.Combine(localFolder, "Wadd", "Exports");
            var fileName = $"todo_export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var filePath = Path.Combine(exportDir, fileName);

            var models = TodoItems.Select(vm => vm.Model).ToList();
            await _exportService.ExportToExcelAsync(models, filePath);

            StatusMessage = $"[Export Success] Exported {models.Count} tasks to: {filePath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (IsSyncing) return;
        try
        {
            IsSyncing = true;
            StatusMessage = "Syncing with Google Apps Script endpoint...";
            var success = await _syncService.SyncAsync();
            if (success)
            {
                StatusMessage = "Sync completed successfully. Local database updated.";
                await LoadTodoItemsAsync();
            }
            else
            {
                StatusMessage = "Sync finished.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    [RelayCommand]
    private Task SyncAsync() => SyncNowAsync();

    [RelayCommand]
    private void OpenSettings()
    {
        SelectedNavIndex = 3;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        if (_syncService is GoogleDriveSyncService driveSync)
        {
            driveSync.WebAppUrl = SyncEndpointUrl.Trim();
        }
        StatusMessage = "Settings saved successfully.";
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        _themeService.ToggleTheme();
    }

    [RelayCommand]
    private void SetThemeMode(string modeName)
    {
        if (Enum.TryParse<ThemeMode>(modeName, true, out var mode))
        {
            _themeService.SetTheme(mode);
        }
    }

    [RelayCommand]
    private void ToggleSideMenu()
    {
        IsSideMenuOpen = !IsSideMenuOpen;
    }

    [RelayCommand]
    private void CloseSideMenu()
    {
        IsSideMenuOpen = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadTodoItemsAsync();
    }

    private void UpdateThemeLabel()
    {
        CurrentThemeLabel = CurrentThemeMode switch
        {
            ThemeMode.Light => "Light Mode",
            ThemeMode.Dark => "Dark Mode",
            _ => "System Default"
        };
    }
}
