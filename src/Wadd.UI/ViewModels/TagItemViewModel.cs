using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Wadd.UI.ViewModels;

public partial class TagItemViewModel : ObservableObject
{
    private readonly Func<string, string, Task> _onRenameAsync;
    private readonly Func<string, Task> _onDeleteAsync;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private int _taskCount;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editingName = string.Empty;

    public TagItemViewModel(string name, int taskCount, Func<string, string, Task> onRenameAsync, Func<string, Task> onDeleteAsync)
    {
        _name = name;
        _taskCount = taskCount;
        _onRenameAsync = onRenameAsync ?? throw new ArgumentNullException(nameof(onRenameAsync));
        _onDeleteAsync = onDeleteAsync ?? throw new ArgumentNullException(nameof(onDeleteAsync));
    }

    [RelayCommand]
    private void StartEdit()
    {
        EditingName = Name;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingName = string.Empty;
        IsEditing = false;
    }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (string.IsNullOrWhiteSpace(EditingName)) return;
        var newName = EditingName.Trim();
        IsEditing = false;

        if (!string.Equals(Name, newName, StringComparison.OrdinalIgnoreCase))
        {
            await _onRenameAsync(Name, newName);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        await _onDeleteAsync(Name);
    }
}
