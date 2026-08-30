using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Wadd.UI.ViewModels;

namespace Wadd.UI.Views;

public partial class GoalsView : UserControl
{
    public GoalsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (s, e) =>
        {
            if (DataContext is GoalsViewModel vm)
            {
                _ = vm.InitializeAsync();
            }
        };

        var desktopCategories = this.FindControl<ScrollViewer>("DesktopCategoriesScrollViewer");
        if (desktopCategories != null)
        {
            desktopCategories.PointerWheelChanged += OnCategoriesPointerWheelChanged;
        }

        var mobileCategories = this.FindControl<ScrollViewer>("MobileCategoriesScrollViewer");
        if (mobileCategories != null)
        {
            mobileCategories.PointerWheelChanged += OnCategoriesPointerWheelChanged;
        }
    }

    private void OnCategoriesPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is ScrollViewer sv && sv.Extent.Width > sv.Viewport.Width)
        {
            var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
            var newX = Math.Clamp(sv.Offset.X - (delta * 50), 0, sv.Extent.Width - sv.Viewport.Width);
            sv.Offset = new Vector(newX, sv.Offset.Y);
            e.Handled = true;
        }
    }
}
