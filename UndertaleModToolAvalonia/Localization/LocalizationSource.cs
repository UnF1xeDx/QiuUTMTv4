using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace UndertaleModToolAvalonia.Localization;

/// <summary>
/// Singleton localization source that supports runtime language switching.
/// Binds to the generated Strings resource manager and notifies UI on culture change.
/// </summary>
public class LocalizationSource : INotifyPropertyChanged
{
    private static readonly LocalizationSource _instance = new();
    public static LocalizationSource Instance => _instance;

    private readonly ResourceManager _manager;

    public event PropertyChangedEventHandler? PropertyChanged;

    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            if (_culture.Name != value.Name)
            {
                _culture = value;
                CultureInfo.DefaultThreadCurrentUICulture = value;
                Assets.Strings.Culture = value;
                // Notify all indexer bindings to refresh (WPF uses Binding.IndexerName = "Item[]")
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
                // Also notify with null to refresh any other bindings
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            }
        }
    }

    /// <summary>
    /// Indexer for XAML binding: LocalizationSource.Instance["KeyName"]
    /// Returns the localized string, or #KeyName# if not found.
    /// </summary>
    public string this[string key] => _manager.GetString(key, _culture) ?? $"#{key}#";

    /// <summary>
    /// Static helper for code-behind access.
    /// </summary>
    public static string GetString(string key) => Instance[key];

    public LocalizationSource()
    {
        _manager = new ResourceManager("UndertaleModToolAvalonia.Assets.Strings", typeof(LocalizationSource).Assembly);
    }
}
