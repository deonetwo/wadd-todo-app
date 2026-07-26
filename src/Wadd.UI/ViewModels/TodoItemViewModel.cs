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
            return Model.DueDate.Value.ToString("MMM d");
        }
    }

    public TodoItemViewModel(TodoItem model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }
}
