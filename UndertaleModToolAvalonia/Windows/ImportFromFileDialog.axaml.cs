using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using UndertaleModLib.Project;

namespace UndertaleModToolAvalonia;

public partial class ImportFromFileDialog : Window
{
    private CrossFileImporter _importer = null!;
    private ObservableCollection<ResourceDisplayItem> _availableItems = new();
    private ObservableCollection<ResourceDisplayItem> _selectedItems = new();
    private List<ResourceInfo>? _allResources;

    public bool ImportCompleted { get; private set; }
    public CrossFileImportResult? ImportResult { get; private set; }

    public ImportFromFileDialog()
    {
        InitializeComponent();
    }

    public ImportFromFileDialog(UndertaleModLib.UndertaleData targetData) : this()
    {
        _importer = new CrossFileImporter(targetData);
        AvailableList.ItemsSource = _availableItems;
        SelectedList.ItemsSource = _selectedItems;
        ImportButton.IsEnabled = false;
    }

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select data file to import from",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Data files")
                {
                    Patterns = new[] { "*.win", "*.unx", "*.ios", "*.droid" }
                },
                new FilePickerFileType("All files")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count == 0)
            return;

        string? filePath = files[0].TryGetLocalPath();
        if (filePath is null)
            return;

        SourceFilePathTextBox.Text = filePath;

        bool loadSuccess = false;
        Exception? loadError = null;

        await Task.Run(() =>
        {
            try
            {
                _importer.LoadSourceFile(filePath);
                loadSuccess = true;
            }
            catch (Exception ex)
            {
                loadError = ex;
            }
        });

        if (!loadSuccess)
        {
            await ShowErrorDialog($"Failed to load source file: {loadError?.Message}");
            return;
        }

        PopulateResourceLists();
        ImportButton.IsEnabled = _selectedItems.Count > 0;
    }

    private void PopulateResourceLists()
    {
        _availableItems.Clear();
        _selectedItems.Clear();

        try
        {
            _allResources = _importer.GetAvailableResources();
        }
        catch (Exception ex)
        {
            _ = ShowErrorDialog($"Failed to enumerate resources: {ex.Message}");
            return;
        }

        foreach (ResourceInfo res in _allResources)
        {
            _availableItems.Add(new ResourceDisplayItem(res));
        }
    }

    private void SelectAllButton_Click(object? sender, RoutedEventArgs e)
    {
        List<ResourceDisplayItem> toMove = _availableItems.ToList();
        foreach (ResourceDisplayItem item in toMove)
        {
            _availableItems.Remove(item);
            _selectedItems.Add(item);
        }
        ImportButton.IsEnabled = _selectedItems.Count > 0;
    }

    private void DeselectAllButton_Click(object? sender, RoutedEventArgs e)
    {
        List<ResourceDisplayItem> toMove = _selectedItems.ToList();
        foreach (ResourceDisplayItem item in toMove)
        {
            _selectedItems.Remove(item);
            _availableItems.Add(item);
        }
        ImportButton.IsEnabled = _selectedItems.Count > 0;
    }

    private void AvailableList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (AvailableList.SelectedItem is ResourceDisplayItem item)
        {
            _availableItems.Remove(item);
            _selectedItems.Add(item);
            ImportButton.IsEnabled = _selectedItems.Count > 0;
        }
    }

    private void SelectedList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SelectedList.SelectedItem is ResourceDisplayItem item)
        {
            _selectedItems.Remove(item);
            _availableItems.Add(item);
            ImportButton.IsEnabled = _selectedItems.Count > 0;
        }
    }

    private async void ImportButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedItems.Count == 0)
            return;

        NameConflictResolution resolution = NameConflictResolution.Skip;
        if (OverwriteRadio.IsChecked == true)
            resolution = NameConflictResolution.Overwrite;
        else if (RenameRadio.IsChecked == true)
            resolution = NameConflictResolution.Rename;

        bool importDeps = ImportDependenciesCheck.IsChecked == true;

        List<ResourceInfo> selectedResources = _selectedItems.Select(item => item.ResourceInfo).ToList();

        CrossFileImportResult? result = null;
        Exception? importError = null;

        await Task.Run(() =>
        {
            try
            {
                result = _importer.ImportResources(selectedResources, resolution, importDeps);
            }
            catch (Exception ex)
            {
                importError = ex;
            }
        });

        if (importError is not null)
        {
            await ShowErrorDialog($"Import failed: {importError.Message}");
            return;
        }

        ImportResult = result!;
        ImportCompleted = true;

        var r = result!;
        string summary = $"Import completed:\n" +
                         $"  Imported: {r.ImportedCount}\n" +
                         $"  Skipped: {r.SkippedCount}\n" +
                         $"  Overwritten: {r.OverwrittenCount}";

        if (r.Warnings.Count > 0 || r.Errors.Count > 0)
        {
            summary += "\n\n";

            if (r.Warnings.Count > 0)
            {
                summary += "Warnings:\n";
                foreach (string w in r.Warnings.Take(10))
                    summary += $"  - {w}\n";
                if (r.Warnings.Count > 10)
                    summary += $"  ... and {r.Warnings.Count - 10} more\n";
            }

            if (r.Errors.Count > 0)
            {
                summary += "Errors:\n";
                foreach (string err in r.Errors.Take(10))
                    summary += $"  - {err}\n";
                if (r.Errors.Count > 10)
                    summary += $"  ... and {r.Errors.Count - 10} more\n";
            }
        }

        var msgWindow = new MessageWindow(summary, "Import Result", ok: true);
        await msgWindow.ShowDialog(this);

        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _importer?.Dispose();
        base.OnClosing(e);
    }

    private async Task ShowErrorDialog(string message)
    {
        var errorDialog = new ErrorDialog("Import Error", "An error occurred during import.", message);
        await errorDialog.ShowDialog(this);
    }
}

public sealed class ResourceDisplayItem
{
    public ResourceInfo ResourceInfo { get; }
    public string DisplayName { get; }
    public string FullName { get; }

    public ResourceDisplayItem(ResourceInfo resourceInfo)
    {
        ResourceInfo = resourceInfo;

        string typeName = resourceInfo.AssetType?.ToInterfaceName() ?? resourceInfo.ResourceType.Name;
        string conflictMarker = resourceInfo.ExistsInTarget ? " \u26A0" : "";

        DisplayName = $"{typeName}: {resourceInfo.Name}{conflictMarker}";
        FullName = resourceInfo.ExistsInTarget
            ? $"{typeName}: {resourceInfo.Name} (exists in target)"
            : $"{typeName}: {resourceInfo.Name}";
    }

    public override string ToString() => DisplayName;
}
