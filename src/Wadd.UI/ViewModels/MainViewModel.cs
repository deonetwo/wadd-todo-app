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
    private string _currentDateFormatted = DateTime.Now.ToString("dddd, MMMM d").ToUpperInvariant();

    [ObservableProperty]
    private string _currentDateFull = DateTime.Now.ToString("dddd, MMMM d, yyyy");

    public ObservableCollection<NotificationBubbleItem> StatusNotifications { get; } = new();

    [ObservableProperty]
    private string _statusMessage = "Ready (Local Mode)";

    partial void OnStatusMessageChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "Ready (Local Mode)")
            return;

        var notif = new NotificationBubbleItem(value);
        StatusNotifications.Add(notif);

        Task.Run(async () =>
        {
            await Task.Delay(4000);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                notif.Opacity = 0.0;
            });
            await Task.Delay(450);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusNotifications.Remove(notif);
            });
        });
    }

    [ObservableProperty]
    private ThemeMode _currentThemeMode = ThemeMode.System;

    [ObservableProperty]
    private string _currentThemeLabel = "System Default";

    [ObservableProperty]
    private string _newTaskTitle = string.Empty;

    [ObservableProperty]
    private string _newTaskDescription = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewTaskDueDate))]
    [NotifyPropertyChangedFor(nameof(NewTaskDueDateFormatted))]
    private DateTime? _newTaskDueDate;

    public bool HasNewTaskDueDate => NewTaskDueDate.HasValue;

    public string NewTaskDueDateFormatted
    {
        get
        {
            if (!NewTaskDueDate.HasValue) return string.Empty;
            var date = NewTaskDueDate.Value.Date;
            var today = DateTime.Today;
            if (date == today) return "Today";
            if (date == today.AddDays(1)) return "Tomorrow";
            return NewTaskDueDate.Value.ToString("MMM d, yyyy");
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewTaskReminder))]
    [NotifyPropertyChangedFor(nameof(NewTaskReminderFormatted))]
    private DateTime? _newTaskReminderDate;

    partial void OnNewTaskReminderDateChanged(DateTime? value)
    {
        if (value.HasValue && !NewTaskReminderTime.HasValue)
        {
            NewTaskReminderTime = TimeSpan.Zero;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewTaskReminder))]
    [NotifyPropertyChangedFor(nameof(NewTaskReminderFormatted))]
    private TimeSpan? _newTaskReminderTime;

    public bool HasNewTaskReminder => NewTaskReminderDate.HasValue || NewTaskReminderTime.HasValue;

    public string NewTaskReminderFormatted
    {
        get
        {
            var dt = GetCombinedNewTaskReminder();
            if (!dt.HasValue) return string.Empty;
            var date = dt.Value.Date;
            var today = DateTime.Today;
            var timeStr = dt.Value.ToString("HH:mm");
            if (date == today) return $"Today at {timeStr}";
            if (date == today.AddDays(1)) return $"Tomorrow at {timeStr}";
            return $"{dt.Value:MMM d, yyyy} at {timeStr}";
        }
    }

    private DateTime? GetCombinedNewTaskReminder()
    {
        if (!NewTaskReminderDate.HasValue && !NewTaskReminderTime.HasValue)
            return null;

        var baseDate = NewTaskReminderDate?.Date ?? DateTime.Today;
        var time = NewTaskReminderTime ?? TimeSpan.Zero; // Default is 00:00
        return baseDate.Add(time);
    }

    [ObservableProperty]
    private bool _isCompact;

    [ObservableProperty]
    private bool _isSideMenuOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    private bool _isNavExpanded = false;

    public double SidebarWidth => IsNavExpanded ? 240 : 64;

    [ObservableProperty]
    private bool _isSyncing;

    [ObservableProperty]
    private string _syncEndpointUrl = string.Empty;

    [ObservableProperty]
    private bool _isGoogleSignedIn;

    [ObservableProperty]
    private string _googleUserEmail = string.Empty;

    [ObservableProperty]
    private string _googleUserName = string.Empty;

    [ObservableProperty]
    private string _googleClientId = string.Empty;

    partial void OnGoogleClientIdChanged(string value)
    {
        _syncService.GoogleClientId = value;
    }

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

        UpdateGoogleAuthState();
        _ = LoadTodoItemsAsync();
    }

    private void UpdateGoogleAuthState()
    {
        IsGoogleSignedIn = _syncService.IsSignedIn;
        GoogleUserEmail = _syncService.UserEmail ?? string.Empty;
        GoogleUserName = _syncService.UserName ?? "Google Account User";
        GoogleClientId = _syncService.GoogleClientId ?? string.Empty;
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
    private void SetDueDateLaterToday()
    {
        NewTaskDueDate = DateTime.Today;
    }

    [RelayCommand]
    private void SetDueDateTomorrow()
    {
        NewTaskDueDate = DateTime.Today.AddDays(1);
    }

    [RelayCommand]
    private void SetDueDateNextWeek()
    {
        NewTaskDueDate = DateTime.Today.AddDays(7);
    }

    [RelayCommand]
    private void ClearDueDate()
    {
        NewTaskDueDate = null;
    }

    [RelayCommand]
    private void SetReminderLaterToday()
    {
        NewTaskReminderDate = DateTime.Today;
        NewTaskReminderTime = new TimeSpan(17, 0, 0); // 5:00 PM
    }

    [RelayCommand]
    private void SetReminderTomorrowMorning()
    {
        NewTaskReminderDate = DateTime.Today.AddDays(1);
        NewTaskReminderTime = new TimeSpan(9, 0, 0); // 9:00 AM
    }

    [RelayCommand]
    private void SetReminderNextWeek()
    {
        NewTaskReminderDate = DateTime.Today.AddDays(7);
        NewTaskReminderTime = new TimeSpan(9, 0, 0); // 9:00 AM
    }

    [RelayCommand]
    private void ClearReminder()
    {
        NewTaskReminderDate = null;
        NewTaskReminderTime = null;
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
                CreatedAt = DateTime.UtcNow,
                DueDate = NewTaskDueDate,
                ReminderAt = GetCombinedNewTaskReminder()
            };

            await _todoService.AddTodoAsync(newItem);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                NewTaskTitle = string.Empty;
                NewTaskDescription = string.Empty;
                NewTaskDueDate = null;
                NewTaskReminderDate = null;
                NewTaskReminderTime = null;
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
    private async Task SignInWithGoogleAsync()
    {
        try
        {
            StatusMessage = "Signing in with Google Account...";
            var success = await _syncService.SignInAsync();
            UpdateGoogleAuthState();
            if (success)
            {
                StatusMessage = $"Signed in as {GoogleUserEmail}. Connected to private Google Drive.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sign-in failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SignOutGoogleAsync()
    {
        try
        {
            await _syncService.SignOutAsync();
            UpdateGoogleAuthState();
            StatusMessage = "Signed out of Google. Cloud sync disabled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sign-out failed: {ex.Message}";
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
    private void ToggleNavExpanded()
    {
        IsNavExpanded = !IsNavExpanded;
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

    public bool IsDarkMode
    {
        get
        {
            if (CurrentThemeMode == ThemeMode.Dark) return true;
            if (CurrentThemeMode == ThemeMode.Light) return false;
            return Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        }
    }

    private void UpdateThemeLabel()
    {
        CurrentThemeLabel = CurrentThemeMode switch
        {
            ThemeMode.Light => "Light Mode",
            ThemeMode.Dark => "Dark Mode",
            _ => "System Default"
        };
        OnPropertyChanged(nameof(IsDarkMode));
    }
}

public partial class NotificationBubbleItem : ObservableObject
{
    public string Message { get; }
    public string Timestamp { get; }

    [ObservableProperty]
    private double _opacity = 1.0;

    public NotificationBubbleItem(string message)
    {
        Message = message;
        Timestamp = DateTime.Now.ToString("HH:mm");
    }
}
