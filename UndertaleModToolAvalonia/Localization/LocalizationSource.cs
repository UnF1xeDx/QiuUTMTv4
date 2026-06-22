using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;

namespace UndertaleModToolAvalonia.Localization;

/// <summary>
/// A ResourceManager that tries the culture-specific manager first,
/// then falls back to the default manager for missing keys.
/// This is used to patch the generated Strings.Designer.cs's resourceMan field
/// so that x:Static bindings also benefit from the Android satellite-assembly workaround.
/// </summary>
internal sealed class FallbackResourceManager : ResourceManager
{
    private readonly ResourceManager _primary;
    private readonly ResourceManager _fallback;

    public FallbackResourceManager(ResourceManager primary, ResourceManager fallback)
    {
        _primary = primary;
        _fallback = fallback;
    }

    public override string? GetString(string name, CultureInfo? culture)
    {
        var value = _primary.GetString(name, culture);
        if (!string.IsNullOrEmpty(value))
            return value;
        return _fallback.GetString(name, culture);
    }

    public override string? GetString(string name)
    {
        var value = _primary.GetString(name);
        if (!string.IsNullOrEmpty(value))
            return value;
        return _fallback.GetString(name);
    }
}

/// <summary>
/// Singleton localization source that supports runtime language switching.
/// Binds to the generated Strings resource manager and notifies UI on culture change.
/// </summary>
public class LocalizationSource : INotifyPropertyChanged
{
    private static readonly LocalizationSource _instance = new();
    public static LocalizationSource Instance => _instance;

    private readonly ResourceManager _manager;
    private ResourceManager? _cultureManager;

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
                _cultureManager = LoadCultureManager(value);
                CultureInfo.DefaultThreadCurrentUICulture = value;
                Assets.Strings.Culture = value;
                // Patch the generated Strings.Designer.cs's cached ResourceManager so that
                // x:Static bindings (which bypass LocalizationSource) also use the
                // culture-specific satellite assembly loaded by LoadCultureManager.
                // Without this, x:Static bindings fall back to English on Android where
                // standard satellite-assembly probing does not work.
                PatchStringsResourceManager();
                // Notify all indexer bindings to refresh (WPF uses Binding.IndexerName = "Item[]")
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
                // Also notify with null to refresh any other bindings
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            }
        }
    }

    /// <summary>
    /// Replaces the private static resourceMan field in the generated Strings.Designer.cs
    /// with a FallbackResourceManager that uses the culture-specific satellite assembly.
    /// This makes x:Static strings:Strings.X bindings return localized values on platforms
    /// (e.g. Android) where ResourceManager's standard satellite-assembly probing fails.
    /// </summary>
    private void PatchStringsResourceManager()
    {
        if (_cultureManager == null)
            return;

        try
        {
            var field = typeof(Assets.Strings).GetField("resourceMan",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(null, new FallbackResourceManager(_cultureManager, _manager));
            }
        }
        catch
        {
            // Ignore reflection errors and fall back to default behavior
        }
    }

    /// <summary>
    /// Indexer for XAML binding: LocalizationSource.Instance["KeyName"]
    /// Returns the localized string, or #KeyName# if not found.
    /// </summary>
    public string this[string key]
    {
        get
        {
            // Try the culture-specific manager first (loaded from satellite assembly)
            if (_cultureManager != null)
            {
                var value = _cultureManager.GetString(key, _culture);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            // Fall back to the default manager (uses ResourceManager satellite probing)
            return _manager.GetString(key, _culture) ?? $"#{key}#";
        }
    }

    /// <summary>
    /// Static helper for code-behind access.
    /// </summary>
    public static string GetString(string key) => Instance[key];

    public LocalizationSource()
    {
        _manager = new ResourceManager("UndertaleModToolAvalonia.Assets.Strings", typeof(LocalizationSource).Assembly);
        // On platforms like Android, standard satellite-assembly probing does not work,
        // so we eagerly load the culture-specific manager for the current culture and
        // patch the generated Strings.Designer.cs. This ensures x:Static bindings return
        // localized values even when the Culture setter is never called (e.g. when the
        // configured language equals the system UI culture).
        _cultureManager = LoadCultureManager(_culture);
        PatchStringsResourceManager();
    }

    /// <summary>
    /// Manually load a satellite assembly for the given culture.
    /// This is needed on Android where the runtime's satellite assembly probing
    /// may not find assemblies extracted to the .__override__ directory.
    /// </summary>
    private ResourceManager? LoadCultureManager(CultureInfo culture)
    {
        if (string.IsNullOrEmpty(culture.Name))
            return null;

        var assemblyName = typeof(LocalizationSource).Assembly.GetName().Name;
        var fileName = $"{assemblyName}.resources.dll";
        var baseDir = AppContext.BaseDirectory;

        try
        {
            // Search for the satellite assembly in the base directory and subdirectories.
            // On Android, it's typically at .__override__/{abi}/{culture}/UndertaleModToolAvalonia.resources.dll
            var files = Directory.GetFiles(baseDir, fileName, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                // Check if the file is in a directory named after the culture
                var dir = Path.GetDirectoryName(file);
                if (dir != null && new DirectoryInfo(dir).Name == culture.Name)
                {
                    var satAssembly = Assembly.LoadFrom(file);
                    return new ResourceManager("UndertaleModToolAvalonia.Assets.Strings", satAssembly);
                }
            }
        }
        catch
        {
            // Ignore errors and fall back to the default manager
        }

        return null;
    }
}
