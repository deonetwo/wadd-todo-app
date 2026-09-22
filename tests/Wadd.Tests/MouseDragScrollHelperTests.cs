using Avalonia.Controls;
using Wadd.UI.Helpers;
using Xunit;

namespace Wadd.Tests;

public class MouseDragScrollHelperTests
{
    [Fact]
    public void IsEnabled_DefaultValue_IsFalse()
    {
        var scrollViewer = new ScrollViewer();
        Assert.False(MouseDragScrollHelper.GetIsEnabled(scrollViewer));
    }

    [Fact]
    public void SetIsEnabled_CanBeEnabledAndDisabled()
    {
        var scrollViewer = new ScrollViewer();

        MouseDragScrollHelper.SetIsEnabled(scrollViewer, true);
        Assert.True(MouseDragScrollHelper.GetIsEnabled(scrollViewer));

        MouseDragScrollHelper.SetIsEnabled(scrollViewer, false);
        Assert.False(MouseDragScrollHelper.GetIsEnabled(scrollViewer));
    }

    [Fact]
    public void SetIsEnabled_MultipleToggle_DoesNotThrow()
    {
        var scrollViewer = new ScrollViewer();

        for (int i = 0; i < 5; i++)
        {
            MouseDragScrollHelper.SetIsEnabled(scrollViewer, true);
            Assert.True(MouseDragScrollHelper.GetIsEnabled(scrollViewer));

            MouseDragScrollHelper.SetIsEnabled(scrollViewer, false);
            Assert.False(MouseDragScrollHelper.GetIsEnabled(scrollViewer));
        }
    }
}
