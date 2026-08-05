using CommunityToolkit.Mvvm.ComponentModel;
using Wadd.Core.Enums;
using Wadd.Core.Models;

namespace Wadd.UI.ViewModels;

/// <summary>
/// Observable ViewModel wrapper around TodoItem domain model for UI data binding.
/// </summary>
public partial class TodoItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private TodoItem _model;

    public Guid Id => Model.Id;

    public string Title
    {
        get => Model.Title;
        set
        {
            if (Model.Title != value)
            {
                Model.Title = value;
                OnPropertyChanged();
            }
        }
    }

    public string Description
    {
        get => Model.Description;
        set
        {
            if (Model.Description != value)
            {
                Model.Description = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsCompleted
    {
        get => Model.IsCompleted;
        set
        {
            if (Model.IsCompleted != value)
            {
                Model.IsCompleted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DueDateFormatted));
                OnPropertyChanged(nameof(ContextDueDateFormatted));
                OnPropertyChanged(nameof(IsOverdue));
                OnPropertyChanged(nameof(HasDueDateOnly));
                OnPropertyChanged(nameof(IsDueToday));
                OnPropertyChanged(nameof(CompletedAtFormatted));
                OnPropertyChanged(nameof(HasCompletedAt));
            }
        }
    }

    public TodoPriority Priority => Model.Priority;

    public DateTime CreatedAt => Model.CreatedAt;

    public DateTime? DueDate => Model.DueDate;

    public bool HasDueDate => Model.DueDate.HasValue;

    public string DueDateFormatted
    {
        get
        {
            if (!Model.DueDate.HasValue) return string.Empty;
            var date = Model.DueDate.Value.Date;
            var today = DateTime.Today;
            if (date == today) return "Today";
            if (date == today.AddDays(1)) return "Tomorrow";
            if (date < today && !Model.IsCompleted) return $"Overdue ({Model.DueDate.Value:MMM d})";
            if (date > today && Model.IsRecurring && !Model.IsCompleted) return $"Upcoming ({Model.DueDate.Value:MMM d})";
            return Model.DueDate.Value.ToString("MMM d");
        }
    }

    [ObservableProperty]
    private DateTime? _contextDate;

    partial void OnContextDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(ContextDueDateFormatted));
    }

    public string ContextDueDateFormatted
    {
        get
        {
            var targetDate = ContextDate?.Date ?? Model.DueDate?.Date;
            if (!targetDate.HasValue) return string.Empty;

            var today = DateTime.Today;
            if (targetDate.Value == today) return "Today";
            if (targetDate.Value == today.AddDays(1)) return "Tomorrow";
            if (targetDate.Value < today && !Model.IsCompleted) return $"Overdue ({targetDate.Value:MMM d})";
            if (targetDate.Value > today && Model.IsRecurring && !Model.IsCompleted) return $"Upcoming ({targetDate.Value:MMM d})";
            return targetDate.Value.ToString("MMM d");
        }
    }

    public bool IsOverdue => Model.DueDate.HasValue && Model.DueDate.Value.Date < DateTime.Today && !Model.IsCompleted;
    public bool HasDueDateOnly => Model.DueDate.HasValue && !IsOverdue;
    public bool IsDueToday => Model.DueDate.HasValue && Model.DueDate.Value.Date == DateTime.Today;

    public DateTime? ReminderAt => Model.ReminderAt;

    public bool HasReminder => Model.ReminderAt.HasValue;

    public string ReminderAtFormatted
    {
        get
        {
            if (!Model.ReminderAt.HasValue) return string.Empty;
            var dt = Model.ReminderAt.Value;
            var date = dt.Date;
            var today = DateTime.Today;
            var timeStr = dt.ToString("HH:mm");
            if (date == today) return $"Today at {timeStr}";
            if (date == today.AddDays(1)) return $"Tomorrow at {timeStr}";
            return $"{dt:MMM d} at {timeStr}";
        }
    }

    public DateTime? CompletedAt => Model.CompletedAt;

    public bool HasCompletedAt => Model.CompletedAt.HasValue;

    public string CompletedAtFormatted
    {
        get
        {
            if (!Model.CompletedAt.HasValue) return string.Empty;
            var local = Model.CompletedAt.Value.ToLocalTime();
            var date = local.Date;
            var today = DateTime.Today;
            var timeStr = local.ToString("HH:mm");
            if (date == today) return $"Completed today at {timeStr}";
            if (date == today.AddDays(-1)) return $"Completed yesterday at {timeStr}";
            return $"Completed {local:MMM d, HH:mm}";
        }
    }

    public bool IsRecurring => Model.IsRecurring;

    public string RecurrenceType => Model.RecurrenceType;

    public bool HasRecurrence => Model.IsRecurring && !string.IsNullOrWhiteSpace(Model.RecurrenceType) && !Model.RecurrenceType.Equals("None", StringComparison.OrdinalIgnoreCase);

    public string RecurrenceFormatted => Wadd.Core.Helpers.RecurrenceHelper.FormatRecurrenceText(Model.IsRecurring, Model.RecurrenceType, Model.CustomRecurrenceInterval, Model.CustomRecurrenceUnit, Model.CustomWeeklyDays);

    public TodoItemViewModel(TodoItem model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public void UpdateFromModel(TodoItem newModel)
    {
        ArgumentNullException.ThrowIfNull(newModel);
        Model = newModel;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(Priority));
        OnPropertyChanged(nameof(DueDate));
        OnPropertyChanged(nameof(HasDueDate));
        OnPropertyChanged(nameof(DueDateFormatted));
        OnPropertyChanged(nameof(ContextDueDateFormatted));
        OnPropertyChanged(nameof(IsOverdue));
        OnPropertyChanged(nameof(HasDueDateOnly));
        OnPropertyChanged(nameof(IsDueToday));
        OnPropertyChanged(nameof(ReminderAt));
        OnPropertyChanged(nameof(HasReminder));
        OnPropertyChanged(nameof(ReminderAtFormatted));
        OnPropertyChanged(nameof(CompletedAt));
        OnPropertyChanged(nameof(HasCompletedAt));
        OnPropertyChanged(nameof(CompletedAtFormatted));
        OnPropertyChanged(nameof(IsRecurring));
        OnPropertyChanged(nameof(RecurrenceType));
        OnPropertyChanged(nameof(HasRecurrence));
        OnPropertyChanged(nameof(RecurrenceFormatted));
    }
}
