using Avalonia.Controls;
using Wadd.UI.ViewModels;

namespace Wadd.UI.Views;

public partial class CalendarView : UserControl
{
    private bool _wasNarrow;

    public CalendarView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            // Auto switch only when window width is narrow (< 680px)
            bool isNarrow = e.NewSize.Width < 680;

            if (isNarrow && !_wasNarrow)
            {
                _wasNarrow = true;
                vm.IsSingleColumnCalendar = true;
            }
            else if (!isNarrow && _wasNarrow)
            {
                _wasNarrow = false;
                vm.IsSingleColumnCalendar = false;
            }
        }
    }
}
