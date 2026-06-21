using System;
using Avalonia.Data;

namespace UndertaleModToolAvalonia.Localization;

/// <summary>
/// Avalonia markup extension for localization bindings.
/// Usage in XAML: {loc:Loc KeyName}
/// Returns a binding to LocalizationSource.Instance[KeyName] that auto-updates on language change.
/// </summary>
public class LocExtension
{
    public string Key { get; }

    public LocExtension(string key)
    {
        Key = key;
    }

    public object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding
        {
            Source = LocalizationSource.Instance,
            Path = $"Item[{Key}]",
            Mode = BindingMode.OneWay,
        };
    }
}
