using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace UndertaleModToolAvalonia;

public partial class ImportGraphicsDialog : Window
{
    private readonly UndertaleData _data = null!;
    private ObservableCollection<ImportImageEntry> _entries = new();

    private static readonly string[] OffsetNames =
    {
        "Top Left", "Top Center", "Top Right",
        "Center Left", "Center", "Center Right",
        "Bottom Left", "Bottom Center", "Bottom Right"
    };

    public List<ImportImageEntry> SelectedEntries { get; private set; } = new();
    public GraphicsImportSettings? Settings { get; private set; }

    public ImportGraphicsDialog()
    {
        InitializeComponent();
    }

    public ImportGraphicsDialog(UndertaleData data) : this()
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        EntriesDataGrid.ItemsSource = _entries;
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select folder containing graphics to import",
            AllowMultiple = false
        });

        if (folders.Count == 0)
            return;

        string? folderPath = folders[0].TryGetLocalPath();
        if (folderPath is null)
            return;

        FolderTextBox.Text = folderPath;
    }

    private async void ScanButton_Click(object? sender, RoutedEventArgs e)
    {
        string folder = FolderTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            await ShowMessage("Please select a valid folder first.");
            return;
        }

        _entries.Clear();
        bool importUnknownAsSprite = ImportUnknownCheck.IsChecked == true;

        List<ImportImageEntry> scannedEntries = new();

        await Task.Run(() =>
        {
            string[] files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".png", StringComparison.InvariantCultureIgnoreCase) ||
                            f.EndsWith(".gif", StringComparison.InvariantCultureIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (string file in files)
            {
                try
                {
                    var entry = CreateEntryFromFile(file, importUnknownAsSprite);
                    if (entry is not null)
                        scannedEntries.Add(entry);
                }
                catch
                {
                    // Skip files that can't be read
                }
            }
        });

        foreach (var entry in scannedEntries)
            _entries.Add(entry);
    }

    private ImportImageEntry? CreateEntryFromFile(string filePath, bool importUnknownAsSprite)
    {
        string fileName = Path.GetFileName(filePath);
        string stripped = Path.GetFileNameWithoutExtension(fileName);
        SpriteType spriteType = GraphicsImporter.GetSpriteType(filePath);

        if (importUnknownAsSprite && (spriteType == SpriteType.Unknown || spriteType == SpriteType.Font))
            spriteType = SpriteType.Sprite;

        string spriteName;
        int frameIndex;

        // Handle _stripN pattern
        Match stripMatch = Regex.Match(stripped, @"(.*)_strip(\d+)");
        if (spriteType == SpriteType.Sprite && stripMatch.Success)
        {
            // Strip sprites are expanded into individual frames by the packer,
            // so we just note the base sprite name and frame 0
            spriteName = stripMatch.Groups[1].Value;
            frameIndex = 0;
        }
        else
        {
            // Try to parse as spriteName_frameIndex
            int lastUnderscore = stripped.LastIndexOf('_');
            if (lastUnderscore >= 0 && int.TryParse(stripped.Substring(lastUnderscore + 1), out int parsedFrame))
            {
                spriteName = stripped.Substring(0, lastUnderscore);
                frameIndex = parsedFrame;
            }
            else
            {
                spriteName = stripped;
                frameIndex = 0;
            }
        }

        // For backgrounds, frame is always 0
        if (spriteType == SpriteType.Background)
        {
            spriteName = stripped;
            frameIndex = 0;
        }

        // Read image dimensions
        int width = 0, height = 0;
        try
        {
            using var img = new ImageMagick.MagickImage(filePath);
            width = (int)img.Width;
            height = (int)img.Height;

            // For strip sprites, divide width by frame count
            if (spriteType == SpriteType.Sprite && stripMatch.Success)
            {
                if (int.TryParse(stripMatch.Groups[2].Value, out int frameCount) && frameCount > 0)
                    width /= frameCount;
            }
        }
        catch
        {
            return null;
        }

        // Determine if the sprite/background exists and what modes are available
        bool spriteExists = false;
        bool canReplaceInPlace = false;
        int? targetWidth = null, targetHeight = null;
        int? sourceWidth = null, sourceHeight = null;
        int? originalTexturePageIndex = null;
        string statusText = "New";

        if (spriteType == SpriteType.Background)
        {
            var bg = _data.Backgrounds.ByName(spriteName);
            if (bg is not null)
            {
                spriteExists = true;
                statusText = "Existing";
                if (bg.Texture is not null)
                {
                    targetWidth = bg.Texture.TargetWidth;
                    targetHeight = bg.Texture.TargetHeight;
                    sourceWidth = bg.Texture.SourceWidth;
                    sourceHeight = bg.Texture.SourceHeight;
                    canReplaceInPlace = width == bg.Texture.TargetWidth && height == bg.Texture.TargetHeight;
                    if (canReplaceInPlace)
                        statusText = "Replace";
                    else
                        statusText = "Size mismatch";

                    // Find texture page index
                    for (int i = 0; i < _data.EmbeddedTextures.Count; i++)
                    {
                        if (_data.EmbeddedTextures[i] == bg.Texture.TexturePage)
                        {
                            originalTexturePageIndex = i;
                            break;
                        }
                    }
                }
            }
        }
        else if (spriteType == SpriteType.Sprite)
        {
            var sprite = _data.Sprites.ByName(spriteName);
            if (sprite is not null)
            {
                spriteExists = true;
                if (frameIndex >= 0 && frameIndex < sprite.Textures.Count && sprite.Textures[frameIndex]?.Texture is not null)
                {
                    var item = sprite.Textures[frameIndex].Texture!;
                    targetWidth = item.TargetWidth;
                    targetHeight = item.TargetHeight;
                    sourceWidth = item.SourceWidth;
                    sourceHeight = item.SourceHeight;
                    canReplaceInPlace = width == item.TargetWidth && height == item.TargetHeight;
                    if (canReplaceInPlace)
                        statusText = "Replace";
                    else
                        statusText = "Size mismatch";

                    // Find texture page index
                    for (int i = 0; i < _data.EmbeddedTextures.Count; i++)
                    {
                        if (_data.EmbeddedTextures[i] == item.TexturePage)
                        {
                            originalTexturePageIndex = i;
                            break;
                        }
                    }
                }
                else
                {
                    statusText = frameIndex >= sprite.Textures.Count ? "New frame" : "Existing";
                }
            }
        }

        // Determine default import mode
        ImportMode defaultMode;
        if (canReplaceInPlace)
            defaultMode = GetGlobalImportMode();
        else if (spriteExists)
            defaultMode = ImportMode.NewTexturePages;
        else
            defaultMode = ImportMode.NewTexturePages;

        var entry = new ImportImageEntry
        {
            FilePath = filePath,
            FileName = fileName,
            SpriteName = spriteName,
            FrameIndex = frameIndex,
            ImageType = spriteType,
            ImageWidth = width,
            ImageHeight = height,
            TargetWidth = targetWidth,
            TargetHeight = targetHeight,
            SourceWidth = sourceWidth,
            SourceHeight = sourceHeight,
            CanReplaceInPlace = canReplaceInPlace,
            SpriteExists = spriteExists,
            StatusText = statusText,
            OriginalTexturePageIndex = originalTexturePageIndex,
            ImportMode = defaultMode,
            IsSelected = true,
            OriginPosition = OriginPositionCombo.SelectedIndex,
            AnimationSpeed = float.TryParse(AnimationSpeedTextBox.Text, out float speed) ? speed : 1f,
            PlaybackType = PlaybackTypeCombo.SelectedIndex
        };

        // Estimate texture page index for new entries
        if (entry.ImportMode == ImportMode.NewTexturePages)
        {
            entry.EstimatedTexturePageIndex = _data.EmbeddedTextures.Count;
        }

        return entry;
    }

    private ImportMode GetGlobalImportMode()
    {
        if (ReplaceInPlaceRadio.IsChecked == true)
            return ImportMode.ReplaceInPlace;
        if (KeepOriginalRadio.IsChecked == true)
            return ImportMode.KeepOriginalTexturePage;
        return ImportMode.NewTexturePages;
    }

    private int GetTexturePageSize()
    {
        return TexturePageSizeCombo.SelectedIndex switch
        {
            1 => 4096,
            2 => 8192,
            _ => 2048
        };
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var entry in _entries)
            entry.IsSelected = true;
    }

    private void DeselectAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var entry in _entries)
            entry.IsSelected = false;
    }

    private void ApplyDefaultsButton_Click(object? sender, RoutedEventArgs e)
    {
        int originPos = OriginPositionCombo.SelectedIndex;
        float animSpeed = float.TryParse(AnimationSpeedTextBox.Text, out float s) ? s : 1f;
        int playbackType = PlaybackTypeCombo.SelectedIndex;

        foreach (var entry in _entries)
        {
            entry.OriginPosition = originPos;
            entry.AnimationSpeed = animSpeed;
            entry.PlaybackType = playbackType;
        }
    }

    private void OKButton_Click(object? sender, RoutedEventArgs e)
    {
        SelectedEntries = _entries.Where(en => en.IsSelected).ToList();

        if (SelectedEntries.Count == 0)
        {
            _ = ShowMessage("No entries selected for import.");
            return;
        }

        Settings = new GraphicsImportSettings
        {
            ImportFolder = FolderTextBox.Text?.Trim() ?? "",
            Mode = GetGlobalImportMode(),
            ImportUnknownAsSprite = ImportUnknownCheck.IsChecked == true,
            ImportFrameless = true,
            OriginPosition = OriginPositionCombo.SelectedIndex,
            AnimationSpeed = float.TryParse(AnimationSpeedTextBox.Text, out float speed) ? speed : 1f,
            PlaybackType = PlaybackTypeCombo.SelectedIndex,
            TexturePageSize = GetTexturePageSize()
        };

        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async Task ShowMessage(string message)
    {
        var msgWindow = new MessageWindow(message, "Import Graphics", ok: true);
        await msgWindow.ShowDialog(this);
    }
}
