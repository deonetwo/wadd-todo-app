using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Wadd.UI.Localization;

public class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; }

    public LocalizeExtension()
    {
        Key = string.Empty;
    }

    public LocalizeExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay
        };
    }
}
