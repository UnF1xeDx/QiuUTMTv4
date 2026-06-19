using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using PropertyChanged.SourceGenerator;
using Semi.Avalonia;

namespace UndertaleModToolAvalonia;

public partial class SettingsFile
{
    public MainViewModel MainVM = null!;

    public static SettingsFile? Instance { get; private set; }

    public SettingsFile()
    {
        Instance = this;
    }

    public SettingsFile(IServiceProvider serviceProvider)
    {
        MainVM = serviceProvider.GetRequiredService<MainViewModel>();
        Instance = this;
    }

    public static SettingsFile LoadWithoutMainVM()
    {

        SettingsFile? settings = null;

        string roamingAppData =
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QiuUTMTv4");

        // Load Settings.json
        string settingsPath = Path.Join(roamingAppData, "Settings.json");

        if (File.Exists(settingsPath))
        {
            try
            {
                string json = File.ReadAllText(settingsPath);
                settings = JsonSerializer.Deserialize<SettingsFile>(json, new JsonSerializerOptions()
                {
                    AllowTrailingCommas = true,
                });

                if (settings is not null)
                {
                    settings.Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?.?.?.?";
                }
            }
            catch (Exception e)
            {}
        }

        return settings;
    }

    public static SettingsFile Load(IServiceProvider serviceProvider)
    {
        MainViewModel mainVM = serviceProvider.GetRequiredService<MainViewModel>();

        SettingsFile? settings = null;

        string roamingAppData =
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QiuUTMTv4");

        // Load Settings.json
        string settingsPath = Path.Join(roamingAppData, "Settings.json");

        if (File.Exists(settingsPath))
        {
            try
            {
                string json = File.ReadAllText(settingsPath);
                settings = JsonSerializer.Deserialize<SettingsFile>(json, new JsonSerializerOptions()
                {
                    AllowTrailingCommas = true,
                });

                if (settings is not null)
                {
                    // Check for upgrades here.
                    settings.MainVM = mainVM;
                    settings.Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?.?.?.?";
                }
            }
            catch (Exception e)
            {
                mainVM.LazyErrorMessages.Add(
                    $"Error when loading settings file:\n{e.Message}\nDefault settings loaded.");
            }
        }

        settings ??= new SettingsFile(serviceProvider);

        // Load Styles.xaml
        string stylesPath = Path.Join(roamingAppData, "Styles.xaml");

        if (File.Exists(stylesPath))
        {
            try
            {
                string xaml = File.ReadAllText(stylesPath);
                Styles styles = AvaloniaRuntimeXamlLoader.Parse<Styles>(xaml);

                if (App.CurrentCustomStyles is not null)
                    App.Current!.Styles.Remove(App.CurrentCustomStyles);

                App.CurrentCustomStyles = styles;
                App.Current!.Styles.Add(styles);
            }
            catch (Exception e)
            {
                mainVM.LazyErrorMessages.Add($"Error when loading styles file:\n{e.Message}");
            }
        }

        return settings;
    }

    public async void Save()
    {
        string roamingAppData =
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QiuUTMTv4");
        Directory.CreateDirectory(roamingAppData);

        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions()
        {
            WriteIndented = true,
        });

        try
        {
            File.WriteAllText(Path.Join(roamingAppData, "Settings.json"), json);
        }
        catch (Exception e)
        {
            await MainVM.ShowMessageDialog($"Error when saving settings file: {e.Message}");
        }
    }

    public string Version { get; set; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?.?.?.?";

    public enum ThemeValue
    {
        SystemDefault = 0,
        Light = 1,
        Dark = 2,
    }

    /// <summary>
    /// Represents a supported language with its culture code and native display name.
    /// </summary>
    public class LanguageInfo
    {
        public string Code { get; }
        public string NativeName { get; }

        public LanguageInfo(string code, string nativeName)
        {
            Code = code;
            NativeName = nativeName;
        }
    }

    /// <summary>
    /// Supported languages with their culture code and display name.
    /// To add a new language: 1) Add entry here, 2) Create Strings.{code}.resx, 3) Add it to csproj.
    /// </summary>
    public static readonly LanguageInfo[] SupportedLanguages =
    [
        new("", "Auto Detect"),   // auto detect - uses system culture
        new("zh", "简体中文"),
        new("en", "English"),
        new("ja", "日本語"),
    ];

    [Notify] private ThemeValue _Theme;

    [Notify] private string _LanguageCode = ""; // empty = auto detect

    /// <summary>
    /// Legacy property for backward compatibility with old Settings.json files.
    /// Old format: Language (int enum: 0=AutoDetect, 1=Zh, 2=En)
    /// New format: LanguageCode (string: "", "zh", "en", "ja", etc.)
    /// When deserializing, if LanguageCode is empty but Language has a value, it migrates.
    /// </summary>
    public int? Language
    {
        get => null; // Always return null so it's not serialized anymore
        set
        {
            if (value.HasValue && string.IsNullOrEmpty(LanguageCode))
            {
                LanguageCode = value.Value switch
                {
                    1 => "zh",
                    2 => "en",
                    _ => ""
                };
            }
        }
    }

    /// <summary>
    /// Index into SupportedLanguages array, for ComboBox binding.
    /// Syncs with LanguageCode bidirectionally.
    /// </summary>
    public int LanguageIndex
    {
        get
        {
            for (int i = 0; i < SupportedLanguages.Length; i++)
            {
                if (SupportedLanguages[i].Code == LanguageCode)
                    return i;
            }
            return 0; // default to auto detect
        }
        set
        {
            if (value >= 0 && value < SupportedLanguages.Length)
            {
                LanguageCode = SupportedLanguages[value].Code;
            }
        }
    }

    void OnThemeChanged()
    {
        if (App.Current is not null)
        {
            App.Current.RequestedThemeVariant = Theme switch
            {
                ThemeValue.SystemDefault => ThemeVariant.Default,
                ThemeValue.Light => ThemeVariant.Light,
                ThemeValue.Dark => ThemeVariant.Dark,
                _ => throw new NotImplementedException(),
            };
        }
    }

    public void OnLanguageChanged()
    {
        if (App.Current is not null)
        {
            CultureInfo culture = GetCultureInfoFromSetting();
            Thread.CurrentThread.CurrentUICulture = Assets.Strings.Culture = culture;
            SemiTheme.OverrideLocaleResources(Application.Current, culture);
            Save();
        }
    }

    public CultureInfo GetCultureInfoFromSetting()
    {
        if (string.IsNullOrEmpty(LanguageCode))
            return Thread.CurrentThread.CurrentCulture;

        try
        {
            return new CultureInfo(LanguageCode, false);
        }
        catch (CultureNotFoundException)
        {
            return Thread.CurrentThread.CurrentCulture;
        }
    }

    public bool OpenNewResourceAfterCreatingIt { get; set; } = false;
    public bool EnableSyntaxHighlighting { get; set; } = true;
    public bool AutomaticallyCompileAndDecompileCodeOnLostFocus { get; set; } = true;

    public bool EnableRoomGridByDefault { get; set; } = false;
    public uint DefaultRoomGridWidth { get; set; } = 20;
    public uint DefaultRoomGridHeight { get; set; } = 20;

    public string InstanceIdPrefix { get; set; } = "inst_";

    public bool EnableQiuUtmtV3ScriptEngine { get; set; } = true;
    
    public bool UseSoraEditor { get; set; } = true;

    [Notify] private bool _ChangeTrackingEnabled = true;
    [Notify] private bool _CodeEditorWordWrap = false;
    [Notify] private bool _CodeEditorShowWhitespace = false;
    [Notify] private bool _CodeEditorShowHoverInfo = true;
    [Notify] private bool _RecompileAllCodeSourcesOnProjectSave = false;

    public List<string> RecentFiles { get; set; } = [];

    public Underanalyzer.Decompiler.DecompileSettings DecompileSettings { get; set; } = new();
}