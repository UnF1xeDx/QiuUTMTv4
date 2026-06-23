using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DynamicData;
using DynamicData.Alias;
using DynamicData.Binding;
using DynamicData.PLinq;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using PropertyChanged.SourceGenerator;
using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Project;
using UndertaleModToolAvalonia.QiuIO;
using UTMTdrid;
using FilePickerFileType = Avalonia.Platform.Storage.FilePickerFileType;

namespace UndertaleModToolAvalonia;

public partial class MainViewModel
{
    // Set this when testing.
    public IView? View;

    // Services
    public readonly IServiceProvider ServiceProvider;

    /// <summary>Error messages to be displayed after the view has been loaded.</summary>
    public List<string> LazyErrorMessages = [];

    // Settings
    public SettingsFile? Settings { get; set; }

    // Scripting
    public Scripting Scripting = null!;

    // Window
    public string Title => $"QiuUTMTv4 - 秋冥 - v" +
                           (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?.?.?.?") +
                           $"{(Data?.GeneralInfo is not null ? " - " + Data.GeneralInfo.ToString() : "")}" +
                           $"{(DataPath is not null ? " [" + DataPath + "]" : "")}";

    [Notify] private bool _IsEnabled = true;

    /// <summary>
    /// Indicates whether an overlay dialog (DialogOverlay or TextInputBox) is currently visible.
    /// Used to hide native controls (like SoraEditor) that would render on top of overlays on Android.
    /// </summary>
    [Notify] private bool _IsOverlayActive;

    // Data
    [Notify] private UndertaleData? _Data;
    [Notify] private string? _DataPath;
    [Notify] private (uint Major, uint Minor, uint Release, uint Build) _DataVersion;

    // Project
    [Notify] private ProjectContext? _Project;

    IReadOnlyList<FilePickerFileType> dataFileTypes =
    [
        new FilePickerFileType("GameMaker data files (.win, .unx, .ios, .droid, audiogroup*.dat)")
        {
            Patterns = ["*.win", "*.unx", "*.ios", "*.droid", "audiogroup*.dat"],
        },
        new FilePickerFileType("All files")
        {
            Patterns = ["*"],
        },
    ];

    // Tree data grid
    public partial class TreeDataGridItem
    {
        [Notify] private string _Text = "<unset text!>";
        public object? Value { get; set; }
        public object? Tag { get; set; }
        [Notify] private IList<TreeDataGridItem>? _Children;
    }

    [Notify] private ObservableCollection<TreeDataGridItem> _TreeDataGridData = [];

    public BehaviorSubject<string> filterTextSubject = new("");

    public string FilterText
    {
        get { return filterTextSubject.Value; }
        set { filterTextSubject.OnNext(value); }
    }

    // Tabs
    public ObservableCollection<TabItemViewModel> Tabs { get; set; }

    public ObservableCollection<MenuItem> ScriptsSubMenuItems { get; set; }

    [Notify] private TabItemViewModel? _TabSelected;
    [Notify] private int _TabSelectedIndex;
    [Notify] private string _TabSelectedResourceIdString = "None";

    // Command text box
    [Notify] private string _CommandTextBoxText = "";

    // Image cache
    public ImageCache ImageCache = new();

    // Internal clipboard
    public object? InternalClipboard = null;

    public MainViewModel(IServiceProvider? serviceProvider = null)
    {
        ServiceProvider = serviceProvider ?? App.Services;

        Tabs =
        [
            new TabItemViewModel(new DescriptionViewModel(
                    UndertaleModToolAvalonia.Assets.Strings.Welcome,
                    UndertaleModToolAvalonia.Assets.Strings.Welcome_to_Qiumen_s_special_version),
                isSelected: true),
        ];

        Me = this;
        if (OperatingSystem.IsAndroid()||OperatingSystem.IsWindows())
            GenerateScriptsSubMenuItems();
    }

    public static MainViewModel Me;

    public void Initialize()
    {
        Settings = SettingsFile.Load(ServiceProvider);
        Settings.OnLanguageChanged();
        Scripting = new(ServiceProvider);
        UpdateRecentFilesMenu();

        // Re-apply custom background when settings change
        Settings.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(SettingsFile.BackgroundImagePath)
                or nameof(SettingsFile.BackgroundOpacity)
                or nameof(SettingsFile.BackgroundStretchMode))
            {
                if (View is MainView mainView)
                    mainView.ApplyCustomBackground();
            }
        };

        if (View is MainView mainView)
            mainView.ApplyCustomBackground();
    }

    public async void OnLoaded()
    {
        foreach (string message in LazyErrorMessages)
        {
            await ShowMessageDialog(message);
        }

        LazyErrorMessages.Clear();

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.Args?.Length >= 1)
            {
                try
                {
                    using FileStream stream = File.OpenRead(desktop.Args[0]);
                    if (await LoadData(stream))
                    {
                        DataPath = stream.Name;
                    }
                }
                catch (SystemException e)
                {
                    await ShowMessageDialog($"Error opening data file from argument: {e.Message}");
                }
            }
        }
    }

    // Called by [Notify]
    public void OnDataChanged()
    {
        if (Data is not null)
        {
            if (Data.GeneralInfo is not null)
                Data.GeneralInfo.PropertyChanged += DataGeneralInfoChangedHandler;
            Data.ToolInfo.InstanceIdPrefix = () => Settings?.InstanceIdPrefix;
            Data.ToolInfo.DecompilerSettings = Settings?.DecompileSettings;
        }

        UpdateVersion();

        TreeDataGridData.Clear();

        if (Data is not null)
        {
            ReadOnlyObservableCollection<TreeDataGridItem>? MakeChildren<T>(IList<T> list) where T : notnull
            {
                if (list is ObservableCollection<T> collection)
                {
                    collection
                        .ToObservableChangeSet()
                        .Filter(filterTextSubject.Select<string, Func<T, bool>>(filterText => item =>
                        {
                            string? name = item switch
                            {
                                UndertaleNamedResource namedResource => namedResource.Name.Content,
                                UndertaleString _string => _string.Content,
                                _ => null,
                            };

                            if (name is null)
                                return true;

                            return name.Contains(filterText, System.StringComparison.CurrentCultureIgnoreCase);
                        }))
                        .Transform(x => new TreeDataGridItem() { Text = "", Value = x })
                        .Bind(out ReadOnlyObservableCollection<TreeDataGridItem> readOnlyCollection)
                        .Subscribe();
                    return readOnlyCollection;
                }

                return null;
            }

            TreeDataGridData.Add(new()
            {
                Value = Data,
                Text = "数据文件(Data)",
                Children =
                [
                    new() { Value = "GeneralInfo", Text = "基本信息(GeneralInfo)" },
                    new() { Value = "GlobalInitScripts", Text = "全局入口脚本(GlobalInitScripts)" },
                    new() { Value = "GameEndScripts", Text = "游戏终止脚本(GameEndScripts)" },
                    new()
                    {
                        Tag = "list", Value = "AudioGroups", Text = "音频组(AudioGroups)",
                        Children = MakeChildren(Data.AudioGroups)
                    },
                    new()
                    {
                        Tag = "list", Value = "Sounds", Text = "声音(Sounds)",
                        Children = MakeChildren(Data.Sounds)
                    },
                    new()
                    {
                        Tag = "list", Value = "Sprites", Text = "精灵(Sprites)",
                        Children = MakeChildren(Data.Sprites)
                    },
                    new()
                    {
                        Tag = "list", Value = "Backgrounds", Text = "背景 & 图块集(Backgrounds & Tiles)",
                        Children = MakeChildren(Data.Backgrounds)
                    },
                    new()
                    {
                        Tag = "list", Value = "Paths", Text = "路径(Paths)",
                        Children = MakeChildren(Data.Paths)
                    },
                    new()
                    {
                        Tag = "list", Value = "Scripts", Text = "脚本(Scripts)",
                        Children = MakeChildren(Data.Scripts)
                    },
                    new()
                    {
                        Tag = "list", Value = "Shaders", Text = "着色器(Shaders)",
                        Children = MakeChildren(Data.Shaders)
                    },
                    new()
                    {
                        Tag = "list", Value = "Fonts", Text = "字体(Fonts)",
                        Children = MakeChildren(Data.Fonts)
                    },
                    new()
                    {
                        Tag = "list", Value = "Timelines", Text = "时间线(Time lines)",
                        Children = MakeChildren(Data.Timelines)
                    },
                    new()
                    {
                        Tag = "list", Value = "GameObjects", Text = "对象(Game Objects)",
                        Children = MakeChildren(Data.GameObjects)
                    },
                    new()
                    {
                        Tag = "list", Value = "Rooms", Text = "房间(Rooms)",
                        Children = MakeChildren(Data.Rooms)
                    },
                    new()
                    {
                        Tag = "list", Value = "Extensions", Text = "扩展(Extensions)",
                        Children = MakeChildren(Data.Extensions)
                    },
                    new()
                    {
                        Tag = "list", Value = "TexturePageItems", Text = "纹理页子项(Texture Page Items)",
                        Children = MakeChildren(Data.TexturePageItems)
                    },
                    new()
                    {
                        Tag = "list", Value = "Code", Text = "代码(Code)",
                        Children = MakeChildren(Data.Code)
                    },
                    new()
                    {
                        Tag = "list", Value = "Variables", Text = "变量(Variables)",
                        Children = MakeChildren(Data.Variables)
                    },
                    new()
                    {
                        Tag = "list", Value = "Functions", Text = "函数(Functions)",
                        Children = MakeChildren(Data.Functions)
                    },
                    new()
                    {
                        Tag = "list", Value = "CodeLocals", Text = "本地变量(CodeLocals)",
                        Children = MakeChildren(Data.CodeLocals)
                    },
                    new()
                    {
                        Tag = "list", Value = "Strings", Text = "字符串(Strings)",
                        Children = MakeChildren(Data.Strings)
                    },
                    new()
                    {
                        Tag = "list", Value = "EmbeddedTextures", Text = "内嵌纹理(Embedded textures)",
                        Children = MakeChildren(Data.EmbeddedTextures)
                    },
                    new()
                    {
                        Tag = "list", Value = "EmbeddedAudio", Text = "内嵌音频(Embedded audio)",
                        Children = MakeChildren(Data.EmbeddedAudio)
                    },
                    new()
                    {
                        Tag = "list", Value = "TextureGroupInformation", Text = "内嵌纹理(Texture group information)",
                        Children = MakeChildren(Data.TextureGroupInfo)
                    },
                    new()
                    {
                        Tag = "list", Value = "EmbeddedImages", Text = "内嵌图像(Embedded images)",
                        Children = MakeChildren(Data.EmbeddedImages)
                    },
                    new()
                    {
                        Tag = "list", Value = "ParticleSystems", Text = "粒子系统(Particle systems)",
                        Children = MakeChildren(Data.ParticleSystems)
                    },
                    new()
                    {
                        Tag = "list", Value = "ParticleSystemEmitters", Text = "粒子系统发射器(Particle system emitters)",
                        Children = MakeChildren(Data.ParticleSystemEmitters)
                    },
                ]
            });
        }
    }

    public async Task<DialogResult> ShowMessageDialog(string message, string? title = null, bool ok = true,
        bool yes = false, bool no = false, bool cancel = false)
    {
        return await View!.MessageDialog(message, title, ok, yes, no, cancel);
    }

    /// <summary>Ask if user wants to save the current file before continuing.
    /// Returns true if either it saved successfully, or if the user didn't want to save, or if there is no file loaded.</summary>
    public async Task<bool> AskFileSave(string message)
    {
        if (Data is null)
            return true;

        var result = await ShowMessageDialog(message, ok: false, yes: true, no: true, cancel: true);
        if (result == DialogResult.Yes)
        {
            if (await FileSave())
            {
                return true;
            }
        }
        else if (result == DialogResult.No)
        {
            return true;
        }

        return false;
    }

    public Task<bool> NewData()
    {
        CloseData();

        Data = UndertaleData.CreateNew();
        DataPath = null;

        return Task.FromResult(true);
    }

    public async Task<bool> LoadData(Stream stream)
    {
        IsEnabled = false;

        ILoaderWindow w = View!.LoaderOpen();
        w.SetText("Opening data file...");

        try
        {
            List<string> warnings = [];
            bool hadImportantWarnings = false;

            UndertaleData data = await Task.Run(() => UndertaleIO.Read(stream,
                (string warning, bool isImportant) =>
                {
                    warnings.Add(warning);
                    if (isImportant)
                    {
                        hadImportantWarnings = true;
                    }
                },
                (string message) => { Dispatcher.UIThread.Post(() => w.SetText($"Opening data file... {message}")); })
            );

            if (warnings.Count > 0)
            {
                w.EnsureShown();
                await ShowMessageDialog($"Warnings occurred when loading the data file:\n\n" +
                                        $"{(hadImportantWarnings ? "Data loss will likely occur when trying to save.\n" : "")}" +
                                        $"{String.Join("\n", warnings)}");
            }

            // TODO: Add other checks for possible stuff.

            Data = data;

            return true;
        }
        catch (Exception e)
        {
            w.EnsureShown();
            Console.WriteLine(e);
            await ShowMessageDialog($"打开数据文件失败: {e.Message} \n {e.StackTrace}");

            return false;
        }
        finally
        {
            IsEnabled = true;
            w.Close();
        }
    }

    public async Task<bool> SaveData(Stream stream)
    {
        IsEnabled = false;

        ILoaderWindow w = View!.LoaderOpen();
        w.SetText(UndertaleModToolAvalonia.Assets.Strings.Saving);

        try
        {
            await Task.Run(() => UndertaleIO.Write(stream, Data,
                message => { Dispatcher.UIThread.Post(() => w.SetText($"Saving... {message}")); }));

            return true;
        }
        catch (Exception e)
        {
            w.EnsureShown();
            await ShowMessageDialog($"Save failed: {e.Message}");
        }
        finally
        {
            IsEnabled = true;
            w.Close();
        }

        return false;
    }

    public void CloseData()
    {
        Data = null;
        DataPath = null;

        Tabs.Clear();
    }

    public void UpdateVersion()
    {
        DataVersion = Data is not null && Data.GeneralInfo is not null
            ? (Data.GeneralInfo.Major, Data.GeneralInfo.Minor, Data.GeneralInfo.Release, Data.GeneralInfo.Build)
            : default;
    }

    private void DataGeneralInfoChangedHandler(object? sender, PropertyChangedEventArgs e)
    {
        if (Data is not null && e.PropertyName is
                nameof(UndertaleGeneralInfo.Major) or nameof(UndertaleGeneralInfo.Minor) or
                nameof(UndertaleGeneralInfo.Release) or nameof(UndertaleGeneralInfo.Build))
        {
            UpdateVersion();
        }
    }

    // Menus
    public async void FileNew()
    {
        if (await AskFileSave(UndertaleModToolAvalonia.Assets.Strings.Save_before_creating))
        {
            await NewData();
        }
    }

    public void AddRecentFile(string path)
    {
        if (string.IsNullOrEmpty(path) || Settings is null)
            return;

        // Remove if already exists, then insert at top
        Settings.RecentFiles.Remove(path);
        Settings.RecentFiles.Insert(0, path);

        // Keep max 10 items
        while (Settings.RecentFiles.Count > 10)
            Settings.RecentFiles.RemoveAt(Settings.RecentFiles.Count - 1);

        Settings.Save();
        UpdateRecentFilesMenu();
    }

    public void UpdateRecentFilesMenu()
    {
        if (View is not MainView mainView)
            return;

        var recentFilesMenu = mainView.FindControl<MenuItem>("RecentFilesMenu");
        if (recentFilesMenu is null)
            return;

        recentFilesMenu.ItemsSource = null;
        if (Settings?.RecentFiles is null || Settings.RecentFiles.Count == 0)
        {
            recentFilesMenu.IsEnabled = false;
            return;
        }

        recentFilesMenu.IsEnabled = true;
        var items = new List<MenuItem>();
        foreach (string filePath in Settings.RecentFiles)
        {
            var item = new MenuItem
            {
                Header = filePath,
            };
            ToolTip.SetTip(item, filePath);
            string capturedPath = filePath;
            item.Click += async (_, _) =>
            {
                if (!await AskFileSave(UndertaleModToolAvalonia.Assets.Strings.Save_before_opening))
                    return;

                CloseData();

                try
                {
                    using Stream stream = File.OpenRead(capturedPath);
                    if (await LoadData(stream))
                    {
                        DataPath = capturedPath;
                        AddRecentFile(capturedPath);
                    }
                }
                catch (SystemException e)
                {
                    await ShowMessageDialog($"Error opening recent file: {e.Message}");
                }
            };
            items.Add(item);
        }
        recentFilesMenu.ItemsSource = items;
    }

    public async Task FileOpen()
    {
        if (OperatingSystem.IsAndroid())
        {
            if (!await MAUIBridge.HasRequiredStoragePermission()) return;
        }

        if (!await AskFileSave(UndertaleModToolAvalonia.Assets.Strings.Save_before_opening))
            return;

        var files = await View!.OpenFileDialog(new FilePickerOpenOptions()
        {
            Title = "Open data file",
            FileTypeFilter = dataFileTypes,
        });

        if (files is null || files.Count != 1)
            return;

        CloseData();

        using Stream stream = await files[0].OpenReadAsync();

        if (await LoadData(stream))
        {
            DataPath = files[0].TryGetLocalPath();
            if (DataPath is not null)
                AddRecentFile(DataPath);
        }
    }

    public void DFS_ScriptMenu(MenuItem? root, string dir)
    {
        //var dir = Path.Combine(AppContext.BaseDirectory, "Scripts");
        List<MenuItem> items = new List<MenuItem>();
        foreach (var dir1 in Directory.GetDirectories(dir))
        {
            var info = new DirectoryInfo(dir1);
            var dirM = new MenuItem
            {
                Header = info.Name
            };
            items.Add(dirM);
            DFS_ScriptMenu(dirM, dir1);
        }

        foreach (var file in Directory.GetFiles(dir))
        {
            var info = new FileInfo(file);
            if (info.Extension is ".csx")
            {
                var dirM = new MenuItem
                {
                    Header = info.Name
                };
                dirM.Click += async (e, r) => { await RunScript(file, new QiuStrongerFile(info)); };
                items.Add(dirM);
            }
        }

        if (root == null)
        {
            ScriptsSubMenuItems.Clear();
            ScriptsSubMenuItems.AddRange(items);
        }
        else
        {
            root.ItemsSource = items;
        }
    }

    public void GenerateScriptsSubMenuItems()
    {
        ScriptsSubMenuItems = [];
        DFS_ScriptMenu(null, Path.Combine(AppContext.BaseDirectory, "Scripts"));
    }

    public async Task<bool> FileSave()
    {
        if (Data is null)
            return false;

        if (OperatingSystem.IsAndroid())
        {
            if (!await MAUIBridge.HasRequiredStoragePermission()) return false;
        }

        IFile? file = await View!.SaveFileDialog(new FilePickerSaveOptions()
        {
            Title = UndertaleModToolAvalonia.Assets.Strings.Save_datafile,
            FileTypeChoices = dataFileTypes,
            DefaultExtension = ".win",
        });

        if (file is null)
            return false;

        using Stream stream = await file.OpenWriteAsync();

        return await SaveData(stream);
    }

    public async void FileClose()
    {
        if (!await AskFileSave(UndertaleModToolAvalonia.Assets.Strings.Save_before_closing))
            return;

        CloseData();
    }

    public async void FileSettings()
    {
        await View!.SettingsDialog();
    }

    public void FileExit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        else if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime viewApp)
        {
            viewApp.MainView = null;
        }
    }

    public void ToolsSearchInCode()
    {
        View!.SearchInCodeOpen();
    }

    public async void ScriptsRunOtherScript()
    {
        if (OperatingSystem.IsAndroid())
        {
            if (!await MAUIBridge.HasRequiredStoragePermission()) return;
        }
        var files = await View!.OpenFileDialog(new FilePickerOpenOptions()
        {
            Title = UndertaleModToolAvalonia.Assets.Strings.Run_Script,
            FileTypeFilter =
            [
                new FilePickerFileType("C# scripts (.csx)")
                {
                    Patterns = ["*.csx"],
                },
                new FilePickerFileType("All files")
                {
                    Patterns = ["*"],
                },
            ],
        });

        if (files is null || files.Count != 1)
            return;

        var file = files[0];
        string? filePath = file.TryGetLocalPath();
        await RunScript(filePath, file);
    }

    private async Task RunScript(string? filePath, IFile file)
    {
        if (OperatingSystem.IsAndroid())
        {
            if (!await MAUIBridge.HasRequiredStoragePermission()) return;
        }

        string text;
        if (filePath == null)
        {
            var t = file.Path.AbsolutePath;
            if (t.StartsWith("/document/primary%3A"))
            {
                t = "/sdcard/" + System.Web.HttpUtility.UrlDecode(t.Substring("/document/primary%3A".Length));
            }

            if (File.Exists(t)) filePath = t;
        }

        if (Settings.EnableQiuUtmtV3ScriptEngine&&!OperatingSystem.IsWindows())
        {
            var loader = new LoaderOverlay(View.View);
            await Task.Run(() =>
            {
                var qf = new QiuFuncMain(file.TryGetLocalPath()??FileSystem.Current.CacheDirectory + "/temp.data",
                    Data, null,
                    new FileInfo(FileSystem.Current.CacheDirectory),
                    true, false);
                try
                {
                    qf.RunCSharpFilePublic(filePath, (line =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (loader != null)
                                loader.SetText(line);
                        });
                    }), null);
                    Dispatcher.UIThread.Post(() => CommandTextBoxText = $"{Path.GetFileName(filePath) ?? "Code"} finished!");
                }
                catch (Exception e)
                {
                    Dispatcher.UIThread.Post(() => CommandTextBoxText = $"{Path.GetFileName(filePath) ?? "Code"} throw exception!\n{e.Message}");
                }
                Dispatcher.UIThread.Post(() => loader.ShowOkButton());
            });
        }
        else
        {
            using (Stream stream = await file.OpenReadAsync())
            {
                using StreamReader streamReader = new(stream);
                text = streamReader.ReadToEnd();
            }

            await Scripting.RunScript(text, filePath);
            CommandTextBoxText = $"{Path.GetFileName(filePath) ?? "Code "} Finished!";
        }
    }

    public async void HelpGitHub()
    {
        await View!.LaunchUriAsync(new Uri("https://github.com/QiumingOrg/"));
    }

    public async void HelpAbout()
    {
        await ShowMessageDialog(
            $"UndertaleModTool by the Underminers team\nUndertaleModToolAvalonia by luizzeroxis\nQiuUTMTv4 by 秋冥散雨_GenOuka\n\nLicensed under the GNU General Public License Version 3.",
            title: UndertaleModToolAvalonia.Assets.Strings.About);
    }

    public async void Donate()
    {
        TabItemViewModel tab = new(new DonateViewModel());
        Tabs.Add(tab);
        TabSelected = tab;
    }

    public async void QQGroup()
    {
        await View!.LaunchUriAsync(new Uri(
            "https://qm.qq.com/cgi-bin/qm/qr?k=jgxnT0-Op9FsJm-J2tgqvOnWa92hdoiY&jump_from=webapi&authKey=CLULWataQkeYLNjKC5Pko38y9M+ErvLb0R7GeJ/EcVBfXWn7EE7Oi0HThlAJrxBn"));
    }

    public async void Bilibili()
    {
        await View!.LaunchUriAsync(new Uri("https://space.bilibili.com/3493116076100126"));
    }

    public async void DataItemAdd(IList list)
    {
        if (Data is null || list is null)
            return;

        UndertaleResource res = UndertaleData.CreateResource(list);

        string? name = UndertaleData.GetDefaultResourceName(list);
        if (name is not null)
        {
            name = await View!.TextBoxDialog("Name of new asset:", name);
            if (name is null)
                return;

            static bool IsValidAssetIdentifier(string name)
            {
                if (string.IsNullOrEmpty(name))
                    return false;

                char firstChar = name[0];
                if (!char.IsAsciiLetter(firstChar) && firstChar != '_')
                    return false;

                foreach (char c in name.Skip(1))
                    if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                        return false;

                return true;
            }

            if (!IsValidAssetIdentifier(name))
            {
                await ShowMessageDialog(
                    $"Asset name \"{name}\" is not a valid identifier. Only letters, digits and underscore allowed, and it must not start with a digit.");
                return;
            }
        }

        Data.InitializeResource(res, list, name);

        if (res is UndertaleRoom room)
        {
            if (await ShowMessageDialog("Add the new room to the end of the room order list?", ok: false, yes: true,
                    no: true) == DialogResult.Yes)
                Data.GeneralInfo?.RoomOrder.Add(new(room));
        }

        list.Add(res);

        if (Settings!.OpenNewResourceAfterCreatingIt)
        {
            TabOpen(res, inNewTab: true);
        }
    }

    public TabItemViewModel? TabOpen(object? item, bool inNewTab = false)
    {
        if (Data is null)
            return null;

        object? content = item switch
        {
            DescriptionViewModel vm => vm,
            "GeneralInfo" => new GeneralInfoViewModel(Data),
            "GlobalInitScripts" => new GlobalInitScriptsViewModel(
                (Data.GlobalInitScripts as ObservableCollection<UndertaleGlobalInit>)!),
            "GameEndScripts" => new GameEndScriptsViewModel(
                (Data.GameEndScripts as ObservableCollection<UndertaleGlobalInit>)!),
            UndertaleAudioGroup r => new UndertaleAudioGroupViewModel(r),
            UndertaleSound r => new UndertaleSoundViewModel(r),
            UndertaleSprite r => new UndertaleSpriteViewModel(r),
            UndertaleBackground r => new UndertaleBackgroundViewModel(r),
            UndertalePath r => new UndertalePathViewModel(r),
            UndertaleScript r => new UndertaleScriptViewModel(r),
            UndertaleShader r => new UndertaleShaderViewModel(r),
            UndertaleFont r => new UndertaleFontViewModel(r),
            UndertaleTimeline r => new UndertaleTimelineViewModel(r),
            UndertaleGameObject r => new UndertaleGameObjectViewModel(r),
            UndertaleRoom r => new UndertaleRoomViewModel(r),
            UndertaleExtension r => new UndertaleExtensionViewModel(r),
            UndertaleTexturePageItem r => new UndertaleTexturePageItemViewModel(r),
            UndertaleCode r => new UndertaleCodeViewModel(r),
            UndertaleVariable r => new UndertaleVariableViewModel(r),
            UndertaleFunction r => new UndertaleFunctionViewModel(r),
            UndertaleCodeLocals r => new UndertaleCodeLocalsViewModel(r),
            UndertaleString r => new UndertaleStringViewModel(r),
            UndertaleEmbeddedTexture r => new UndertaleEmbeddedTextureViewModel(r),
            UndertaleEmbeddedAudio r => new UndertaleEmbeddedAudioViewModel(r),
            UndertaleTextureGroupInfo r => new UndertaleTextureGroupInfoViewModel(r),
            UndertaleEmbeddedImage r => new UndertaleEmbeddedImageViewModel(r),
            UndertaleParticleSystem r => new UndertaleParticleSystemViewModel(r),
            UndertaleParticleSystemEmitter r => new UndertaleParticleSystemEmitterViewModel(r),
            _ => null,
        };

        if (content is not null)
        {
            if (!inNewTab && TabSelected is not null)
            {
                TabSelected.GoTo(content);
                return TabSelected;
            }
            else
            {
                TabItemViewModel tab = new(content);
                Tabs.Add(tab);
                TabSelected = tab;
                return tab;
            }
        }

        return null;
    }

    public void TabClose(TabItemViewModel tab)
    {
        var selected = TabSelected;
        var index = TabSelectedIndex;

        Tabs.Remove(tab);

        if (TabSelected != selected)
        {
            if (index >= Tabs.Count)
                index = Tabs.Count - 1;

            TabSelectedIndex = index;
        }
    }

    public void TabGoBack()
    {
        TabSelected?.GoBack();
    }

    public void TabGoForward()
    {
        TabSelected?.GoForward();
    }

    private void OnTabSelectedChanged()
    {
        if (Data is not null && TabSelected?.Content is IUndertaleResourceViewModel vm)
        {
            TabSelectedResourceIdString = Data.IndexOf(vm.Resource).ToString();
        }
        else
        {
            TabSelectedResourceIdString = "None";
        }
    }

    // Project system

    IReadOnlyList<FilePickerFileType> projectFileTypes =
    [
        new FilePickerFileType("Project files (.json)")
        {
            Patterns = ["*.json"],
        },
        new FilePickerFileType("All files")
        {
            Patterns = ["*"],
        },
    ];

    public async void ProjectNew()
    {
        if (Project is not null && Project.HasUnexportedAssets)
        {
            var result = await ShowMessageDialog(
                "The current project has unexported assets. Are you sure you want to create a new project?",
                title: "Project already open", ok: false, yes: true, no: true, cancel: true);
            if (result != DialogResult.Yes)
                return;
        }

        // If necessary, ask for a source data file
        if (Data is null || DataPath is null)
        {
            var files = await View!.OpenFileDialog(new FilePickerOpenOptions()
            {
                Title = "Choose source data file",
                FileTypeFilter = dataFileTypes,
            });
            if (files is null || files.Count != 1)
                return;

            using Stream stream = await files[0].OpenReadAsync();
            if (!await LoadData(stream))
                return;

            DataPath = files[0].TryGetLocalPath();
            if (DataPath is null)
                return;
        }

        // Ask for name
        string? projectName = await View!.TextBoxDialog("Choose a name for the new project:",
            Data.GeneralInfo?.DisplayName?.Content ?? "New Mod", title: "Choose project name");
        if (projectName is null)
        {
            CommandTextBoxText = "New project creation cancelled.";
            return;
        }
        projectName = projectName.Trim();

        // Prompt location for project directory
        var folders = await View!.OpenFolderDialog(new FolderPickerOpenOptions()
        {
            Title = "Choose project directory"
        });
        if (folders is null || folders.Count != 1)
        {
            CommandTextBoxText = "New project creation cancelled.";
            return;
        }
        string? directory = folders[0].TryGetLocalPath();
        if (directory is null)
        {
            CommandTextBoxText = "New project creation cancelled.";
            return;
        }

        // Ask for save file path
        var saveFile = await View!.SaveFileDialog(new FilePickerSaveOptions()
        {
            Title = "Choose save file path for project",
            FileTypeChoices = dataFileTypes,
            DefaultExtension = ".win",
        });
        if (saveFile is null)
        {
            CommandTextBoxText = "New project creation cancelled.";
            return;
        }
        string? saveFilePath = saveFile.TryGetLocalPath();

        // Attempt making project at the specified location
        ProjectContext newProjectContext;
        try
        {
            newProjectContext = new(Data, DataPath, saveFilePath,
                Path.Join(directory, "project.json"), projectName,
                (f) => Dispatcher.UIThread.Invoke(f));
        }
        catch (ProjectException ex)
        {
            await ShowMessageDialog(ex.Message, title: "Failed to create project");
            CommandTextBoxText = "Project creation failed.";
            return;
        }
        catch (Exception ex)
        {
            await ShowMessageDialog($"Error creating project: {ex}", title: "Failed to create project");
            CommandTextBoxText = "Project creation failed.";
            return;
        }

        AssignNewProject(newProjectContext);
        CommandTextBoxText = $"Project \"{projectName}\" created successfully.";
    }

    public async void ProjectOpen()
    {
        if (Project is not null && Project.HasUnexportedAssets)
        {
            var result = await ShowMessageDialog(
                "The current project has unexported assets. Are you sure you want to open a different project?",
                title: "Project already open", ok: false, yes: true, no: true, cancel: true);
            if (result != DialogResult.Yes)
                return;
        }

        // Choose project file to open
        var files = await View!.OpenFileDialog(new FilePickerOpenOptions()
        {
            Title = "Open project file",
            FileTypeFilter = projectFileTypes,
        });
        if (files is null || files.Count != 1)
            return;

        string? projectFilePath = files[0].TryGetLocalPath();
        if (projectFilePath is null)
            return;

        // If necessary, ask for a source data file
        string? dataFilePathToLoad = null;
        if (Data is null || DataPath is null)
        {
            var sourceFiles = await View!.OpenFileDialog(new FilePickerOpenOptions()
            {
                Title = "Choose source data file",
                FileTypeFilter = dataFileTypes,
            });
            if (sourceFiles is null || sourceFiles.Count != 1)
                return;

            dataFilePathToLoad = sourceFiles[0].TryGetLocalPath();
        }

        // Ask for save file path
        var saveFile = await View!.SaveFileDialog(new FilePickerSaveOptions()
        {
            Title = "Choose save file path for project",
            FileTypeChoices = dataFileTypes,
            DefaultExtension = ".win",
        });
        if (saveFile is null)
            return;

        string? saveFilePath = saveFile.TryGetLocalPath();

        // Load data file if needed
        if (dataFilePathToLoad is not null)
        {
            using Stream stream = File.OpenRead(dataFilePathToLoad);
            if (!await LoadData(stream))
                return;

            DataPath = dataFilePathToLoad;
        }

        if (Data is null || DataPath is null)
            return;

        // Change main file path to the save data file path
        string loadFilePath = DataPath;
        string? originalDataPath = DataPath;

        // Attempt loading project from the specific JSON
        ProjectContext? newProjectContext = null;
        IsEnabled = false;
        await Task.Run(() =>
        {
            try
            {
                newProjectContext = ProjectContext.CreateWithDataFilePaths(loadFilePath, saveFilePath, projectFilePath);
                newProjectContext.Import(Data, null, (f) => Dispatcher.UIThread.Invoke(f));
            }
            catch (ProjectException ex)
            {
                newProjectContext = null;
                Dispatcher.UIThread.Post(() => ShowMessageDialog(ex.Message, title: "Failed to load project"));
            }
            catch (Exception ex)
            {
                newProjectContext = null;
                Dispatcher.UIThread.Post(() => ShowMessageDialog($"Error loading project: {ex}", title: "Failed to load project"));
            }
        });
        IsEnabled = true;

        // Don't assign new project context if load failed
        if (newProjectContext is null)
        {
            CommandTextBoxText = "Project failed to open.";
            return;
        }

        // Update DataPath to save path
        DataPath = saveFilePath;

        AssignNewProject(newProjectContext);
        CommandTextBoxText = $"Project \"{newProjectContext.Name}\" opened successfully.";
    }

    public async void ProjectSave()
    {
        if (Data is null || Project is null)
            return;

        IsEnabled = false;
        bool success = false;
        await Task.Run(() =>
        {
            try
            {
                Project.Export(true);
                success = true;
            }
            catch (ProjectException ex)
            {
                Dispatcher.UIThread.Post(() => ShowMessageDialog(ex.Message, title: "Failed to save project"));
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => ShowMessageDialog($"Error saving project: {ex}", title: "Failed to save project"));
            }
        });
        IsEnabled = true;
        CommandTextBoxText = success ? "Project saved successfully." : "Project failed to save.";
    }

    public async void ProjectClose()
    {
        if (Project is null)
            return;

        if (Project.HasUnexportedAssets)
        {
            var result = await ShowMessageDialog(
                "The current project has unexported assets. Are you sure you want to close the project?",
                title: "Close project", ok: false, yes: true, no: true, cancel: true);
            if (result != DialogResult.Yes)
                return;
        }

        UnloadProject();
        CommandTextBoxText = "Project closed.";
    }

    public void ProjectViewAssets()
    {
        if (Data is null || Project is null)
            return;

        // Open project assets as a tab
        TabItemViewModel tab = new(new ProjectAssetsViewModel(Project));
        Tabs.Add(tab);
        TabSelected = tab;
    }

    private void UnloadProject()
    {
        Project = null;
    }

    private void AssignNewProject(ProjectContext project)
    {
        UnloadProject();
        Project = project;
    }
}