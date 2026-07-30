using Avalonia;
using Avalonia.Styling;
using Wadd.Core.Enums;
using Wadd.Core.Interfaces;

namespace Wadd.Services;

public class ThemeService : IThemeService
{
    private ThemeMode _currentTheme = ThemeMode.System;

    public ThemeMode CurrentTheme => _currentTheme;

    public bool IsDarkMode
    {
        get
        {
            if (_currentTheme == ThemeMode.Dark) return true;
            if (_currentTheme == ThemeMode.Light) return false;
            return Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        }
    }

    public event EventHandler<ThemeMode>? ThemeChanged;

    public ThemeService()
    {
        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += (s, e) =>
            {
                ThemeChanged?.Invoke(this, _currentTheme);
            };
        }
    }

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
        var nextTheme = IsDarkMode ? ThemeMode.Light : ThemeMode.Dark;
        SetTheme(nextTheme);
    }
}
