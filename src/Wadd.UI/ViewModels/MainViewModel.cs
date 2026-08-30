using System.IO;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Wadd.Core.Enums;
using Wadd.Core.Helpers;
using Wadd.Core.Interfaces;
using Wadd.Core.Models;
using Wadd.Services;
using Wadd.UI.Views;

namespace Wadd.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ITodoService _todoService;
    private readonly IThemeService _themeService;
    private readonly ISyncService _syncService;
    private readonly IExportService _exportService;
    private readonly IGoalService _goalService;

    [ObservableProperty]
    private GoalsViewModel _goalsVM;

    private readonly HashSet<Guid> _togglingTaskIds = new();

    private int _periodicSyncTicks;
    private bool _initialSyncCompleted;
    private DispatcherTimer? _autoSyncDebounceTimer;

    [ObservableProperty]
    private string _title = "Wadd - ToDo Application";

    [ObservableProperty]
    private bool _isConflictDialogVisible;

    [ObservableProperty]
    private string _currentDateFormatted = DateTime.Now.ToString("dddd, MMMM d").ToUpperInvariant();

    [ObservableProperty]
    private string _currentDateFull = DateTime.Now.ToString("dddd, MMMM d, yyyy");

    public ObservableCollection<NotificationBubbleItem> StatusNotifications { get; } = new();

    [ObservableProperty]
    private string _statusMessage = "Ready";

    partial void OnStatusMessageChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "Ready")
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
    private string _searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value)
    {
        UpdateSubCollections();
        UpdateSearchResults();
    }

    [ObservableProperty]
    private bool _showNotePreviewsInList = true;

    partial void OnShowNotePreviewsInListChanged(bool value)
    {
        SaveUserSettings();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStandardTasksLayout))]
    [NotifyPropertyChangedFor(nameof(IsFocusTasksLayout))]
    [NotifyPropertyChangedFor(nameof(IsCompletedFirstTasksLayout))]
    [NotifyPropertyChangedFor(nameof(IsTodayCompletedOnlyTasksLayout))]
    [NotifyPropertyChangedFor(nameof(IsTodayUpcomingOnlyTasksLayout))]
    [NotifyPropertyChangedFor(nameof(TodayTasksGridRow))]
    [NotifyPropertyChangedFor(nameof(CompletedTodayGridRow))]
    [NotifyPropertyChangedFor(nameof(UpcomingTasksGridRow))]
    [NotifyPropertyChangedFor(nameof(ShowUpcomingSection))]
    [NotifyPropertyChangedFor(nameof(ShowCompletedTodaySection))]
    [NotifyPropertyChangedFor(nameof(IsCompletedTodaySectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsUpcomingSectionVisible))]
    private string _tasksViewLayout = "Standard";

    partial void OnTasksViewLayoutChanged(string value)
    {
        SaveUserSettings();
    }

    public List<TasksLayoutOption> TasksLayoutOptions { get; } = new()
    {
        new TasksLayoutOption { Id = "Standard", Name = "Standard (Today → Completed → Upcoming)", Description = "Shows Today, Completed Today, and Upcoming tasks" },
        new TasksLayoutOption { Id = "Focus", Name = "Focus (Today → Upcoming → Completed)", Description = "Shows Today, Upcoming, and Completed tasks at bottom" },
        new TasksLayoutOption { Id = "CompletedFirst", Name = "Log First (Completed → Today → Upcoming)", Description = "Shows Completed Today at top, then Today and Upcoming" },
        new TasksLayoutOption { Id = "TodayCompletedOnly", Name = "Today & Completed Only", Description = "Shows Today Tasks and Completed Today (Hides Upcoming)" },
        new TasksLayoutOption { Id = "TodayUpcomingOnly", Name = "Today & Upcoming Only", Description = "Shows Today Tasks and Upcoming Tasks (Hides Completed)" }
    };

    [ObservableProperty]
    private TasksLayoutOption? _selectedTasksLayoutOption;

    [ObservableProperty]
    private bool _isTasksLayoutPickerSheetOpen;

    [RelayCommand]
    private void OpenTasksLayoutPicker()
    {
        IsTasksLayoutPickerSheetOpen = true;
    }

    [RelayCommand]
    private void CloseTasksLayoutPicker()
    {
        IsTasksLayoutPickerSheetOpen = false;
    }

    [RelayCommand]
    private void SelectTasksLayoutOption(TasksLayoutOption? option)
    {
        if (option != null)
        {
            SelectedTasksLayoutOption = option;
            IsTasksLayoutPickerSheetOpen = false;
        }
    }

    partial void OnSelectedTasksLayoutOptionChanged(TasksLayoutOption? value)
    {
        if (value != null && !string.IsNullOrWhiteSpace(value.Id))
        {
            TasksViewLayout = value.Id;
        }
    }

    public bool IsStandardTasksLayout => TasksViewLayout == "Standard";
    public bool IsFocusTasksLayout => TasksViewLayout == "Focus";
    public bool IsCompletedFirstTasksLayout => TasksViewLayout == "CompletedFirst";
    public bool IsTodayCompletedOnlyTasksLayout => TasksViewLayout == "TodayCompletedOnly";
    public bool IsTodayUpcomingOnlyTasksLayout => TasksViewLayout == "TodayUpcomingOnly";

    public int TodayTasksGridRow => TasksViewLayout switch
    {
        "CompletedFirst" => 1,
        _ => 0
    };

    public int CompletedTodayGridRow => TasksViewLayout switch
    {
        "Focus" => 2,
        "CompletedFirst" => 0,
        _ => 1
    };

    public int UpcomingTasksGridRow => TasksViewLayout switch
    {
        "Focus" => 1,
        "CompletedFirst" => 2,
        "TodayUpcomingOnly" => 1,
        _ => 2
    };

    public bool ShowUpcomingSection => TasksViewLayout != "TodayCompletedOnly";
    public bool ShowCompletedTodaySection => TasksViewLayout != "TodayUpcomingOnly";

    public bool IsCompletedTodaySectionVisible => HasCompletedTodayTodoItems && ShowCompletedTodaySection;
    public bool IsUpcomingSectionVisible => HasUpcomingTodoItems && ShowUpcomingSection;

    [RelayCommand]
    private void SetTasksViewLayout(string layout)
    {
        if (!string.IsNullOrWhiteSpace(layout))
        {
            TasksViewLayout = layout;
            SelectedTasksLayoutOption = TasksLayoutOptions.FirstOrDefault(x => x.Id == layout) ?? TasksLayoutOptions[0];
        }
    }

    [ObservableProperty]
    private bool _isNoteComposerExpanded;

    [RelayCommand]
    private void ToggleNoteComposer()
    {
        IsNoteComposerExpanded = !IsNoteComposerExpanded;
    }

    [RelayCommand]
    private void ExpandNoteComposer()
    {
        IsNoteComposerExpanded = true;
    }

    [ObservableProperty]
    private TodoItemViewModel? _selectedDetailTask;

    partial void OnSelectedDetailTaskChanged(TodoItemViewModel? oldValue, TodoItemViewModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnSelectedDetailTaskPropertyChanged;
        }
        if (newValue != null)
        {
            newValue.PropertyChanged += OnSelectedDetailTaskPropertyChanged;
        }
    }

    private async void OnSelectedDetailTaskPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (SelectedDetailTask != null && (e.PropertyName == nameof(TodoItemViewModel.Description) || e.PropertyName == nameof(TodoItemViewModel.Title)))
        {
            await _todoService.UpdateTodoAsync(SelectedDetailTask.Model);
            RequestDebouncedAutoSync();
        }
    }

    [ObservableProperty]
    private bool _isDetailDrawerOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotePreviewMode))]
    private bool _isNoteEditMode = true;

    public bool IsNotePreviewMode
    {
        get => !IsNoteEditMode;
        set => IsNoteEditMode = !value;
    }

    [RelayCommand]
    private void OpenDetailDrawer(TodoItemViewModel? item)
    {
        if (item == null) return;
        IsNoteEditMode = true;
        SelectedDetailTask = item;
        IsDetailDrawerOpen = true;
    }

    [RelayCommand]
    private async Task CloseDetailDrawerAsync()
    {
        if (SelectedDetailTask != null)
        {
            await _todoService.UpdateTodoAsync(SelectedDetailTask.Model);
            RequestDebouncedAutoSync();
        }
        IsDetailDrawerOpen = false;
        SelectedDetailTask = null;
    }

    private void CloseDetailDrawer() => _ = CloseDetailDrawerAsync();

    [RelayCommand]
    private async Task SaveDetailTaskAsync()
    {
        if (SelectedDetailTask == null) return;
        await _todoService.UpdateTodoAsync(SelectedDetailTask.Model);
        RequestDebouncedAutoSync();
        UpdateSubCollections();
    }

    [ObservableProperty]
    private string _newTaskTitle = string.Empty;

    [ObservableProperty]
    private string _newTaskDescription = string.Empty;

    [ObservableProperty]
    private string _newTaskCategoryInput = string.Empty;

    public ObservableCollection<string> NewTaskCategories { get; } = new();

    public bool HasNewTaskCategories => NewTaskCategories.Count > 0;

    [RelayCommand]
    private void AddNewTaskCategory(string? category)
    {
        var tag = string.IsNullOrWhiteSpace(category) ? NewTaskCategoryInput : category;
        if (string.IsNullOrWhiteSpace(tag)) return;

        tag = tag.Trim();
        if (!NewTaskCategories.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            NewTaskCategories.Add(tag);
            NewTaskCategoryInput = string.Empty;
            OnPropertyChanged(nameof(HasNewTaskCategories));
        }
        IsMobileCategorySheetOpen = false;
    }

    [RelayCommand]
    private void RemoveNewTaskCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return;
        var existing = NewTaskCategories.FirstOrDefault(x => x.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            NewTaskCategories.Remove(existing);
            OnPropertyChanged(nameof(HasNewTaskCategories));
        }
    }

    [RelayCommand]
    private void ClearNewTaskCategories()
    {
        NewTaskCategories.Clear();
        NewTaskCategoryInput = string.Empty;
        OnPropertyChanged(nameof(HasNewTaskCategories));
    }

    private bool _isUpdatingCategoryFilters;

    public ObservableCollection<CategoryFilterOption> CategoryFilterOptions { get; } = new();

    public ObservableCollection<string> ExistingCategories { get; } = new();
    public bool HasExistingCategories => ExistingCategories.Count > 0;

    public string CategoryFilterButtonText
    {
        get
        {
            var selected = CategoryFilterOptions.Where(o => o.IsSelected).Select(o => o.Name).ToList();
            if (selected.Count == 0 || selected.Contains("All Categories", StringComparer.OrdinalIgnoreCase))
            {
                return "Tags";
            }

            if (selected.Count == 1)
            {
                return selected[0];
            }

            var joined = string.Join(", ", selected);
            return joined.Length > 18 ? $"{selected.Count} Tags" : joined;
        }
    }

    public ObservableCollection<TagItemViewModel> AllTags { get; } = new();

    private readonly List<string> _customEmptyTags = new();
    private readonly Dictionary<string, DateTime> _tagCreatedTimes = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _newGlobalTagInput = string.Empty;

    public void UpdateAvailableCategories()
    {
        var activeCategories = TodoItems
            .SelectMany(x => x.Categories)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var combinedActiveAndCustom = activeCategories
            .Concat(_customEmptyTags.Where(c => !activeCategories.Contains(c, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        DateTime GetLatestTime(string cat)
        {
            var taskLatest = TodoItems
                .Where(x => x.Categories.Contains(cat, StringComparer.OrdinalIgnoreCase))
                .Select(x => x.CreatedAt)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            var tagLatest = _tagCreatedTimes.TryGetValue(cat, out var dt) ? dt : DateTime.MinValue;
            return taskLatest > tagLatest ? taskLatest : tagLatest;
        }

        var sortedCategories = combinedActiveAndCustom
            .OrderByDescending(cat => GetLatestTime(cat))
            .ThenBy(cat => cat)
            .ToList();

        ExistingCategories.Clear();
        foreach (var cat in sortedCategories)
        {
            ExistingCategories.Add(cat);
        }
        OnPropertyChanged(nameof(HasExistingCategories));

        AllTags.Clear();
        foreach (var cat in sortedCategories)
        {
            int count = TodoItems.Count(x => x.Categories.Contains(cat, StringComparer.OrdinalIgnoreCase));
            AllTags.Add(new TagItemViewModel(cat, count, RenameGlobalTagAsync, DeleteGlobalTagAsync));
        }

        var defaults = new List<string> { "All Categories", "Uncategorized" };
        var combinedNames = defaults
            .Concat(sortedCategories.Where(c => !defaults.Contains(c, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        _isUpdatingCategoryFilters = true;
        try
        {
            var existingMap = CategoryFilterOptions.ToDictionary(x => x.Name, x => x.IsSelected, StringComparer.OrdinalIgnoreCase);

            CategoryFilterOptions.Clear();
            foreach (var name in combinedNames)
            {
                bool isSel = existingMap.TryGetValue(name, out var wasSel) ? wasSel : name.Equals("All Categories", StringComparison.OrdinalIgnoreCase);
                var opt = new CategoryFilterOption(name, isSel)
                {
                    OnSelectionChanged = OnCategoryOptionSelectionChanged
                };
                CategoryFilterOptions.Add(opt);
            }

            if (!CategoryFilterOptions.Any(o => o.IsSelected))
            {
                var allOpt = CategoryFilterOptions.FirstOrDefault(o => o.Name.Equals("All Categories", StringComparison.OrdinalIgnoreCase));
                if (allOpt != null) allOpt.IsSelected = true;
            }
        }
        finally
        {
            _isUpdatingCategoryFilters = false;
        }

        OnPropertyChanged(nameof(CategoryFilterButtonText));
    }

    private void OnCategoryOptionSelectionChanged(CategoryFilterOption changedOption)
    {
        if (_isUpdatingCategoryFilters) return;

        _isUpdatingCategoryFilters = true;
        try
        {
            if (changedOption.Name.Equals("All Categories", StringComparison.OrdinalIgnoreCase))
            {
                if (changedOption.IsSelected)
                {
                    foreach (var opt in CategoryFilterOptions.Where(o => !o.Name.Equals("All Categories", StringComparison.OrdinalIgnoreCase)))
                    {
                        opt.IsSelected = false;
                    }
                }
                else
                {
                    if (!CategoryFilterOptions.Any(o => o.IsSelected))
                    {
                        changedOption.IsSelected = true;
                    }
                }
            }
            else
            {
                if (changedOption.IsSelected)
                {
                    var allOpt = CategoryFilterOptions.FirstOrDefault(o => o.Name.Equals("All Categories", StringComparison.OrdinalIgnoreCase));
                    if (allOpt != null) allOpt.IsSelected = false;
                }
                else
                {
                    if (!CategoryFilterOptions.Any(o => o.IsSelected))
                    {
                        var allOpt = CategoryFilterOptions.FirstOrDefault(o => o.Name.Equals("All Categories", StringComparison.OrdinalIgnoreCase));
                        if (allOpt != null) allOpt.IsSelected = true;
                    }
                }
            }
        }
        finally
        {
            _isUpdatingCategoryFilters = false;
        }

        OnPropertyChanged(nameof(CategoryFilterButtonText));
        UpdateSubCollections();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewTaskDueDate))]
    [NotifyPropertyChangedFor(nameof(NewTaskDueDateFormatted))]
    [NotifyPropertyChangedFor(nameof(DueDateHeaderYear))]
    [NotifyPropertyChangedFor(nameof(DueDateHeaderMainText))]
    private DateTime? _newTaskDueDate;

    public DateTime MinDueDate => DateTime.Today;

    public TimeSpan GetSuggestedLaterTodayTime()
    {
        var now = DateTime.Now;
        if (now.Hour < 17)
        {
            return new TimeSpan(17, 0, 0);
        }
        else if (now.Hour < 20)
        {
            return new TimeSpan(20, 0, 0);
        }
        else if (now.Hour < 22)
        {
            return new TimeSpan(22, 0, 0);
        }
        else
        {
            return new TimeSpan(23, 59, 0);
        }
    }

    public string ReminderLaterTodayText
    {
        get
        {
            var time = GetSuggestedLaterTodayTime();
            return $"Later Today ({time:hh\\:mm})";
        }
    }

    public bool IsReminderLaterTodayEnabled
    {
        get
        {
            var time = GetSuggestedLaterTodayTime();
            var preset = DateTime.Today.Add(time);
            if (preset <= DateTime.Now) return false;

            if (!NewTaskDueDate.HasValue) return true;
            var maxAllowed = NewTaskDueDate.Value.TimeOfDay == TimeSpan.Zero
                ? NewTaskDueDate.Value.Date.AddDays(1).AddTicks(-1)
                : NewTaskDueDate.Value;
            return preset <= maxAllowed;
        }
    }

    public bool IsReminderTomorrowMorningEnabled
    {
        get
        {
            var preset = DateTime.Today.AddDays(1).AddHours(9);
            if (preset <= DateTime.Now) return false;

            if (!NewTaskDueDate.HasValue) return true;
            var maxAllowed = NewTaskDueDate.Value.TimeOfDay == TimeSpan.Zero
                ? NewTaskDueDate.Value.Date.AddDays(1).AddTicks(-1)
                : NewTaskDueDate.Value;
            return preset <= maxAllowed;
        }
    }

    public bool IsReminderNextWeekEnabled
    {
        get
        {
            var preset = DateTime.Today.AddDays(7).AddHours(9);
            if (preset <= DateTime.Now) return false;

            if (!NewTaskDueDate.HasValue) return true;
            var maxAllowed = NewTaskDueDate.Value.TimeOfDay == TimeSpan.Zero
                ? NewTaskDueDate.Value.Date.AddDays(1).AddTicks(-1)
                : NewTaskDueDate.Value;
            return preset <= maxAllowed;
        }
    }

    partial void OnNewTaskDueDateChanged(DateTime? value)
    {
        if (value.HasValue && value.Value.Date < DateTime.Today)
        {
            NewTaskDueDate = DateTime.Today;
            StatusMessage = "Due date cannot be set in the past.";
            return;
        }
        ValidateAndResetReminderIfExceedsDueDate();
        OnPropertyChanged(nameof(ReminderLaterTodayText));
        OnPropertyChanged(nameof(IsReminderLaterTodayEnabled));
        OnPropertyChanged(nameof(IsReminderTomorrowMorningEnabled));
        OnPropertyChanged(nameof(IsReminderNextWeekEnabled));
    }

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
    [NotifyPropertyChangedFor(nameof(ReminderHeaderYear))]
    [NotifyPropertyChangedFor(nameof(ReminderHeaderMainText))]
    private DateTime? _newTaskReminderDate;

    partial void OnNewTaskReminderDateChanged(DateTime? value)
    {
        if (value.HasValue && !NewTaskReminderTime.HasValue)
        {
            NewTaskReminderTime = TimeSpan.Zero;
        }
        ValidateAndResetReminderIfExceedsDueDate();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewTaskReminder))]
    [NotifyPropertyChangedFor(nameof(NewTaskReminderFormatted))]
    [NotifyPropertyChangedFor(nameof(ReminderHeaderMainText))]
    private TimeSpan? _newTaskReminderTime;

    partial void OnNewTaskReminderTimeChanged(TimeSpan? value)
    {
        ValidateAndResetReminderIfExceedsDueDate();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReminderDateTabSelected))]
    [NotifyPropertyChangedFor(nameof(IsReminderTimeTabSelected))]
    [NotifyPropertyChangedFor(nameof(ReminderHeaderMainText))]
    private int _reminderSelectedTab = 0;

    public bool IsReminderDateTabSelected => ReminderSelectedTab == 0;
    public bool IsReminderTimeTabSelected => ReminderSelectedTab == 1;

    [RelayCommand]
    private void SelectReminderDateTab() => ReminderSelectedTab = 0;

    [RelayCommand]
    private void SelectReminderTimeTab() => ReminderSelectedTab = 1;

    public string ReminderHeaderYear => (NewTaskReminderDate ?? DateTime.Today).ToString("yyyy");

    public string ReminderHeaderMainText
    {
        get
        {
            var dateStr = (NewTaskReminderDate ?? DateTime.Today).ToString("ddd, MMM d");
            if (IsReminderTimeTabSelected && NewTaskReminderTime.HasValue)
            {
                var timeStr = DateTime.Today.Add(NewTaskReminderTime.Value).ToString("HH:mm");
                return $"{dateStr}, {timeStr}";
            }
            return dateStr;
        }
    }

    public string DueDateHeaderYear => (NewTaskDueDate ?? DateTime.Today).ToString("yyyy");
    public string DueDateHeaderMainText => (NewTaskDueDate ?? DateTime.Today).ToString("ddd, MMM d");

    [RelayCommand]
    private void SetReminderPresetMorning()
    {
        if (!NewTaskReminderDate.HasValue) NewTaskReminderDate = DateTime.Today;
        NewTaskReminderTime = new TimeSpan(9, 0, 0);
    }

    [RelayCommand]
    private void SetReminderPresetAfternoon()
    {
        if (!NewTaskReminderDate.HasValue) NewTaskReminderDate = DateTime.Today;
        NewTaskReminderTime = new TimeSpan(13, 0, 0);
    }

    [RelayCommand]
    private void SetReminderPresetEvening()
    {
        if (!NewTaskReminderDate.HasValue) NewTaskReminderDate = DateTime.Today;
        NewTaskReminderTime = new TimeSpan(17, 0, 0);
    }

    [RelayCommand]
    private void SetReminderPresetNight()
    {
        if (!NewTaskReminderDate.HasValue) NewTaskReminderDate = DateTime.Today;
        NewTaskReminderTime = new TimeSpan(20, 0, 0);
    }

    private bool _isValidatingReminder;

    private void ValidateAndResetReminderIfExceedsDueDate()
    {
        if (_isValidatingReminder) return;
        if (!NewTaskDueDate.HasValue) return;

        var reminderDt = GetCombinedNewTaskReminder();
        if (!reminderDt.HasValue) return;

        DateTime maxReminderAllowed = NewTaskDueDate.Value.TimeOfDay == TimeSpan.Zero
            ? NewTaskDueDate.Value.Date.AddDays(1).AddTicks(-1)
            : NewTaskDueDate.Value;

        if (reminderDt.Value > maxReminderAllowed)
        {
            _isValidatingReminder = true;
            try
            {
                NewTaskReminderDate = null;
                NewTaskReminderTime = null;
                StatusMessage = "Reminder reset because it exceeded the due date.";
            }
            finally
            {
                _isValidatingReminder = false;
            }
        }
    }

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
    [NotifyPropertyChangedFor(nameof(IsRepeatEnabled))]
    [NotifyPropertyChangedFor(nameof(IsCustomRecurrenceVisible))]
    [NotifyPropertyChangedFor(nameof(IsWeeklyDaysPickerVisible))]
    [NotifyPropertyChangedFor(nameof(HasNewTaskRecurrence))]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private string _selectedRecurrenceType = "None";

    public bool IsRepeatEnabled => !string.IsNullOrWhiteSpace(SelectedRecurrenceType) && !SelectedRecurrenceType.Equals("None", StringComparison.OrdinalIgnoreCase);

    public List<string> RecurrenceOptions { get; } = new() { "None", "Daily", "Weekdays", "Weekly", "Monthly", "Yearly", "Custom" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private int _customInterval = 1;

    partial void OnCustomIntervalChanged(int value)
    {
        if (value < 1) CustomInterval = 1;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWeeklyDaysPickerVisible))]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private string _selectedCustomUnit = "Days";

    public List<string> CustomUnitOptions { get; } = new() { "Days", "Weeks", "Months", "Years" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private bool _isMondaySelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private bool _isTuesdaySelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private bool _isWednesdaySelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private bool _isThursdaySelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private bool _isFridaySelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private bool _isSaturdaySelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTaskRecurrenceFormatted))]
    private bool _isSundaySelected;

    public bool IsCustomRecurrenceVisible => IsRepeatEnabled && SelectedRecurrenceType == "Custom";

    public bool IsWeeklyDaysPickerVisible => IsCustomRecurrenceVisible && SelectedCustomUnit == "Weeks";

    public bool HasNewTaskRecurrence => IsRepeatEnabled;

    public string NewTaskRecurrenceFormatted
    {
        get
        {
            if (!IsRepeatEnabled) return string.Empty;
            return RecurrenceHelper.FormatRecurrenceText(
                IsRepeatEnabled,
                SelectedRecurrenceType,
                CustomInterval,
                SelectedCustomUnit,
                GetSelectedWeeklyDaysString());
        }
    }

    private string GetSelectedWeeklyDaysString()
    {
        var days = new List<string>();
        if (IsMondaySelected) days.Add("Monday");
        if (IsTuesdaySelected) days.Add("Tuesday");
        if (IsWednesdaySelected) days.Add("Wednesday");
        if (IsThursdaySelected) days.Add("Thursday");
        if (IsFridaySelected) days.Add("Friday");
        if (IsSaturdaySelected) days.Add("Saturday");
        if (IsSundaySelected) days.Add("Sunday");
        return string.Join(",", days);
    }

    [RelayCommand]
    private void SetRecurrenceType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return;
        SelectedRecurrenceType = type;
        if (type != "Custom")
        {
            CloseMobileRepeatSheet();
        }
    }

    [RelayCommand]
    private void ClearRecurrence()
    {
        SelectedRecurrenceType = "None";
        CustomInterval = 1;
        SelectedCustomUnit = "Days";
        IsMondaySelected = false;
        IsTuesdaySelected = false;
        IsWednesdaySelected = false;
        IsThursdaySelected = false;
        IsFridaySelected = false;
        IsSaturdaySelected = false;
        IsSundaySelected = false;
    }

    [RelayCommand]
    private void OpenMobileTaskComposer()
    {
        IsMobileTaskComposerOpen = true;
    }

    [RelayCommand]
    private void CloseMobileTaskComposer()
    {
        IsMobileTaskComposerOpen = false;
        IsMobileDueDateSheetOpen = false;
        IsMobileReminderSheetOpen = false;
        IsMobileRepeatSheetOpen = false;
        IsMobileCategorySheetOpen = false;
    }

    [RelayCommand]
    private void OpenMobileDueDateSheet()
    {
        IsMobileDueDateSheetOpen = true;
    }

    [RelayCommand]
    private void CloseMobileDueDateSheet()
    {
        IsMobileDueDateSheetOpen = false;
    }

    [RelayCommand]
    private void OpenMobileReminderSheet()
    {
        IsMobileReminderSheetOpen = true;
    }

    [RelayCommand]
    private void CloseMobileReminderSheet()
    {
        IsMobileReminderSheetOpen = false;
    }

    [RelayCommand]
    private void OpenMobileRepeatSheet()
    {
        IsMobileRepeatSheetOpen = true;
    }

    [RelayCommand]
    private void CloseMobileRepeatSheet()
    {
        IsMobileRepeatSheetOpen = false;
    }

    [RelayCommand]
    private void OpenMobileCategorySheet()
    {
        IsMobileCategorySheetOpen = true;
    }

    [RelayCommand]
    private void CloseMobileCategorySheet()
    {
        IsMobileCategorySheetOpen = false;
    }

    [RelayCommand]
    private void OpenMobileTagFilterSheet()
    {
        IsMobileTagFilterSheetOpen = true;
    }

    [RelayCommand]
    private void CloseMobileTagFilterSheet()
    {
        IsMobileTagFilterSheetOpen = false;
    }

    [RelayCommand]
    private void ClearTagFilter()
    {
        var allOpt = CategoryFilterOptions.FirstOrDefault(o => o.Name.Equals("All Categories", StringComparison.OrdinalIgnoreCase));
        foreach (var opt in CategoryFilterOptions)
        {
            opt.IsSelected = (opt == allOpt);
        }
    }

    [ObservableProperty]
    private string _completedDateFilterPreset = "All";

    [ObservableProperty]
    private DateTime? _completedDateFilterStartDate;

    [ObservableProperty]
    private DateTime? _completedDateFilterEndDate;

    [ObservableProperty]
    private bool _isMobileCompletedDateFilterSheetOpen;

    partial void OnCompletedDateFilterPresetChanged(string value)
    {
        ApplyCompletedDatePreset(value);
        NotifyCompletedDateFilterProperties();
        UpdateSubCollections();
    }

    partial void OnCompletedDateFilterStartDateChanged(DateTime? value)
    {
        if (value.HasValue && CompletedDateFilterEndDate.HasValue && value.Value.Date > CompletedDateFilterEndDate.Value.Date)
        {
            CompletedDateFilterEndDate = value.Value.Date;
        }

        if (CompletedDateFilterPreset != "Custom" && value.HasValue)
        {
            _completedDateFilterPreset = "Custom";
            OnPropertyChanged(nameof(CompletedDateFilterPreset));
        }
        NotifyCompletedDateFilterProperties();
        UpdateSubCollections();
    }

    partial void OnCompletedDateFilterEndDateChanged(DateTime? value)
    {
        if (value.HasValue && CompletedDateFilterStartDate.HasValue && value.Value.Date < CompletedDateFilterStartDate.Value.Date)
        {
            CompletedDateFilterStartDate = value.Value.Date;
        }

        if (CompletedDateFilterPreset != "Custom" && value.HasValue)
        {
            _completedDateFilterPreset = "Custom";
            OnPropertyChanged(nameof(CompletedDateFilterPreset));
        }
        NotifyCompletedDateFilterProperties();
        UpdateSubCollections();
    }

    [RelayCommand]
    private void SetCompletedStartDate(string? option)
    {
        var today = DateTime.Today;
        switch (option)
        {
            case "Today":
                CompletedDateFilterStartDate = today;
                break;
            case "7DaysAgo":
                CompletedDateFilterStartDate = today.AddDays(-6);
                break;
            case "30DaysAgo":
                CompletedDateFilterStartDate = today.AddDays(-29);
                break;
            case "FirstOfMonth":
                CompletedDateFilterStartDate = new DateTime(today.Year, today.Month, 1);
                break;
        }
    }

    [RelayCommand]
    private void SetCompletedEndDate(string? option)
    {
        var today = DateTime.Today;
        switch (option)
        {
            case "Today":
                CompletedDateFilterEndDate = today;
                break;
            case "Yesterday":
                CompletedDateFilterEndDate = today.AddDays(-1);
                break;
        }
    }

    [ObservableProperty]
    private string _selectedCompletedDateTab = "Start";

    public bool IsCompletedDateStartTabActive => SelectedCompletedDateTab == "Start";

    [RelayCommand]
    private void SelectCompletedDateTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab)) return;
        SelectedCompletedDateTab = tab;
        OnPropertyChanged(nameof(IsCompletedDateStartTabActive));
    }

    public string CompletedDateHeaderYear => (CompletedDateFilterStartDate ?? DateTime.Today).ToString("yyyy");
    public string CompletedDateHeaderMainText => CompletedDateFilterButtonText;

    public string CompletedDateFilterStartDateFormatted => CompletedDateFilterStartDate.HasValue ? CompletedDateFilterStartDate.Value.ToString("ddd, MMM d") : "Any Date";
    public string CompletedDateFilterEndDateFormatted => CompletedDateFilterEndDate.HasValue ? CompletedDateFilterEndDate.Value.ToString("ddd, MMM d") : "Any Date";

    private void NotifyCompletedDateFilterProperties()
    {
        OnPropertyChanged(nameof(CompletedDateFilterButtonText));
        OnPropertyChanged(nameof(CompletedDateHeaderYear));
        OnPropertyChanged(nameof(CompletedDateHeaderMainText));
        OnPropertyChanged(nameof(CompletedDateFilterStartDateFormatted));
        OnPropertyChanged(nameof(CompletedDateFilterEndDateFormatted));
        OnPropertyChanged(nameof(IsCompletedDateFilterActive));
        OnPropertyChanged(nameof(IsAnyCompletedFilterActive));
        OnPropertyChanged(nameof(CompletedDateFilterStartDateOffset));
        OnPropertyChanged(nameof(CompletedDateFilterEndDateOffset));
    }

    public DateTimeOffset? CompletedDateFilterStartDateOffset
    {
        get => CompletedDateFilterStartDate.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(CompletedDateFilterStartDate.Value, DateTimeKind.Local)) : null;
        set
        {
            if (value.HasValue)
            {
                CompletedDateFilterStartDate = value.Value.LocalDateTime.Date;
            }
            else
            {
                CompletedDateFilterStartDate = null;
            }
        }
    }

    public DateTimeOffset? CompletedDateFilterEndDateOffset
    {
        get => CompletedDateFilterEndDate.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(CompletedDateFilterEndDate.Value, DateTimeKind.Local)) : null;
        set
        {
            if (value.HasValue)
            {
                CompletedDateFilterEndDate = value.Value.LocalDateTime.Date;
            }
            else
            {
                CompletedDateFilterEndDate = null;
            }
        }
    }

    private void ApplyCompletedDatePreset(string preset)
    {
        var today = DateTime.Today;
        switch (preset)
        {
            case "Today":
                CompletedDateFilterStartDate = today;
                CompletedDateFilterEndDate = today;
                break;
            case "Yesterday":
                CompletedDateFilterStartDate = today.AddDays(-1);
                CompletedDateFilterEndDate = today.AddDays(-1);
                break;
            case "7Days":
                CompletedDateFilterStartDate = today.AddDays(-6);
                CompletedDateFilterEndDate = today;
                break;
            case "30Days":
                CompletedDateFilterStartDate = today.AddDays(-29);
                CompletedDateFilterEndDate = today;
                break;
            case "ThisMonth":
                CompletedDateFilterStartDate = new DateTime(today.Year, today.Month, 1);
                CompletedDateFilterEndDate = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
                break;
            case "Custom":
                break;
            case "All":
            default:
                CompletedDateFilterStartDate = null;
                CompletedDateFilterEndDate = null;
                break;
        }
    }

    public bool IsCompletedDateFilterActive => CompletedDateFilterPreset != "All" || CompletedDateFilterStartDate.HasValue || CompletedDateFilterEndDate.HasValue;

    public bool IsAnyCompletedFilterActive => IsCompletedDateFilterActive || (CategoryFilterOptions != null && CategoryFilterOptions.Any(o => o.IsSelected && !o.Name.Equals("All Categories", StringComparison.OrdinalIgnoreCase)));

    public string CompletedDateFilterButtonText
    {
        get
        {
            return CompletedDateFilterPreset switch
            {
                "Today" => "Completed: Today",
                "Yesterday" => "Completed: Yesterday",
                "7Days" => "Last 7 Days",
                "30Days" => "Last 30 Days",
                "ThisMonth" => "This Month",
                "Custom" => GetCustomDateFilterText(),
                _ => "All Time"
            };
        }
    }

    private string GetCustomDateFilterText()
    {
        if (CompletedDateFilterStartDate.HasValue && CompletedDateFilterEndDate.HasValue)
        {
            if (CompletedDateFilterStartDate.Value.Date == CompletedDateFilterEndDate.Value.Date)
            {
                return $"{CompletedDateFilterStartDate.Value:MMM d, yyyy}";
            }
            return $"{CompletedDateFilterStartDate.Value:MMM d} - {CompletedDateFilterEndDate.Value:MMM d}";
        }
        if (CompletedDateFilterStartDate.HasValue)
        {
            return $"From {CompletedDateFilterStartDate.Value:MMM d}";
        }
        if (CompletedDateFilterEndDate.HasValue)
        {
            return $"Until {CompletedDateFilterEndDate.Value:MMM d}";
        }
        return "Custom Range";
    }

    [RelayCommand]
    private void SetCompletedDateFilterPreset(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset)) return;
        CompletedDateFilterPreset = preset;
    }

    [RelayCommand]
    private void ClearCompletedDateFilter()
    {
        CompletedDateFilterPreset = "All";
    }

    [RelayCommand]
    private void ClearAllCompletedFilters()
    {
        ClearCompletedDateFilter();
        ClearTagFilter();
    }

    [RelayCommand]
    private void OpenMobileCompletedDateFilterSheet()
    {
        IsMobileCompletedDateFilterSheetOpen = true;
    }

    [RelayCommand]
    private void CloseMobileCompletedDateFilterSheet()
    {
        IsMobileCompletedDateFilterSheetOpen = false;
    }

    private bool PassesCompletedDateFilter(TodoItemViewModel item)
    {
        if (CompletedDateFilterPreset == "All" && !CompletedDateFilterStartDate.HasValue && !CompletedDateFilterEndDate.HasValue)
        {
            return true;
        }

        if (!item.CompletedAt.HasValue)
        {
            return false;
        }

        var localCompletedDate = item.CompletedAt.Value.ToLocalTime().Date;

        if (CompletedDateFilterStartDate.HasValue && localCompletedDate < CompletedDateFilterStartDate.Value.Date)
        {
            return false;
        }

        if (CompletedDateFilterEndDate.HasValue && localCompletedDate > CompletedDateFilterEndDate.Value.Date)
        {
            return false;
        }

        return true;
    }

    [ObservableProperty]
    private bool _isCompact;

    partial void OnIsCompactChanged(bool value)
    {
        if (GoalsVM != null)
        {
            GoalsVM.IsCompact = value;
        }
    }

    [ObservableProperty]
    private bool _isSideMenuOpen;

    [ObservableProperty]
    private bool _isMobileTaskComposerOpen;

    [ObservableProperty]
    private bool _isMobileDueDateSheetOpen;

    [ObservableProperty]
    private bool _isMobileReminderSheetOpen;

    [ObservableProperty]
    private bool _isMobileRepeatSheetOpen;

    [ObservableProperty]
    private bool _isMobileCategorySheetOpen;

    [ObservableProperty]
    private bool _isMobileTagFilterSheetOpen;

    [ObservableProperty]
    private bool _isMobileMoreSheetOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SidebarWidth))]
    [NotifyPropertyChangedFor(nameof(ScrimOpacity))]
    private bool _isNavExpanded = false;

    public double SidebarWidth => IsNavExpanded ? 240 : 64;
    public double ScrimOpacity => IsNavExpanded ? 1.0 : 0.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageStatusText))]
    private bool _isSyncing;

    [ObservableProperty]
    private string _syncEndpointUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StorageStatusText))]
    private bool _isGoogleSignedIn;

    public string StorageStatusText
    {
        get
        {
            if (IsSyncing) return "Syncing with cloud…";
            if (IsGoogleSignedIn) return "Synced with Google Drive";
            return "Saved on device";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastUpdatedFormatted))]
    private DateTime? _lastUpdatedAt;

    public string LastUpdatedFormatted
    {
        get
        {
            if (!LastUpdatedAt.HasValue) return "Saved on device";
            var now = DateTime.Now;
            var diff = now - LastUpdatedAt.Value;
            if (diff.TotalSeconds < 60) return "Updated just now";
            if (diff.TotalMinutes < 60) return $"Updated {Math.Max(1, (int)diff.TotalMinutes)}m ago";
            if (LastUpdatedAt.Value.Date == now.Date) return $"Updated at {LastUpdatedAt.Value:h:mm tt}";
            return $"Updated {LastUpdatedAt.Value:MMM d, h:mm tt}";
        }
    }

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
    private string _firebaseApiKey = string.Empty;

    partial void OnFirebaseApiKeyChanged(string value)
    {
        _syncService.FirebaseApiKey = value;
    }

    [ObservableProperty]
    private string _firebaseProjectId = string.Empty;

    partial void OnFirebaseProjectIdChanged(string value)
    {
        _syncService.FirebaseProjectId = value;
    }

    [ObservableProperty]
    private int _selectedNavIndex = 0;

    [ObservableProperty]
    private bool _hasLoadedCompletedView;

    [ObservableProperty]
    private bool _hasLoadedRecurringView;

    [ObservableProperty]
    private bool _hasLoadedCalendarView;

    [ObservableProperty]
    private bool _hasLoadedSearchView;

    [ObservableProperty]
    private bool _hasLoadedTagsView;

    [ObservableProperty]
    private bool _hasLoadedSettingsView;

    [ObservableProperty]
    private bool _hasLoadedGoalsView;

    public bool IsTasksView => SelectedNavIndex == 0;
    public bool IsCompletedView => SelectedNavIndex == 1;
    public bool IsRecurringView => SelectedNavIndex == 2;
    public bool IsCalendarView => SelectedNavIndex == 3;
    public bool IsSearchView => SelectedNavIndex == 4;
    public bool IsTagsView => SelectedNavIndex == 5;
    public bool IsGoalsView => SelectedNavIndex == 6;
    public bool IsSettingsView => SelectedNavIndex == 7;

    public bool IsMoreActive => IsSearchView || IsTagsView || IsGoalsView || IsSettingsView;

    partial void OnSelectedNavIndexChanged(int value)
    {
        if (value == 1 && !HasLoadedCompletedView) HasLoadedCompletedView = true;
        if (value == 2 && !HasLoadedRecurringView) HasLoadedRecurringView = true;
        if (value == 3 && !HasLoadedCalendarView) HasLoadedCalendarView = true;
        if (value == 4 && !HasLoadedSearchView) HasLoadedSearchView = true;
        if (value == 5 && !HasLoadedTagsView) HasLoadedTagsView = true;
        if (value == 6 && !HasLoadedGoalsView) HasLoadedGoalsView = true;
        if (value == 7 && !HasLoadedSettingsView) HasLoadedSettingsView = true;

        OnPropertyChanged(nameof(IsTasksView));
        OnPropertyChanged(nameof(IsSearchView));
        OnPropertyChanged(nameof(IsCompletedView));
        OnPropertyChanged(nameof(IsRecurringView));
        OnPropertyChanged(nameof(IsCalendarView));
        OnPropertyChanged(nameof(IsTagsView));
        OnPropertyChanged(nameof(IsGoalsView));
        OnPropertyChanged(nameof(IsSettingsView));
        OnPropertyChanged(nameof(IsMoreActive));
    }

    [ObservableProperty]
    private bool _isReviewPromptVisible;

    [ObservableProperty]
    private bool _isTaskWizardVisible;

    public ObservableCollection<TodoItemViewModel> TodoItems { get; } = new();

    public ObservableCollection<TodoItemViewModel> UpcomingTodoItems { get; } = new();

    public ObservableCollection<TodoItemViewModel> TodayTodoItems => StandardTodoItems;

    public ObservableCollection<TodoItemViewModel> StandardTodoItems { get; } = new();

    public ObservableCollection<TodoItemViewModel> CompletedTodayTodoItems { get; } = new();

    public ObservableCollection<TodoItemViewModel> CompletedHistoryTodoItems { get; } = new();

    public ObservableCollection<TodoItemViewModel> AllRecurringTodoItems { get; } = new();

    public ObservableCollection<TodoItemViewModel> SearchResultsTodoItems { get; } = new();

    public bool HasSearchResults => SearchResultsTodoItems.Count > 0;

    [ObservableProperty]
    private string _searchStatusFilter = "All";

    partial void OnSearchStatusFilterChanged(string value)
    {
        UpdateSearchResults();
    }

    [RelayCommand]
    private void SetSearchStatusFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return;
        SearchStatusFilter = filter;
    }

    [RelayCommand]
    private void ClearSearchQuery()
    {
        SearchQuery = string.Empty;
    }

    [RelayCommand]
    private void ExecuteSearch()
    {
        UpdateSearchResults();
    }

    public bool HasUpcomingTodoItems => UpcomingTodoItems.Count > 0;

    public bool HasTodayTodoItems => StandardTodoItems.Count > 0;

    public bool HasStandardTodoItems => StandardTodoItems.Count > 0;

    public bool HasCompletedTodayTodoItems => CompletedTodayTodoItems.Count > 0;

    public bool HasCompletedHistoryTodoItems => CompletedHistoryTodoItems.Count > 0;

    public bool HasAllRecurringTodoItems => AllRecurringTodoItems.Count > 0;

    [ObservableProperty]
    private bool _isUpcomingTasksExpanded = true;

    [ObservableProperty]
    private bool _isTodayTasksExpanded = true;

    [ObservableProperty]
    private bool _isCompletedTodayExpanded = true;

    [RelayCommand]
    private void ToggleUpcomingTasksExpanded() => IsUpcomingTasksExpanded = !IsUpcomingTasksExpanded;

    [RelayCommand]
    private void ToggleTodayTasksExpanded() => IsTodayTasksExpanded = !IsTodayTasksExpanded;

    [RelayCommand]
    private void ToggleCompletedTodayExpanded() => IsCompletedTodayExpanded = !IsCompletedTodayExpanded;

    [RelayCommand]
    private void NavigateToRecurringView() => SelectedNavIndex = 2;

    [RelayCommand]
    private void NavigateToCalendarView() => SelectedNavIndex = 3;

    private bool PassesSearchAndCategoryFilter(TodoItemViewModel item)
    {
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.Trim();
            bool matchesTitle = item.Title.Contains(q, StringComparison.OrdinalIgnoreCase);
            bool matchesDesc = item.HasDescription && item.Description.Contains(q, StringComparison.OrdinalIgnoreCase);
            bool matchesTag = item.Categories.Any(c => c.Contains(q, StringComparison.OrdinalIgnoreCase));
            if (!matchesTitle && !matchesDesc && !matchesTag) return false;
        }

        return PassesCategoryFilter(item);
    }

    private bool PassesCategoryFilter(TodoItemViewModel item)
    {
        var selectedOptions = CategoryFilterOptions.Where(o => o.IsSelected).Select(o => o.Name).ToList();
        if (selectedOptions.Count == 0 || selectedOptions.Contains("All Categories", StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        bool allowsUncategorized = selectedOptions.Contains("Uncategorized", StringComparer.OrdinalIgnoreCase);
        var namedCategories = selectedOptions.Where(s => !s.Equals("All Categories", StringComparison.OrdinalIgnoreCase) && !s.Equals("Uncategorized", StringComparison.OrdinalIgnoreCase)).ToList();

        if (allowsUncategorized && !item.HasCategories)
        {
            return true;
        }

        if (item.Categories.Any(c => namedCategories.Contains(c, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private void UpdateSubCollections()
    {
        var today = DateTime.Today;
        var filteredItems = TodoItems.Where(PassesSearchAndCategoryFilter).ToList();
        var activeItems = filteredItems.Where(x => !x.IsCompleted).ToList();
        var completedItems = filteredItems.Where(x => x.IsCompleted).ToList();

        // Today tasks: active tasks with DueDate <= today OR ReminderAt <= today OR without any dates
        var freshStandard = activeItems.Where(x =>
            (x.DueDate.HasValue && x.DueDate.Value.Date <= today) ||
            (x.ReminderAt.HasValue && x.ReminderAt.Value.Date <= today) ||
            (!x.DueDate.HasValue && !x.ReminderAt.HasValue)).ToList();

        // Upcoming tasks: active one-off (non-recurring) tasks due or reminded strictly in the future, sorted by nearest due date
        var allUpcoming = activeItems
            .Where(x => !x.IsRecurring && !freshStandard.Contains(x))
            .OrderBy(x => x.DueDate ?? x.ReminderAt ?? DateTime.MaxValue)
            .ToList();

        // Cap upcoming display to 5 nearest items
        var freshUpcoming = allUpcoming
            .Take(5)
            .ToList();

        var freshCompletedToday = completedItems
            .Where(x => !x.CompletedAt.HasValue || x.CompletedAt.Value.ToLocalTime().Date == today)
            .OrderByDescending(x => x.CompletedAt ?? DateTime.MinValue)
            .ToList();
        var freshCompletedHistory = completedItems
            .Where(PassesCompletedDateFilter)
            .OrderByDescending(x => x.CompletedAt ?? DateTime.MinValue)
            .ToList();

        // All recurring tasks
        var freshAllRecurring = filteredItems.Where(x => x.IsRecurring).ToList();

        SyncCollection(UpcomingTodoItems, freshUpcoming);
        SyncCollection(StandardTodoItems, freshStandard);
        SyncCollection(CompletedTodayTodoItems, freshCompletedToday);
        SyncCollection(CompletedHistoryTodoItems, freshCompletedHistory);
        SyncCollection(AllRecurringTodoItems, freshAllRecurring);

        OnPropertyChanged(nameof(HasUpcomingTodoItems));
        OnPropertyChanged(nameof(HasTodayTodoItems));
        OnPropertyChanged(nameof(HasStandardTodoItems));
        OnPropertyChanged(nameof(HasCompletedTodayTodoItems));
        OnPropertyChanged(nameof(HasCompletedHistoryTodoItems));
        OnPropertyChanged(nameof(HasAllRecurringTodoItems));
        OnPropertyChanged(nameof(IsCompletedTodaySectionVisible));
        OnPropertyChanged(nameof(IsUpcomingSectionVisible));

        GenerateCalendarGrid();
        UpdateSearchResults();
    }

    private void UpdateSearchResults()
    {
        var q = SearchQuery?.Trim() ?? string.Empty;
        var filtered = TodoItems.Where(PassesCategoryFilter).ToList();

        if (!string.IsNullOrWhiteSpace(q))
        {
            filtered = filtered.Where(x =>
                x.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (x.HasDescription && x.Description.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                x.Categories.Any(c => c.Contains(q, StringComparison.OrdinalIgnoreCase))
            ).ToList();
        }

        if (SearchStatusFilter == "Active")
        {
            filtered = filtered.Where(x => !x.IsCompleted).ToList();
        }
        else if (SearchStatusFilter == "Completed")
        {
            filtered = filtered.Where(x => x.IsCompleted).ToList();
        }
        else if (SearchStatusFilter == "WithNotes")
        {
            filtered = filtered.Where(x => x.HasDescription).ToList();
        }

        SyncCollection(SearchResultsTodoItems, filtered);
        OnPropertyChanged(nameof(HasSearchResults));
    }

    #region Calendar View Logic

    private bool _isUpdatingCalendarPickers;

    [ObservableProperty]
    private bool _isSingleColumnCalendar;

    [RelayCommand]
    private void SetCalendarLayoutMode(string? mode)
    {
        IsSingleColumnCalendar = string.Equals(mode, "Schedule", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(mode, "SingleColumn", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(mode, "Portrait", StringComparison.OrdinalIgnoreCase);
    }

    [ObservableProperty]
    private DateTime _currentCalendarDate = DateTime.Today;

    [ObservableProperty]
    private string _currentMonthYearText = DateTime.Today.ToString("MMMM yyyy");

    [ObservableProperty]
    private int _selectedMonthIndex = DateTime.Today.Month - 1;

    partial void OnSelectedMonthIndexChanged(int value)
    {
        if (_isUpdatingCalendarPickers) return;
        if (value < 0 || value > 11) return;
        int targetMonth = value + 1;
        int targetYear = SelectedYear > 0 ? SelectedYear : CurrentCalendarDate.Year;
        int maxDays = DateTime.DaysInMonth(targetYear, targetMonth);
        int targetDay = Math.Min(CurrentCalendarDate.Day, maxDays);

        CurrentCalendarDate = new DateTime(targetYear, targetMonth, targetDay);
        GenerateCalendarGrid();
    }

    [ObservableProperty]
    private int _selectedYear = DateTime.Today.Year;

    partial void OnSelectedYearChanged(int value)
    {
        if (_isUpdatingCalendarPickers) return;
        if (value < 2000 || value > 2100) return;
        int targetMonth = SelectedMonthIndex >= 0 ? SelectedMonthIndex + 1 : CurrentCalendarDate.Month;
        int maxDays = DateTime.DaysInMonth(value, targetMonth);
        int targetDay = Math.Min(CurrentCalendarDate.Day, maxDays);

        CurrentCalendarDate = new DateTime(value, targetMonth, targetDay);
        GenerateCalendarGrid();
    }

    public List<string> MonthOptions { get; } = new()
    {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    };

    public List<int> YearOptions { get; } = Enumerable.Range(DateTime.Today.Year - 10, 21).ToList();

    #region Recurrence & Task Filters for Calendar View

    [ObservableProperty]
    private bool _isStandardFilterEnabled = true;

    partial void OnIsStandardFilterEnabledChanged(bool value) => GenerateCalendarGrid();

    [ObservableProperty]
    private bool _isDailyFilterEnabled = true;

    partial void OnIsDailyFilterEnabledChanged(bool value) => GenerateCalendarGrid();

    [ObservableProperty]
    private bool _isWeekdaysFilterEnabled = true;

    partial void OnIsWeekdaysFilterEnabledChanged(bool value) => GenerateCalendarGrid();

    [ObservableProperty]
    private bool _isWeeklyFilterEnabled = true;

    partial void OnIsWeeklyFilterEnabledChanged(bool value) => GenerateCalendarGrid();

    [ObservableProperty]
    private bool _isMonthlyFilterEnabled = true;

    partial void OnIsMonthlyFilterEnabledChanged(bool value) => GenerateCalendarGrid();

    [ObservableProperty]
    private bool _isYearlyFilterEnabled = true;

    partial void OnIsYearlyFilterEnabledChanged(bool value) => GenerateCalendarGrid();

    [ObservableProperty]
    private bool _isCompletedFilterEnabled = true;

    partial void OnIsCompletedFilterEnabledChanged(bool value) => GenerateCalendarGrid();

    private IEnumerable<TodoItem> GetFilteredTodoModels()
    {
        var allModels = TodoItems.Where(PassesSearchAndCategoryFilter).Select(x => x.Model).ToList();
        var recurringTitleToTypeMap = allModels
            .Where(t => t.IsRecurring && !string.IsNullOrWhiteSpace(t.RecurrenceType) && !t.RecurrenceType.Equals("None", StringComparison.OrdinalIgnoreCase))
            .GroupBy(t => t.Title.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().RecurrenceType);

        return allModels.Where(item =>
        {
            if (item.IsCompleted && !IsCompletedFilterEnabled)
            {
                return false;
            }

            string recType = item.RecurrenceType ?? "None";
            if (recType.Equals("None", StringComparison.OrdinalIgnoreCase) && item.IsCompleted)
            {
                var titleKey = item.Title.Trim().ToLowerInvariant();
                if (recurringTitleToTypeMap.TryGetValue(titleKey, out var parentRecType))
                {
                    recType = parentRecType;
                }
            }

            string recurrenceType = recType.ToLowerInvariant();
            if (recurrenceType.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return IsStandardFilterEnabled;
            }

            return recurrenceType switch
            {
                "daily" => IsDailyFilterEnabled,
                "weekdays" => IsWeekdaysFilterEnabled,
                "weekly" => IsWeeklyFilterEnabled,
                "monthly" => IsMonthlyFilterEnabled,
                "yearly" => IsYearlyFilterEnabled,
                "custom" => IsDailyFilterEnabled || IsWeeklyFilterEnabled || IsMonthlyFilterEnabled,
                _ => IsStandardFilterEnabled
            };
        });
    }

    #endregion

    private readonly Dictionary<string, string> _dateNotes = new();
    private bool _isSelectingDay;
    private DispatcherTimer? _dateNoteSaveTimer;

    [ObservableProperty]
    private string _selectedDayNoteText = string.Empty;

    partial void OnSelectedDayNoteTextChanged(string value)
    {
        if (_selectedDay == null || _isSelectingDay) return;

        // 1. Instantly update in-memory SelectedDay NoteText & badge
        var dateKey = _selectedDay.Date.ToString("yyyy-MM-dd");
        var trimmedText = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmedText))
        {
            _dateNotes.Remove(dateKey);
            _selectedDay.NoteText = string.Empty;
        }
        else
        {
            _dateNotes[dateKey] = trimmedText;
            _selectedDay.NoteText = trimmedText;
        }
        _selectedDay.RefreshComputedProperties();

        // 2. Debounce async SQLite persistence (500ms delay after last keystroke)
        if (_dateNoteSaveTimer == null)
        {
            _dateNoteSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _dateNoteSaveTimer.Tick += async (_, _) =>
            {
                _dateNoteSaveTimer?.Stop();
                if (_selectedDay != null)
                {
                    var noteToSave = SelectedDayNoteText?.Trim() ?? string.Empty;
                    await _todoService.SaveDateNoteAsync(_selectedDay.Date, noteToSave);
                }
            };
        }

        _dateNoteSaveTimer.Stop();
        _dateNoteSaveTimer.Start();
    }

    public ObservableCollection<CalendarDayViewModel> CalendarDays { get; } = new();

    public ObservableCollection<TodoItemViewModel> SelectedDateTasks { get; } = new();

    [ObservableProperty]
    private bool _isSidebarOpen;

    [ObservableProperty]
    private CalendarDayViewModel? _selectedDay;

    [ObservableProperty]
    private string _selectedDateTitle = "Tasks for Selected Date";

    public bool HasSelectedDateTasks => SelectedDateTasks.Count > 0;

    [RelayCommand]
    private void NextMonth()
    {
        CurrentCalendarDate = CurrentCalendarDate.AddMonths(1);
        GenerateCalendarGrid();
    }

    [RelayCommand]
    private void PreviousMonth()
    {
        CurrentCalendarDate = CurrentCalendarDate.AddMonths(-1);
        GenerateCalendarGrid();
    }

    [RelayCommand]
    private void JumpToToday()
    {
        CurrentCalendarDate = DateTime.Today;
        GenerateCalendarGrid();
        var todayVm = CalendarDays.FirstOrDefault(x => x.Date.Date == DateTime.Today);
        if (todayVm != null)
        {
            SelectDay(todayVm);
        }
    }

    [RelayCommand]
    private void SelectDay(CalendarDayViewModel? day)
    {
        if (day == null) return;

        foreach (var d in CalendarDays)
        {
            d.IsSelected = false;
        }

        _isSelectingDay = true;
        day.IsSelected = true;
        SelectedDay = day;
        SelectedDayNoteText = day.NoteText;
        _isSelectingDay = false;

        var tasksForDate = RecurrenceEvaluator.GetTasksForDate(day.Date, GetFilteredTodoModels()).ToList();
        var freshSelectedTasks = TodoItems
            .Where(x => PassesCategoryFilter(x) && tasksForDate.Any(t => t.Id == x.Id))
            .Select(x => new TodoItemViewModel(x.Model) { ContextDate = day.Date })
            .ToList();

        SyncCollection(SelectedDateTasks, freshSelectedTasks);
        SelectedDateTitle = $"Tasks for {day.Date:MMM d, yyyy}";
        IsSidebarOpen = true;

        OnPropertyChanged(nameof(HasSelectedDateTasks));
    }

    [RelayCommand]
    private async Task SaveSelectedDayNoteAsync()
    {
        if (SelectedDay == null) return;

        var dateKey = SelectedDay.Date.ToString("yyyy-MM-dd");
        var text = SelectedDayNoteText?.Trim() ?? string.Empty;

        await _todoService.SaveDateNoteAsync(SelectedDay.Date, text);

        if (string.IsNullOrWhiteSpace(text))
        {
            _dateNotes.Remove(dateKey);
            SelectedDay.NoteText = string.Empty;
        }
        else
        {
            _dateNotes[dateKey] = text;
            SelectedDay.NoteText = text;
        }

        SelectedDay.RefreshComputedProperties();
        GenerateCalendarGrid();
        StatusMessage = $"Note saved for {SelectedDay.Date:MMM d}";
    }

    [RelayCommand]
    private async Task DeleteSelectedDayNoteAsync()
    {
        if (SelectedDay == null) return;

        var dateKey = SelectedDay.Date.ToString("yyyy-MM-dd");
        await _todoService.DeleteDateNoteAsync(SelectedDay.Date);

        _dateNotes.Remove(dateKey);
        SelectedDayNoteText = string.Empty;
        SelectedDay.NoteText = string.Empty;
        SelectedDay.RefreshComputedProperties();
        GenerateCalendarGrid();
        StatusMessage = $"Note deleted for {SelectedDay.Date:MMM d}";
    }

    [RelayCommand]
    private void CloseSidebar()
    {
        IsSidebarOpen = false;
    }

    private int _calendarGridVersion = 0;

    public void GenerateCalendarGrid()
    {
        CurrentMonthYearText = CurrentCalendarDate.ToString("MMMM yyyy");

        _isUpdatingCalendarPickers = true;
        try
        {
            SelectedMonthIndex = CurrentCalendarDate.Month - 1;
            SelectedYear = CurrentCalendarDate.Year;
        }
        finally
        {
            _isUpdatingCalendarPickers = false;
        }

        var currentVersion = ++_calendarGridVersion;
        var currentCalendarDate = CurrentCalendarDate;
        var filteredModels = GetFilteredTodoModels().ToList();
        var vmDict = TodoItems.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        var notesSnapshot = new Dictionary<string, string>(_dateNotes);
        var selectedDate = SelectedDay?.Date;

        Task.Run(() =>
        {
            var firstDayOfMonth = new DateTime(currentCalendarDate.Year, currentCalendarDate.Month, 1);
            int offsetDays = (int)firstDayOfMonth.DayOfWeek;
            var gridStartDate = firstDayOfMonth.AddDays(-offsetDays);
            var today = DateTime.Today;

            var freshDays = new List<CalendarDayViewModel>(42);
            for (int i = 0; i < 42; i++)
            {
                var dayDate = gridStartDate.AddDays(i);
                bool isCurrentMonth = dayDate.Month == currentCalendarDate.Month;
                bool isToday = dayDate.Date == today;

                var dayVm = new CalendarDayViewModel(dayDate, isCurrentMonth, isToday);
                if (notesSnapshot.TryGetValue(dayDate.ToString("yyyy-MM-dd"), out var noteText))
                {
                    dayVm.NoteText = noteText;
                }

                var matchingModels = RecurrenceEvaluator.GetTasksForDate(dayDate, filteredModels);
                foreach (var m in matchingModels)
                {
                    if (vmDict.TryGetValue(m.Id, out var vm))
                    {
                        var contextualVm = new TodoItemViewModel(m)
                        {
                            ContextDate = dayDate
                        };
                        dayVm.DayTasks.Add(contextualVm);
                    }
                }
                dayVm.RefreshComputedProperties();

                if (selectedDate.HasValue && dayVm.Date.Date == selectedDate.Value.Date)
                {
                    dayVm.IsSelected = true;
                }

                freshDays.Add(dayVm);
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (currentVersion != _calendarGridVersion) return;

                if (CalendarDays.Count == 42)
                {
                    for (int i = 0; i < 42; i++)
                    {
                        var target = CalendarDays[i];
                        var source = freshDays[i];

                        target.Date = source.Date;
                        target.IsCurrentMonth = source.IsCurrentMonth;
                        target.IsToday = source.IsToday;
                        target.IsSelected = source.IsSelected;
                        target.NoteText = source.NoteText;
                        target.DayTasks.Clear();
                        foreach (var task in source.DayTasks)
                        {
                            target.DayTasks.Add(task);
                        }
                        target.RefreshComputedProperties();
                    }
                }
                else
                {
                    CalendarDays.Clear();
                    foreach (var d in freshDays)
                    {
                        CalendarDays.Add(d);
                    }
                }

                if (SelectedDay != null)
                {
                    var updatedSelectedDay = CalendarDays.FirstOrDefault(x => x.Date.Date == SelectedDay.Date.Date);
                    if (updatedSelectedDay != null)
                    {
                        var tasksForDate = RecurrenceEvaluator.GetTasksForDate(updatedSelectedDay.Date, filteredModels);
                        var freshSelectedTasks = tasksForDate
                            .Select(m => new TodoItemViewModel(m) { ContextDate = updatedSelectedDay.Date })
                            .ToList();
                        SyncCollection(SelectedDateTasks, freshSelectedTasks);
                        OnPropertyChanged(nameof(HasSelectedDateTasks));
                    }
                }
            });
        });
    }

    #endregion

    private static void SyncCollection(ObservableCollection<TodoItemViewModel> collection, List<TodoItemViewModel> freshItems)
    {
        var freshSet = new HashSet<Guid>(freshItems.Select(x => x.Id));
        for (int i = collection.Count - 1; i >= 0; i--)
        {
            if (!freshSet.Contains(collection[i].Id))
            {
                collection.RemoveAt(i);
            }
        }
        for (int i = 0; i < freshItems.Count; i++)
        {
            var item = freshItems[i];
            int existingIndex = -1;
            for (int j = 0; j < collection.Count; j++)
            {
                if (collection[j].Id == item.Id)
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                collection.Insert(i, item);
            }
            else
            {
                collection[existingIndex].ContextDate = item.ContextDate;
                if (existingIndex != i)
                {
                    collection.Move(existingIndex, i);
                }
            }
        }
    }

    public TaskConflictViewModel TaskConflictVm { get; }

    public int UnresolvedConflictCount => _syncService.UnresolvedConflictCount;
    public bool HasUnresolvedConflicts => UnresolvedConflictCount > 0;
    public string UnresolvedConflictNotificationText => $"{UnresolvedConflictCount} task update(s) require your review.";

    public MainViewModel() : this(
        App.Services?.GetService<ITodoService>() ?? new SQLiteTodoService(),
        App.Services?.GetService<IThemeService>() ?? new ThemeService(),
        App.Services?.GetService<ISyncService>() ?? new GoogleDriveSyncService(App.Services?.GetService<ITodoService>() ?? new SQLiteTodoService()),
        App.Services?.GetService<IExportService>() ?? new ExcelExportService(),
        App.Services?.GetService<IGoalService>() ?? new SQLiteGoalService())
    {
    }

    public MainViewModel(ITodoService todoService, IThemeService themeService, ISyncService syncService, IExportService exportService, IGoalService goalService)
    {
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _goalService = goalService ?? throw new ArgumentNullException(nameof(goalService));
        _goalsVM = new GoalsViewModel(_goalService);
        _selectedTasksLayoutOption = TasksLayoutOptions[0];
        LoadUserSettings();

        IConflictRepository conflictRepo;
        if (_todoService is SQLiteTodoService sqliteSvc)
        {
            conflictRepo = new SQLiteConflictRepository(sqliteSvc.DatabaseConnection);
        }
        else
        {
            conflictRepo = new SQLiteConflictRepository(Wadd.Core.Helpers.AppDataHelper.GetWaddFilePath("wadd.db"));
        }

        TaskConflictVm = new TaskConflictViewModel(conflictRepo, _todoService, _syncService);

        _syncService.ConflictCountChanged += (_, _) =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(UnresolvedConflictCount));
                OnPropertyChanged(nameof(HasUnresolvedConflicts));
                OnPropertyChanged(nameof(UnresolvedConflictNotificationText));
            });
        };

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
        CheckAndUpdateCurrentDate();
        _ = LoadTodoItemsAsync();

        WaddDatabaseNotifier.DataChanged += async (_, _) =>
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                CheckAndUpdateCurrentDate();
                await LoadTodoItemsAsync();
                RequestDebouncedAutoSync();
            });
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += (_, _) =>
        {
            CheckAndUpdateCurrentDate();
            OnPropertyChanged(nameof(LastUpdatedFormatted));
            OnPropertyChanged(nameof(ReminderLaterTodayText));
            OnPropertyChanged(nameof(IsReminderLaterTodayEnabled));
            OnPropertyChanged(nameof(IsReminderTomorrowMorningEnabled));
            OnPropertyChanged(nameof(IsReminderNextWeekEnabled));

            _periodicSyncTicks++;
            if (_periodicSyncTicks >= 20) // Every 5 minutes (20 * 15s)
            {
                _periodicSyncTicks = 0;
                if (IsGoogleSignedIn && !IsSyncing)
                {
                    _ = TriggerAutoSyncAsync();
                }
            }
        };
        timer.Start();
    }

    private DateTime _lastRecordedDate = DateTime.Today;

    public void CheckAndUpdateCurrentDate()
    {
        var today = DateTime.Today;
        CurrentDateFormatted = DateTime.Now.ToString("dddd, MMMM d").ToUpperInvariant();
        CurrentDateFull = DateTime.Now.ToString("dddd, MMMM d, yyyy");
        OnPropertyChanged(nameof(MinDueDate));
        OnPropertyChanged(nameof(DueDateHeaderYear));
        OnPropertyChanged(nameof(DueDateHeaderMainText));

        if (today != _lastRecordedDate)
        {
            _lastRecordedDate = today;
            UpdateSubCollections();
            UpdateAvailableCategories();
            GenerateCalendarGrid();
        }
    }

    [RelayCommand]
    private async Task ReviewNowAsync()
    {
        IsReviewPromptVisible = false;
        await ShowTaskReviewWizardAsync();
    }

    [RelayCommand]
    private void ReviewLater()
    {
        IsReviewPromptVisible = false;
        StatusMessage = "Task updates saved for later review.";
    }

    [RelayCommand]
    public async Task ShowTaskReviewWizardAsync()
    {
        await TaskConflictVm.LoadConflictsAsync();
        if (TaskConflictVm.Conflicts.Count == 0) return;

        var tcs = new TaskCompletionSource<bool>();

        EventHandler onCompletedHandler = null!;
        onCompletedHandler = (_, _) =>
        {
            TaskConflictVm.OnCompleted -= onCompletedHandler;
            tcs.TrySetResult(true);
        };
        TaskConflictVm.OnCompleted += onCompletedHandler;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var window = new TaskConflictDialog
            {
                DataContext = TaskConflictVm
            };

            EventHandler desktopCloseHandler = null!;
            desktopCloseHandler = (_, _) =>
            {
                TaskConflictVm.OnCompleted -= desktopCloseHandler;
                window.Close();
            };
            TaskConflictVm.OnCompleted += desktopCloseHandler;

            await window.ShowDialog(desktop.MainWindow);
            tcs.TrySetResult(true);
        }
        else
        {
            IsTaskWizardVisible = true;
            await tcs.Task;
            IsTaskWizardVisible = false;
        }

        await LoadTodoItemsAsync();
        StatusMessage = "Syncing reviewed changes to Google Drive...";
        try
        {
            await _syncService.SyncAsync();
            StatusMessage = "✓ All task updates have been reviewed successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✓ Task updates saved. Sync update pending: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenConflictCenter()
    {
        _ = ShowTaskReviewWizardAsync();
    }

    private void UpdateGoogleAuthState()
    {
        IsGoogleSignedIn = _syncService.IsSignedIn;
        GoogleUserEmail = _syncService.UserEmail ?? string.Empty;
        GoogleUserName = _syncService.UserName ?? "Google Account User";
        GoogleClientId = _syncService.GoogleClientId ?? string.Empty;
        FirebaseApiKey = _syncService.FirebaseApiKey ?? string.Empty;
        FirebaseProjectId = _syncService.FirebaseProjectId ?? string.Empty;
    }

    [RelayCommand]
    private async Task LoadTodoItemsAsync()
    {
        try
        {
            var itemsList = (await _todoService.GetTodosAsync()).ToList();
            var notesDict = await _todoService.GetAllDateNotesAsync();
            var customTagsDict = await _todoService.GetCustomTagsAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _dateNotes.Clear();
                foreach (var kvp in notesDict)
                {
                    _dateNotes[kvp.Key] = kvp.Value;
                }

                _customEmptyTags.Clear();
                foreach (var kvp in customTagsDict)
                {
                    if (!_customEmptyTags.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        _customEmptyTags.Add(kvp.Key);
                    }
                    _tagCreatedTimes[kvp.Key] = kvp.Value;
                }

                var existingDict = TodoItems.GroupBy(vm => vm.Id).ToDictionary(g => g.Key, g => g.First());
                var freshIds = new HashSet<Guid>(itemsList.Select(x => x.Id));

                // 1. Remove items that no longer exist
                for (int i = TodoItems.Count - 1; i >= 0; i--)
                {
                    if (!freshIds.Contains(TodoItems[i].Id))
                    {
                        TodoItems.RemoveAt(i);
                    }
                }

                // 2. Update existing items in place or add new ones in order
                for (int i = 0; i < itemsList.Count; i++)
                {
                    var item = itemsList[i];
                    if (existingDict.TryGetValue(item.Id, out var existingVm))
                    {
                        existingVm.UpdateFromModel(item);
                        var currentIndex = TodoItems.IndexOf(existingVm);
                        if (currentIndex != i && currentIndex >= 0)
                        {
                            TodoItems.Move(currentIndex, i);
                        }
                    }
                    else
                    {
                        TodoItems.Insert(i, new TodoItemViewModel(item));
                    }
                }

                UpdateSubCollections();
                UpdateAvailableCategories();
                GenerateCalendarGrid();
                LastUpdatedAt = DateTime.Now;
                StatusMessage = $"Loaded {TodoItems.Count} {(TodoItems.Count == 1 ? "task" : "tasks")} from device.";

                if (!_initialSyncCompleted && IsGoogleSignedIn)
                {
                    _initialSyncCompleted = true;
                    _ = TriggerAutoSyncAsync();
                }
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
        CloseMobileDueDateSheet();
    }

    [RelayCommand]
    private void SetDueDateTomorrow()
    {
        NewTaskDueDate = DateTime.Today.AddDays(1);
        CloseMobileDueDateSheet();
    }

    [RelayCommand]
    private void SetDueDateNextWeek()
    {
        NewTaskDueDate = DateTime.Today.AddDays(7);
        CloseMobileDueDateSheet();
    }

    [RelayCommand]
    private void ClearDueDate()
    {
        NewTaskDueDate = null;
    }

    [RelayCommand]
    private void SetReminderLaterToday()
    {
        var time = GetSuggestedLaterTodayTime();
        var preset = DateTime.Today.Add(time);
        if (preset <= DateTime.Now)
        {
            StatusMessage = "Cannot set reminder in the past.";
            return;
        }
        NewTaskReminderDate = DateTime.Today;
        NewTaskReminderTime = time;
        CloseMobileReminderSheet();
    }

    [RelayCommand]
    private void SetReminderTomorrowMorning()
    {
        NewTaskReminderDate = DateTime.Today.AddDays(1);
        NewTaskReminderTime = new TimeSpan(9, 0, 0); // 9:00 AM
        CloseMobileReminderSheet();
    }

    [RelayCommand]
    private void SetReminderNextWeek()
    {
        NewTaskReminderDate = DateTime.Today.AddDays(7);
        NewTaskReminderTime = new TimeSpan(9, 0, 0); // 9:00 AM
        CloseMobileReminderSheet();
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

        var reminderAt = GetCombinedNewTaskReminder();
        if (NewTaskDueDate.HasValue && reminderAt.HasValue)
        {
            DateTime maxAllowed = NewTaskDueDate.Value.TimeOfDay == TimeSpan.Zero
                ? NewTaskDueDate.Value.Date.AddDays(1).AddTicks(-1)
                : NewTaskDueDate.Value;

            if (reminderAt.Value > maxAllowed)
            {
                NewTaskReminderDate = null;
                NewTaskReminderTime = null;
                StatusMessage = "Reminder reset because it exceeded the due date.";
                return;
            }
        }

        try
        {
            var initialDueDate = NewTaskDueDate?.Date ?? DateTime.Today;
            var cat = NewTaskCategories.Count > 0
                ? string.Join(", ", NewTaskCategories)
                : (string.IsNullOrWhiteSpace(NewTaskCategoryInput) ? null : NewTaskCategoryInput.Trim());

            var newItem = new TodoItem
            {
                Title = NewTaskTitle.Trim(),
                Description = NewTaskDescription.Trim(),
                Category = cat,
                IsCompleted = false,
                Priority = TodoPriority.Medium,
                CreatedAt = DateTime.UtcNow,
                ReminderAt = GetCombinedNewTaskReminder(),
                IsRecurring = IsRepeatEnabled,
                RecurrenceType = IsRepeatEnabled ? SelectedRecurrenceType : "None",
                CustomRecurrenceInterval = IsRepeatEnabled && SelectedRecurrenceType == "Custom" ? CustomInterval : null,
                CustomRecurrenceUnit = IsRepeatEnabled && SelectedRecurrenceType == "Custom" ? SelectedCustomUnit : null,
                CustomWeeklyDays = IsRepeatEnabled && SelectedRecurrenceType == "Custom" && SelectedCustomUnit == "Weeks" ? GetSelectedWeeklyDaysString() : null
            };

            newItem.DueDate = IsRepeatEnabled
                ? RecurrenceHelper.GetFirstValidOccurrenceDate(newItem, initialDueDate)
                : NewTaskDueDate;

            await _todoService.AddTodoAsync(newItem);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                NewTaskTitle = string.Empty;
                NewTaskDescription = string.Empty;
                NewTaskCategoryInput = string.Empty;
                NewTaskCategories.Clear();
                OnPropertyChanged(nameof(HasNewTaskCategories));
                NewTaskDueDate = null;
                NewTaskReminderDate = null;
                NewTaskReminderTime = null;
                ClearRecurrence();
                IsMobileTaskComposerOpen = false;
                IsMobileDueDateSheetOpen = false;
                IsMobileReminderSheetOpen = false;
                IsMobileRepeatSheetOpen = false;
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

        lock (_togglingTaskIds)
        {
            if (!_togglingTaskIds.Add(itemVm.Id))
            {
                return;
            }
        }

        try
        {
            DateTime? targetDate = itemVm.ContextDate ?? (IsCalendarView && SelectedDay != null ? SelectedDay.Date : itemVm.DueDate);
            await _todoService.ToggleCompleteAsync(itemVm.Id, targetDate);
            await LoadTodoItemsAsync();
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Error toggling task: {ex.Message}";
            });
        }
        finally
        {
            lock (_togglingTaskIds)
            {
                _togglingTaskIds.Remove(itemVm.Id);
            }
        }
    }

    [RelayCommand]
    private async Task DeleteTodoAsync(TodoItemViewModel? itemVm)
    {
        if (itemVm == null) return;
        try
        {
            await _todoService.DeleteTodoAsync(itemVm.Id);

            if (SelectedDetailTask?.Id == itemVm.Id)
            {
                SelectedDetailTask = null;
                IsDetailDrawerOpen = false;
            }

            CloseAllOverlays();

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
            if (string.IsNullOrWhiteSpace(localFolder))
            {
                localFolder = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            }
            var exportDir = Path.Combine(localFolder, "Wadd", "Exports");
            Directory.CreateDirectory(exportDir);

            var fileName = $"todo_export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var filePath = Path.Combine(exportDir, fileName);

            var models = TodoItems.Select(vm => vm.Model).ToList();
            await _exportService.ExportToExcelAsync(models, filePath);

            StatusMessage = $"Exported {models.Count} {(models.Count == 1 ? "task" : "tasks")} successfully to {Path.GetFileName(filePath)}";
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
                StatusMessage = $"Signed in as {GoogleUserEmail}. Connected to Google Drive.";
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
            if (SelectedDetailTask != null)
            {
                await _todoService.UpdateTodoAsync(SelectedDetailTask.Model);
            }

            IsSyncing = true;
            StatusMessage = "Syncing with Google Drive...";
            var success = await _syncService.SyncAsync();
            if (success)
            {
                LastUpdatedAt = DateTime.Now;
                await LoadTodoItemsAsync();

                if (HasUnresolvedConflicts)
                {
                    StatusMessage = $"{UnresolvedConflictCount} task update(s) require your review.";
                    IsReviewPromptVisible = true;
                }
                else
                {
                    StatusMessage = "✓ Sync completed successfully.";
                }
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

    private void RequestDebouncedAutoSync()
    {
        if (!IsGoogleSignedIn) return;

        if (_autoSyncDebounceTimer == null)
        {
            _autoSyncDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _autoSyncDebounceTimer.Tick += async (_, _) =>
            {
                _autoSyncDebounceTimer?.Stop();
                if (IsGoogleSignedIn && !IsSyncing)
                {
                    await TriggerAutoSyncAsync();
                }
            };
        }
        _autoSyncDebounceTimer.Stop();
        _autoSyncDebounceTimer.Start();
    }

    private async Task TriggerAutoSyncAsync()
    {
        if (IsSyncing || !IsGoogleSignedIn) return;
        try
        {
            if (SelectedDetailTask != null)
            {
                await _todoService.UpdateTodoAsync(SelectedDetailTask.Model);
            }

            IsSyncing = true;
            var success = await _syncService.SyncAsync();
            if (success)
            {
                LastUpdatedAt = DateTime.Now;
                await LoadTodoItemsAsync();

                if (HasUnresolvedConflicts)
                {
                    StatusMessage = $"{UnresolvedConflictCount} task update(s) require your review.";
                    IsReviewPromptVisible = true;
                }
            }
        }
        catch
        {
            // Silent error handling for background auto sync
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private void CloseAllOverlays()
    {
        IsMobileMoreSheetOpen = false;
        IsMobileTagFilterSheetOpen = false;
        IsMobileCategorySheetOpen = false;
        IsMobileDueDateSheetOpen = false;
        IsMobileReminderSheetOpen = false;
        IsMobileRepeatSheetOpen = false;
        IsMobileCompletedDateFilterSheetOpen = false;
        IsMobileTaskComposerOpen = false;
        IsDetailDrawerOpen = false;
    }

    [RelayCommand]
    private void OpenTasksView()
    {
        SelectedNavIndex = 0;
        CloseAllOverlays();
    }

    [RelayCommand]
    private void OpenCompletedView()
    {
        SelectedNavIndex = 1;
        CloseAllOverlays();
    }

    [RelayCommand]
    private void OpenRecurringView()
    {
        SelectedNavIndex = 2;
        CloseAllOverlays();
    }

    [RelayCommand]
    private void OpenCalendarView()
    {
        SelectedNavIndex = 3;
        CloseAllOverlays();
    }

    [RelayCommand]
    private void OpenSearchView(object? parameter = null)
    {
        SelectedNavIndex = 4;
        CloseAllOverlays();

        if (parameter is Flyout flyout)
        {
            flyout.Hide();
        }
    }

    [RelayCommand]
    private void OpenTagsView(object? parameter = null)
    {
        SelectedNavIndex = 5;
        CloseAllOverlays();

        if (parameter is Flyout flyout)
        {
            flyout.Hide();
        }
    }

    [RelayCommand]
    private void OpenGoalsView(object? parameter = null)
    {
        SelectedNavIndex = 6;
        CloseAllOverlays();
        _ = GoalsVM.InitializeAsync();

        if (parameter is Flyout flyout)
        {
            flyout.Hide();
        }
    }

    [RelayCommand]
    private void OpenSettings(object? parameter = null)
    {
        SelectedNavIndex = 7;
        CloseAllOverlays();

        if (parameter is Flyout flyout)
        {
            flyout.Hide();
        }
    }

    [RelayCommand]
    private void ToggleMobileMoreSheet() => IsMobileMoreSheetOpen = !IsMobileMoreSheetOpen;

    [RelayCommand]
    private void CloseMobileMoreSheet() => IsMobileMoreSheetOpen = false;

    [RelayCommand]
    private async Task CreateNewGlobalTagAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGlobalTagInput)) return;
        var tag = NewGlobalTagInput.Trim();
        if (!_customEmptyTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            var created = DateTime.UtcNow;
            _customEmptyTags.Insert(0, tag);
            _tagCreatedTimes[tag] = created;
            NewGlobalTagInput = string.Empty;
            await _todoService.SaveCustomTagAsync(tag, created);
            UpdateAvailableCategories();
        }
    }

    private async Task RenameGlobalTagAsync(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;
        var oldTag = oldName.Trim();
        var newTag = newName.Trim();

        var prevTime = _tagCreatedTimes.TryGetValue(oldTag, out var t) ? t : DateTime.UtcNow;
        _tagCreatedTimes.Remove(oldTag);
        _tagCreatedTimes[newTag] = prevTime;

        _customEmptyTags.RemoveAll(x => x.Equals(oldTag, StringComparison.OrdinalIgnoreCase));
        if (!_customEmptyTags.Contains(newTag, StringComparer.OrdinalIgnoreCase))
        {
            _customEmptyTags.Insert(0, newTag);
        }

        await _todoService.RenameCustomTagAsync(oldTag, newTag);
        await _todoService.RenameCategoryAsync(oldTag, newTag);
        await LoadTodoItemsAsync();
    }

    private async Task DeleteGlobalTagAsync(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return;
        var tag = tagName.Trim();

        _tagCreatedTimes.Remove(tag);
        _customEmptyTags.RemoveAll(x => x.Equals(tag, StringComparison.OrdinalIgnoreCase));
        await _todoService.DeleteCustomTagAsync(tag);
        await _todoService.DeleteCategoryAsync(tag);
        await LoadTodoItemsAsync();
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
        var resolved = IsDarkMode ? "Dark" : "Light";
        CurrentThemeLabel = CurrentThemeMode switch
        {
            ThemeMode.Light => "Light",
            ThemeMode.Dark => "Dark",
            _ => $"System — {resolved}"
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

public partial class CategoryFilterOption : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;

    public Action<CategoryFilterOption>? OnSelectionChanged { get; set; }

    public CategoryFilterOption(string name, bool isSelected = false)
    {
        Name = name;
        _isSelected = isSelected;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnSelectionChanged?.Invoke(this);
    }
}

public partial class MainViewModel
{
    private void LoadUserSettings()
    {
        var settings = AppSettingsHelper.LoadSettings();
        if (!string.IsNullOrWhiteSpace(settings.TasksViewLayout))
        {
            TasksViewLayout = settings.TasksViewLayout;
            SelectedTasksLayoutOption = TasksLayoutOptions.FirstOrDefault(x => x.Id == settings.TasksViewLayout) ?? TasksLayoutOptions[0];
        }
        ShowNotePreviewsInList = settings.ShowNotePreviewsInList;
        _themeService.SetTheme(settings.ThemeMode);
    }

    public void SaveUserSettings()
    {
        var settings = AppSettingsHelper.LoadSettings();
        settings.TasksViewLayout = TasksViewLayout;
        settings.ShowNotePreviewsInList = ShowNotePreviewsInList;
        settings.ThemeMode = _themeService.CurrentTheme;
        AppSettingsHelper.SaveSettings(settings);
    }
}

public class TasksLayoutOption
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
