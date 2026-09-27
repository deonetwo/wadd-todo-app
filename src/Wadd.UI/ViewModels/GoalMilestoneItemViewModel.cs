using CommunityToolkit.Mvvm.ComponentModel;
using Wadd.Core.Models;

namespace Wadd.UI.ViewModels;

public partial class GoalMilestoneItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private GoalMilestone _model;

    public GoalMilestoneItemViewModel(GoalMilestone model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public string Id => Model.Id;
    public string GoalId => Model.GoalId;

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

    public int OrderIndex
    {
        get => Model.OrderIndex;
        set
        {
            if (Model.OrderIndex != value)
            {
                Model.OrderIndex = value;
                OnPropertyChanged();
            }
        }
    }
}
