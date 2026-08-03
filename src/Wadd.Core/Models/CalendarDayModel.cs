using System.Collections.ObjectModel;

namespace Wadd.Core.Models;

public class CalendarDayModel
{
    public DateTime Date { get; set; }

    public bool IsCurrentMonth { get; set; }

    public bool IsToday { get; set; }

    public bool IsSelected { get; set; }

    public ObservableCollection<TodoItem> DayTasks { get; set; } = new();

    public bool HasOverflow => DayTasks.Count > 2;

    public int OverflowCount => DayTasks.Count > 2 ? DayTasks.Count - 2 : 0;

    public IEnumerable<TodoItem> VisibleTasks => DayTasks.Take(2);
}
