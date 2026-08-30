using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Wadd.Core.Models;

namespace Wadd.UI.ViewModels;

public partial class LifeGoalItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private LifeGoal _model;

    [ObservableProperty]
    private double _progressPercentage;

    [ObservableProperty]
    private int _completedMilestonesCount;

    [ObservableProperty]
    private int _totalMilestonesCount;

    [ObservableProperty]
    private bool _isSelected;

    public LifeGoalItemViewModel(LifeGoal model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public string Id => Model.Id;

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

    public string? Description
    {
        get => Model.Description;
        set
        {
            if (Model.Description != value)
            {
                Model.Description = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasDescription));
            }
        }
    }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public string Category
    {
        get => string.IsNullOrWhiteSpace(Model.Category) ? "Uncategorized" : Model.Category;
        set
        {
            if (Model.Category != value)
            {
                Model.Category = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCategory));
            }
        }
    }

    public bool HasCategory => true;

    public DateTime? TargetDate
    {
        get => Model.TargetDate;
        set
        {
            if (Model.TargetDate != value)
            {
                Model.TargetDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedTargetDate));
                OnPropertyChanged(nameof(HasTargetDate));
            }
        }
    }

    public bool HasTargetDate => TargetDate.HasValue;

    public string FormattedTargetDate => TargetDate.HasValue ? TargetDate.Value.ToString("MMM d, yyyy") : "No target date";

    public bool IsAchieved
    {
        get => Model.IsAchieved;
        set
        {
            if (Model.IsAchieved != value)
            {
                Model.IsAchieved = value;
                if (TotalMilestonesCount == 0)
                {
                    ProgressPercentage = value ? 100.0 : 0.0;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressPercentage));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBadgeText));
            }
        }
    }

    public string StatusText => IsAchieved ? "Achieved" : (TotalMilestonesCount > 0 ? $"{Math.Round(ProgressPercentage)}% Done" : "In Progress");

    public string StatusBadgeText => IsAchieved ? "Achieved" : "In Progress";

    public bool HasMilestones => TotalMilestonesCount > 0;

    public void UpdateMilestonesSummary(int completed, int total)
    {
        CompletedMilestonesCount = completed;
        TotalMilestonesCount = total;
        ProgressPercentage = total > 0 ? ((double)completed / total) * 100.0 : (IsAchieved ? 100.0 : 0.0);

        if (total > 0)
        {
            if (completed == total && !Model.IsAchieved)
            {
                Model.IsAchieved = true;
            }
            else if (completed < total && Model.IsAchieved)
            {
                Model.IsAchieved = false;
            }
        }

        OnPropertyChanged(nameof(CompletedMilestonesCount));
        OnPropertyChanged(nameof(TotalMilestonesCount));
        OnPropertyChanged(nameof(ProgressPercentage));
        OnPropertyChanged(nameof(IsAchieved));
        OnPropertyChanged(nameof(StatusText));
    }
}
