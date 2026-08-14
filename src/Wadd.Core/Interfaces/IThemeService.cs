using Wadd.Core.Enums;

namespace Wadd.Core.Interfaces;

public interface IThemeService
{
    ThemeMode CurrentTheme { get; }
    event EventHandler<ThemeMode>? ThemeChanged;
    void SetTheme(ThemeMode mode);
    void ToggleTheme();
    void LoadTheme();
    void SaveTheme();
}

