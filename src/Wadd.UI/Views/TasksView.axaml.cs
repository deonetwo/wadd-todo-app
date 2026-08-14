using Avalonia.Controls;
using Avalonia.Input;
using Wadd.UI.ViewModels;

namespace Wadd.UI.Views;

public partial class TasksView : UserControl
{
    public TasksView()
    {
        InitializeComponent();
    }

    private async void OnRefreshRequested(object? sender, RefreshRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (DataContext is MainViewModel vm)
            {
                if (vm.LoadTodoItemsCommand.CanExecute(null))
                {
                    await vm.LoadTodoItemsCommand.ExecuteAsync(null);
                }
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnNoteInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (DataContext is MainViewModel vm && vm.AddTaskCommand.CanExecute(null))
            {
                vm.AddTaskCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void OnTitleInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.IsNoteComposerExpanded = true;
            }
        }
    }
}
