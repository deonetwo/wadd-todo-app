using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Wadd.UI.ViewModels;

namespace Wadd.UI.Views;

public partial class MainView : UserControl
{
    private const double CompactWidthThreshold = 820.0;

    public MainView()
    {
        InitializeComponent();
        SizeChanged += OnMainViewSizeChanged;
        MobileTaskComposerPanel.PropertyChanged += (s, e) =>
        {
            if (e.Property == IsVisibleProperty && e.NewValue is true)
            {
                Dispatcher.UIThread.Post(() => MobileTaskTitleInput.Focus(), DispatcherPriority.Render);
            }
        };
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

    private void OnMobileCategoryBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseMobileCategorySheetCommand.Execute(null);
    }

    private void OnMobileMoreSheetBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseMobileMoreSheetCommand.Execute(null);
    }

    private void OnMobileTagFilterBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseMobileTagFilterSheetCommand.Execute(null);
    }

    private void OnMobileCompletedDateBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseMobileCompletedDateFilterSheetCommand.Execute(null);
    }

    private void OnTasksLayoutPickerBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseTasksLayoutPickerCommand.Execute(null);
    }

    private void OnUpcomingTasksRangePickerBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseUpcomingTasksRangePickerCommand.Execute(null);
    }

    private void OnMobileDetailDrawerBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseDetailDrawerCommand.Execute(null);
    }

    private void OnAiProviderPickerBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseAiProviderPickerCommand.Execute(null);
    }

    private void OnAiModelPickerBackgroundPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CloseAiModelPickerCommand.Execute(null);
    }
}
