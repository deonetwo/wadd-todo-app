using System;
using System.ComponentModel;
using System.Globalization;
using Wadd.Core.Localization;

namespace Wadd.UI.Localization;

public class LocalizationManager : Wadd.Core.Interfaces.ILocalizationService
{
    private static LocalizationManager? _instance;
    public static LocalizationManager Instance => _instance ??= new LocalizationManager();

    private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;
    private string _currentLanguage = "system";

    public event Action? CultureChanged;

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        private set
        {
            _currentCulture = value;
            Strings.Culture = value;
            CultureInfo.CurrentUICulture = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;

            OnPropertyChanged(nameof(CurrentCulture));
            OnPropertyChanged(nameof(CurrentLanguage));
            OnPropertyChanged("Item");
            OnPropertyChanged("Item[]");
            OnPropertyChanged(string.Empty);
            CultureChanged?.Invoke();
        }
    }

    public string CurrentLanguage => _currentLanguage;

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            var localized = Strings.Get(key, _currentCulture);
            return localized ?? key;
        }
    }

    public string GetString(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        var localized = Strings.Get(key, _currentCulture);
        return localized ?? key;
    }

    public void SetLanguage(string? languageCode)
    {
        _currentLanguage = string.IsNullOrWhiteSpace(languageCode) ? "system" : languageCode;

        CultureInfo targetCulture;
        if (_currentLanguage.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            var systemCulture = CultureInfo.CurrentUICulture ?? CultureInfo.InstalledUICulture;
            if (systemCulture.TwoLetterISOLanguageName.Equals("id", StringComparison.OrdinalIgnoreCase))
            {
                targetCulture = new CultureInfo("id-ID");
            }
            else
            {
                targetCulture = new CultureInfo("en-US");
            }
        }
        else
        {
            try
            {
                if (_currentLanguage.StartsWith("id", StringComparison.OrdinalIgnoreCase))
                {
                    targetCulture = new CultureInfo("id-ID");
                }
                else
                {
                    targetCulture = new CultureInfo("en-US");
                }
            }
            catch
            {
                targetCulture = new CultureInfo("en-US");
            }
        }

        CurrentCulture = targetCulture;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
