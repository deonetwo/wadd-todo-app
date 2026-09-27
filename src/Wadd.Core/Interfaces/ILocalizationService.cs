using System;
using System.ComponentModel;
using System.Globalization;

namespace Wadd.Core.Interfaces;

/// <summary>
/// Service providing runtime-switchable string localization for views and ViewModels.
/// Implements INotifyPropertyChanged so Avalonia bindings react immediately when active culture changes.
/// </summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    CultureInfo CurrentCulture { get; }
    string CurrentLanguage { get; }
    string this[string key] { get; }
    string GetString(string key);
    void SetLanguage(string? languageCode);
    event Action? CultureChanged;
}
