using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using UndertaleModLib.Project;

namespace UndertaleModToolAvalonia;

public class ProjectAssetsViewModel : INotifyPropertyChanged
{
    private readonly ProjectContext _project;
    private bool _preventUpdateList = false;

    public readonly record struct UnexportedAsset(string Name, string AssetType, IProjectAsset ProjectAsset);

    public List<UnexportedAsset> Assets { get; private set; } = new();

    public string Heading => "Unexported Project Assets";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProjectAssetsViewModel(ProjectContext project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
        UpdateList();
        _project.UnexportedAssetsChanged += OnUnexportedAssetsChanged;
    }

    private void OnUnexportedAssetsChanged(object? sender, EventArgs e)
    {
        UpdateList();
    }

    public void UpdateList()
    {
        if (_preventUpdateList)
            return;

        Assets = _project
            .EnumerateUnexportedAssets()
            .Select(asset => new UnexportedAsset(asset.ProjectName, asset.ProjectAssetType.ToInterfaceName(), asset))
            .OrderBy(a => a.AssetType)
            .ThenBy(a => a.Name)
            .ToList();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Assets)));
    }

    public void OpenAsset(UnexportedAsset asset, bool inNewTab = false)
    {
        if (asset.ProjectAsset is not UndertaleModLib.UndertaleObject obj)
            return;

        MainViewModel.Me.TabOpen(obj, inNewTab);
    }

    public void UnmarkAssetForExport(UnexportedAsset asset)
    {
        _preventUpdateList = true;
        _project.UnmarkAssetForExport(asset.ProjectAsset);
        _preventUpdateList = false;
        UpdateList();
    }

    public void UnmarkSelectedAssetsForExport(List<UnexportedAsset> selectedAssets)
    {
        _preventUpdateList = true;
        foreach (var asset in selectedAssets)
        {
            _project.UnmarkAssetForExport(asset.ProjectAsset);
        }
        _preventUpdateList = false;
        UpdateList();
    }
}
