using System.Collections.ObjectModel;
using Avalonia;
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
    private TimeSpan? _newTaskReminderTime;

    partial void OnNewTaskReminderTimeChanged(TimeSpan? value)
    {
        ValidateAndResetReminderIfExceedsDueDate();
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

    [ObservableProperty]
    private bool _isCompact;

    [ObservableProperty]
    private bool _isSideMenuOpen;

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
            return "Saved to local storage";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastUpdatedFormatted))]
    private DateTime? _lastUpdatedAt;

    public string LastUpdatedFormatted
    {
        get
        {
            if (!LastUpdatedAt.HasValue) return "Local Storage Mode";
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
    private string _googleClientSecret = string.Empty;

    partial void OnGoogleClientSecretChanged(string value)
    {
        _syncService.GoogleClientSecret = value;
    }

    [ObservableProperty]
    private int _selectedNavIndex = 0;

    public bool IsTasksView => SelectedNavIndex == 0;
    public bool IsCompletedView => SelectedNavIndex == 1;
    public bool IsRecurringView => SelectedNavIndex == 2;
    public bool IsCalendarView => SelectedNavIndex == 3;
    public bool IsSettingsView => SelectedNavIndex == 4;

    partial void OnSelectedNavIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTasksView));
        OnPropertyChanged(nameof(IsCompletedView));
        OnPropertyChanged(nameof(IsRecurringView));
        OnPropertyChanged(nameof(IsCalendarView));
        OnPropertyChanged(nameof(IsSettingsView));
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
    private void NavigateToRecurringView()
    {
        SelectedNavIndex = 2;
    }

    [RelayCommand]
    private void NavigateToCalendarView()
    {
        SelectedNavIndex = 3;
    }

    private void UpdateSubCollections()
    {
        var today = DateTime.Today;
        var activeItems = TodoItems.Where(x => !x.IsCompleted).ToList();
        var completedItems = TodoItems.Where(x => x.IsCompleted).ToList();

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
            .Where(x => x.CompletedAt.HasValue && x.CompletedAt.Value.ToLocalTime().Date < today)
            .OrderByDescending(x => x.CompletedAt)
            .ToList();

        // All recurring tasks
        var freshAllRecurring = TodoItems.Where(x => x.IsRecurring).ToList();

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

        GenerateCalendarGrid();
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
        var allModels = TodoItems.Select(x => x.Model).ToList();
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

        day.IsSelected = true;
        SelectedDay = day;

        var tasksForDate = RecurrenceEvaluator.GetTasksForDate(day.Date, TodoItems.Select(x => x.Model)).ToList();
        var freshSelectedTasks = TodoItems.Where(x => tasksForDate.Any(t => t.Id == x.Id)).ToList();
        foreach (var taskVm in freshSelectedTasks)
        {
            taskVm.ContextDate = day.Date;
        }

        SyncCollection(SelectedDateTasks, freshSelectedTasks);
        SelectedDateTitle = $"Tasks for {day.Date:MMM d, yyyy}";
        IsSidebarOpen = true;

        OnPropertyChanged(nameof(HasSelectedDateTasks));
    }

    [RelayCommand]
    private void CloseSidebar()
    {
        IsSidebarOpen = false;
    }

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

        var firstDayOfMonth = new DateTime(CurrentCalendarDate.Year, CurrentCalendarDate.Month, 1);
        int offsetDays = (int)firstDayOfMonth.DayOfWeek; // Sunday = 0
        var gridStartDate = firstDayOfMonth.AddDays(-offsetDays);

        var freshDays = new List<CalendarDayViewModel>();
        for (int i = 0; i < 42; i++)
        {
            var dayDate = gridStartDate.AddDays(i);
            bool isCurrentMonth = dayDate.Month == CurrentCalendarDate.Month;
            bool isToday = dayDate.Date == DateTime.Today;

            var dayVm = new CalendarDayViewModel(dayDate, isCurrentMonth, isToday);

            var matchingModels = RecurrenceEvaluator.GetTasksForDate(dayDate, GetFilteredTodoModels()).ToList();
            var matchingVms = TodoItems.Where(x => matchingModels.Any(m => m.Id == x.Id)).ToList();

            foreach (var vm in matchingVms)
            {
                dayVm.DayTasks.Add(vm);
            }
            dayVm.RefreshComputedProperties();

            if (SelectedDay != null && dayVm.Date.Date == SelectedDay.Date.Date)
            {
                dayVm.IsSelected = true;
            }

            freshDays.Add(dayVm);
        }

        CalendarDays.Clear();
        foreach (var d in freshDays)
        {
            CalendarDays.Add(d);
        }

        if (SelectedDay != null)
        {
            var updatedSelectedDay = CalendarDays.FirstOrDefault(x => x.Date.Date == SelectedDay.Date.Date);
            if (updatedSelectedDay != null)
            {
                var tasksForDate = RecurrenceEvaluator.GetTasksForDate(updatedSelectedDay.Date, GetFilteredTodoModels()).ToList();
                var freshSelectedTasks = TodoItems.Where(x => tasksForDate.Any(t => t.Id == x.Id)).ToList();
                foreach (var taskVm in freshSelectedTasks)
                {
                    taskVm.ContextDate = updatedSelectedDay.Date;
                }
                SyncCollection(SelectedDateTasks, freshSelectedTasks);
                OnPropertyChanged(nameof(HasSelectedDateTasks));
            }
        }
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
            var currentIndex = collection.IndexOf(item);
            if (currentIndex < 0)
            {
                collection.Insert(i, item);
            }
            else if (currentIndex != i && currentIndex >= 0)
            {
                collection.Move(currentIndex, i);
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
        App.Services?.GetService<IExportService>() ?? new ExcelExportService())
    {
    }

    public MainViewModel(ITodoService todoService, IThemeService themeService, ISyncService syncService, IExportService exportService)
    {
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));

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
        _ = LoadTodoItemsAsync();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += (_, _) =>
        {
            OnPropertyChanged(nameof(LastUpdatedFormatted));
            OnPropertyChanged(nameof(ReminderLaterTodayText));
            OnPropertyChanged(nameof(IsReminderLaterTodayEnabled));
            OnPropertyChanged(nameof(IsReminderTomorrowMorningEnabled));
            OnPropertyChanged(nameof(IsReminderNextWeekEnabled));
        };
        timer.Start();
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
        GoogleClientSecret = _syncService.GoogleClientSecret ?? string.Empty;
    }

    [RelayCommand]
    private async Task LoadTodoItemsAsync()
    {
        try
        {
            var itemsList = (await _todoService.GetTodosAsync()).ToList();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var existingDict = TodoItems.ToDictionary(vm => vm.Id);
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
                LastUpdatedAt = DateTime.Now;
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
        var time = GetSuggestedLaterTodayTime();
        var preset = DateTime.Today.Add(time);
        if (preset <= DateTime.Now)
        {
            StatusMessage = "Cannot set reminder in the past.";
            return;
        }
        NewTaskReminderDate = DateTime.Today;
        NewTaskReminderTime = time;
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
            var newItem = new TodoItem
            {
                Title = NewTaskTitle.Trim(),
                Description = NewTaskDescription.Trim(),
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
                NewTaskDueDate = null;
                NewTaskReminderDate = null;
                NewTaskReminderTime = null;
                ClearRecurrence();
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
            DateTime? targetDate = IsCalendarView && SelectedDay != null ? SelectedDay.Date : null;
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
