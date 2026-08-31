using System;
using Avalonia.Controls;

namespace Wadd.UI.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnAiModelFlyoutOpening(object? sender, EventArgs e)
    {
        if (AiModelDropDownButton != null && AiModelFlyoutContainer != null)
        {
            var btnWidth = AiModelDropDownButton.Bounds.Width;
            if (btnWidth > 50)
            {
                // Account for FlyoutPresenter internal padding and border
                AiModelFlyoutContainer.Width = Math.Max(150, btnWidth - 22);
            }
        }
    }
}
