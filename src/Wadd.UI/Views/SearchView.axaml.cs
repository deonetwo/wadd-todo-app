using Avalonia.Controls;
using Wadd.UI.ViewModels;

namespace Wadd.UI.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
        Loaded += SearchView_Loaded;
    }

    private void SearchView_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            if (string.IsNullOrWhiteSpace(vm.SearchStatusFilter))
            {
                vm.SearchStatusFilter = "All";
            }
            vm.IsSearchFilterAll = (vm.SearchStatusFilter == "All");
        }
    }
}
