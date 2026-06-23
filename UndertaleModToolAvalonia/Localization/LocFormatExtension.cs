using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace UndertaleModToolAvalonia.Localization;

/// <summary>
/// Avalonia markup extension for localized StringFormat bindings.
/// Usage in XAML: {loc:LocFormat Key=UndertaleSpriteViewModelHeading, BindingPath=Sprite.Name.Content}
/// Returns a MultiBinding that combines a localized format string with a bound argument.
/// Supports runtime language switching.
/// </summary>
public class LocFormatExtension : MarkupExtension
{
    public string? Key { get; set; }
    public string? BindingPath { get; set; }

    public LocFormatExtension() { }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new MultiBinding
        {
            Bindings = new List<IBinding>
            {
                new Binding
                {
                    Source = LocalizationSource.Instance,
                    Path = $"[{Key}]",
                    Mode = BindingMode.OneWay,
                },
                new Binding
                {
                    Path = BindingPath ?? "",
                    Mode = BindingMode.OneWay,
                },
            },
            Converter = FormatStringConverter.Instance,
            Mode = BindingMode.OneWay,
        };
    }
}

internal sealed class FormatStringConverter : IMultiValueConverter
{
    public static readonly FormatStringConverter Instance = new();

    public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is string format && values[1] is not null)
        {
            try
            {
                return string.Format(format, values[1]);
            }
            catch
            {
                return values[1]?.ToString() ?? "";
            }
        }

        if (values.Count >= 1 && values[0] is string s)
            return s;

        return "";
    }
}
