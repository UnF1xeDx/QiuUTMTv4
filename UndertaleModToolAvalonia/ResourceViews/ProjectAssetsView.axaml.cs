using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace UndertaleModToolAvalonia;

public partial class ProjectAssetsView : UserControl
{
    public ProjectAssetsView()
    {
        InitializeComponent();
    }

    private ProjectAssetsViewModel? GetViewModel()
    {
        return DataContext as ProjectAssetsViewModel;
    }

    private void OpenSelectedListViewItem(bool inNewTab = false)
    {
        if (GetViewModel() is { } vm && AssetsDataGrid.SelectedItem is ProjectAssetsViewModel.UnexportedAsset asset)
        {
            vm.OpenAsset(asset, inNewTab);
        }
    }

    private void UnmarkSelectedListViewItemsForExport()
    {
        if (GetViewModel() is not { } vm)
            return;

        var selectedAssets = new List<ProjectAssetsViewModel.UnexportedAsset>();
        foreach (var item in AssetsDataGrid.SelectedItems)
        {
            if (item is ProjectAssetsViewModel.UnexportedAsset asset)
                selectedAssets.Add(asset);
        }
        vm.UnmarkSelectedAssetsForExport(selectedAssets);
    }

    private void DataGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        OpenSelectedListViewItem();
    }

    private void DataGrid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedListViewItem();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            UnmarkSelectedListViewItemsForExport();
            e.Handled = true;
        }
    }

    private void MenuItemOpen_Click(object? sender, RoutedEventArgs e)
    {
        OpenSelectedListViewItem();
    }

    private void MenuItemOpenInNewTab_Click(object? sender, RoutedEventArgs e)
    {
        OpenSelectedListViewItem(true);
    }

    private void MenuItemUnmarkForExport_Click(object? sender, RoutedEventArgs e)
    {
        UnmarkSelectedListViewItemsForExport();
    }
}
