using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Wadd.UI.ViewModels;

public partial class CalendarDayViewModel : ViewModelBase
{
    public DateTime Date { get; }

    public string DayNumberText => Date.Day.ToString();

    public string DayOfWeekShortText => Date.ToString("ddd").ToUpperInvariant();

    public string FullDateText => Date.ToString("dddd, MMM d, yyyy");

    [ObservableProperty]
    private bool _isCurrentMonth;

    [ObservableProperty]
    private bool _isToday;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ObservableCollection<TodoItemViewModel> _dayTasks = new();

    [ObservableProperty]
    private string _noteText = string.Empty;

    public bool HasNote => !string.IsNullOrWhiteSpace(NoteText);

    public bool HasTasks => DayTasks.Count > 0;

    public bool HasOverflow => DayTasks.Count > 2;

    public int OverflowCount => DayTasks.Count > 2 ? DayTasks.Count - 2 : 0;

    public string OverflowCountText => $"+{OverflowCount} more";

    public IEnumerable<TodoItemViewModel> VisibleTasks => DayTasks.Take(2);

    public CalendarDayViewModel(DateTime date, bool isCurrentMonth = true, bool isToday = false)
    {
        Date = date;
        IsCurrentMonth = isCurrentMonth;
        IsToday = isToday;
    }

    public void RefreshComputedProperties()
    {
        OnPropertyChanged(nameof(HasNote));
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(HasOverflow));
        OnPropertyChanged(nameof(OverflowCount));
        OnPropertyChanged(nameof(OverflowCountText));
        OnPropertyChanged(nameof(VisibleTasks));
    }
}
