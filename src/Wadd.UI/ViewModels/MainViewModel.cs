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

    [ObservableProperty]
    private string _title = "Wadd - ToDo Application";

    [ObservableProperty]
    private string _statusMessage = "Ready";

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
    private int _selectedNavIndex = 0;

    public ObservableCollection<TodoItem> TodoItems { get; } = new();

    public MainViewModel() : this(new SQLiteTodoService(), new ThemeService())
    {
    }

    public MainViewModel(ITodoService todoService, IThemeService themeService)
    {
        _todoService = todoService;
        _themeService = themeService;
        _currentThemeMode = _themeService.CurrentTheme;
        UpdateThemeLabel();

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
                    TodoItems.Add(item);
                }
                StatusMessage = $"Loaded {TodoItems.Count} tasks from SQLite database.";
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
    private async Task ToggleTodoAsync(TodoItem? item)
    {
        if (item == null) return;
        try
        {
            await _todoService.ToggleCompleteAsync(item.Id);
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
    private async Task DeleteTodoAsync(TodoItem? item)
    {
        if (item == null) return;
        try
        {
            await _todoService.DeleteTodoAsync(item.Id);
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
