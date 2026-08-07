using Avalonia.Controls;
using Avalonia.Input;
using Wadd.UI.ViewModels;

namespace Wadd.UI.Views;

public partial class MainView : UserControl
{
    private const double CompactWidthThreshold = 720.0;

    public MainView()
    {
        InitializeComponent();
        SizeChanged += OnMainViewSizeChanged;
    }

    private void OnMainViewSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsCompact = e.NewSize.Width < CompactWidthThreshold;
            if (!vm.IsCompact)
            {
                vm.IsSideMenuOpen = false;
            }
        }
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsNavExpanded = false;
    }

    private void OnMobileComposerBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseMobileTaskComposerCommand.Execute(null);
    }

    private void OnMobileDueDateBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseMobileDueDateSheetCommand.Execute(null);
    }

    private void OnMobileReminderBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseMobileReminderSheetCommand.Execute(null);
    }

    private void OnMobileRepeatBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseMobileRepeatSheetCommand.Execute(null);
    }
}
