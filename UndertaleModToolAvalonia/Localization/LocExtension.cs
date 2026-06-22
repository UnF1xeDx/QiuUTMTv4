using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace UndertaleModToolAvalonia.Localization;

/// <summary>
/// Avalonia markup extension for localization bindings.
/// Usage in XAML: {loc:Loc KeyName}
/// Returns a binding to LocalizationSource.Instance[KeyName] that auto-updates on language change.
/// </summary>
public class LocExtension : MarkupExtension
{
    public string Key { get; }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // In Avalonia, the indexer path syntax is [key], not Item[key].
        // The Binding class uses reflection-based binding by default,
        // which supports string indexers.
        return new Binding
        {
            Source = LocalizationSource.Instance,
            Path = $"[{Key}]",
            Mode = BindingMode.OneWay,
        };
    }
}
