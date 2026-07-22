using Avalonia;
using Avalonia.Styling;
using Wadd.Core.Enums;
using Wadd.Core.Interfaces;

namespace Wadd.Services;

public class ThemeService : IThemeService
{
    private ThemeMode _currentTheme = ThemeMode.System;

    public ThemeMode CurrentTheme => _currentTheme;

    public event EventHandler<ThemeMode>? ThemeChanged;

    public void SetTheme(ThemeMode mode)
    {
        _currentTheme = mode;
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = mode switch
            {
                ThemeMode.Light => ThemeVariant.Light,
                ThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
        ThemeChanged?.Invoke(this, mode);
    }

    public void ToggleTheme()
    {
        var nextTheme = _currentTheme switch
        {
            ThemeMode.System => ThemeMode.Light,
            ThemeMode.Light => ThemeMode.Dark,
            ThemeMode.Dark => ThemeMode.System,
            _ => ThemeMode.System
        };
        SetTheme(nextTheme);
    }
}
