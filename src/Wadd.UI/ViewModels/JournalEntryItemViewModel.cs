using CommunityToolkit.Mvvm.ComponentModel;
using Wadd.Core.Models;

namespace Wadd.UI.ViewModels;

public partial class JournalEntryItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private JournalEntry _model;

    [ObservableProperty]
    private string? _associatedGoalTitle;

    public JournalEntryItemViewModel(JournalEntry model, string? associatedGoalTitle = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _associatedGoalTitle = associatedGoalTitle;
    }

    public string Id => Model.Id;
    public string? GoalId => Model.GoalId;

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

    public string Content
    {
        get => Model.Content;
        set
        {
            if (Model.Content != value)
            {
                Model.Content = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime EntryDate
    {
        get => Model.EntryDate;
        set
        {
            if (Model.EntryDate != value)
            {
                Model.EntryDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedEntryDate));
            }
        }
    }

    public string FormattedEntryDate => EntryDate.ToLocalTime().ToString("MMM d, yyyy · HH:mm");

    public bool HasAssociatedGoal => !string.IsNullOrWhiteSpace(AssociatedGoalTitle);
}
