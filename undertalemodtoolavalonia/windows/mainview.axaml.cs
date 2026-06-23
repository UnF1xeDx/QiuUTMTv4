using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace UndertaleModToolAvalonia;

public partial class MainView : UserControl, IView
{
    private TaskCompletionSource<object?>? _overlayDialogTcs;

    public MainView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateAndroidTouchPadding();

        DataContextChanged += (_, __) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.View = this;

                MainTreeDataGrid.Source = new HierarchicalTreeDataGridSource<MainViewModel.TreeDataGridItem>(vm.TreeDataGridData)
                {
                    Columns = {
                        new HierarchicalExpanderColumn<MainViewModel.TreeDataGridItem>(
                            new TemplateColumn<MainViewModel.TreeDataGridItem>(null,
                                new FuncDataTemplate<MainViewModel.TreeDataGridItem>((value, namescope) =>
                                {
                                    if (value is null)
                                        return null;

                                    TextBlock textBlock = new() { Text = value.Text };

                                    if (value.Value is UndertaleNamedResource namedResource)
                                    {
                                        textBlock[!TextBlock.TextProperty] = new Binding("Value.Name.Content");
                                    }
                                    else if (value.Value is UndertaleString _string)
                                    {
                                        textBlock[!TextBlock.TextProperty] = new Binding("Value.Content");
                                    }
                                    //else if (value.Value is UndertaleData data)
                                    //{
                                    //    textBlock[!TextBlock.TextProperty] = new Binding("Value.GeneralInfo");
                                    //}

                                    return textBlock;
                                }), width: GridLength.Star
                            ),
                            x => x.Children)
                    }
                };
                // Expand root node by default for all platforms
                vm.TreeDataGridData.CollectionChanged += (_, _) =>
                {
                    Dispatcher.UIThread.Post(ExpandRootNode, DispatcherPriority.Background);
                };
                Dispatcher.UIThread.Post(ExpandRootNode, DispatcherPriority.Background);
            }
        };

        Loaded += (_, __) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.OnLoaded();
            }
            UpdateAndroidTouchPadding();

            // Wire up drag-over detection for tab detach
            var tabStrip = this.FindControl<TabStrip>("MainTabStrip");
            if (tabStrip is not null)
            {
                DragDrop.SetAllowDrop(tabStrip, true);
                tabStrip.AddHandler(DragDrop.DragOverEvent, TabStrip_DragOver);
            }
        };
    }

    private void FilterTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.FilterText = FilterTextBox.Text ?? "";
        }
    }

    private MainViewModel.TreeDataGridItem? GetItemFromTreeDataGridControl(object? source)
    {
        if (DataContext is MainViewModel vm)
        {
            if (source is Control control)
            {
                TreeDataGridRow? row = control.FindLogicalAncestorOfType<TreeDataGridRow>(includeSelf: true);
                if (row?.DataContext is MainViewModel.TreeDataGridItem item)
                {
                    return item;
                }
            }
        }
        return null;
    }

    private void OpenItemFromTreeDataGridControl(object? source)
    {
        if (DataContext is MainViewModel vm)
        {
            if (source is Control control)
            {
                TreeDataGridRow? row = control.FindLogicalAncestorOfType<TreeDataGridRow>(includeSelf: true);
                if (row?.DataContext is MainViewModel.TreeDataGridItem item)
                {
                    if (row.Rows?[row.RowIndex] is HierarchicalRow<MainViewModel.TreeDataGridItem> hierarchicalRow)
                    {
                        hierarchicalRow.IsExpanded = !hierarchicalRow.IsExpanded;
                    }
                    vm.TabOpen(item.Value);
                    // On Android, when double-clicking enters a tab page (leaf node), switch to full view
                    if (OperatingSystem.IsAndroid() && (item.Children is null || item.Children.Count == 0))
                    {
                        SetGri0State1(2);
                    }
                }
            }
        }
    }

    private void TreeDataGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        OpenItemFromTreeDataGridControl(e.Source);
    }

    private void MainTreeDataGrid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.PhysicalKey == PhysicalKey.Enter)
        {
            OpenItemFromTreeDataGridControl(e.Source);
        }
    }

    public void ContextMenu_Add_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            MainViewModel.TreeDataGridItem? item = GetItemFromTreeDataGridControl(e.Source);
            if (item is not null && vm.Data is not null)
            {
                // This could probably be better
                IList list = (item.Value switch
                {
                    "AudioGroups" => vm.Data.AudioGroups as IList,
                    "Sounds" => vm.Data.Sounds as IList,
                    "Sprites" => vm.Data.Sprites as IList,
                    "Backgrounds" => vm.Data.Backgrounds as IList,
                    "Paths" => vm.Data.Paths as IList,
                    "Scripts" => vm.Data.Scripts as IList,
                    "Shaders" => vm.Data.Shaders as IList,
                    "Fonts" => vm.Data.Fonts as IList,
                    "Timelines" => vm.Data.Timelines as IList,
                    "GameObjects" => vm.Data.GameObjects as IList,
                    "Rooms" => vm.Data.Rooms as IList,
                    "Extensions" => vm.Data.Extensions as IList,
                    "TexturePageItems" => vm.Data.TexturePageItems as IList,
                    "Code" => vm.Data.Code as IList,
                    "Variables" => vm.Data.Variables as IList,
                    "Functions" => vm.Data.Functions as IList,
                    "CodeLocals" => vm.Data.CodeLocals as IList,
                    "Strings" => vm.Data.Strings as IList,
                    "EmbeddedTextures" => vm.Data.EmbeddedTextures as IList,
                    "EmbeddedAudio" => vm.Data.EmbeddedAudio as IList,
                    "TextureGroupInformation" => vm.Data.TextureGroupInfo as IList,
                    "EmbeddedImages" => vm.Data.EmbeddedImages as IList,
                    "ParticleSystems" => vm.Data.ParticleSystems as IList,
                    "ParticleSystemEmitters" => vm.Data.ParticleSystemEmitters as IList,
                    _ => null,
                })!;

                vm.DataItemAdd(list);
            }
        }
    }

    public void ContextMenu_Open_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            MainViewModel.TreeDataGridItem? item = GetItemFromTreeDataGridControl(e.Source);
            if (item is not null && vm.Data is not null)
            {
                vm.TabOpen(item.Value);
            }
        }
    }

    public void ContextMenu_OpenInNewTab_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            MainViewModel.TreeDataGridItem? item = GetItemFromTreeDataGridControl(e.Source);
            if (item is not null && vm.Data is not null)
            {
                vm.TabOpen(item.Value, inNewTab: true);
            }
        }
    }

    public async void ContextMenu_CopyName_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            MainViewModel.TreeDataGridItem? item = GetItemFromTreeDataGridControl(e.Source);
            if (item is not null && vm.Data is not null)
            {
                string? name = item.Value switch
                {
                    UndertaleNamedResource namedResource => namedResource.Name.Content,
                    UndertaleString _string => _string.Content,
                    _ => null,
                };

                if (name is not null)
                {
                    TopLevel topLevel = TopLevel.GetTopLevel(this)!;
                    await topLevel.Clipboard!.SetTextAsync(name);
                }
            }
        }
    }

    public async void ContextMenu_Move_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            MainViewModel.TreeDataGridItem? item = GetItemFromTreeDataGridControl(e.Source);
            if (item is not null && vm.Data is not null && vm.View is not null)
            {
                UndertaleResource resource = (item.Value as UndertaleResource)!;
                IList list = vm.Data[resource.GetType()];
                int oldIndex = list.IndexOf(resource);

                string? input = await vm.View.TextBoxDialog("Swap to position:", oldIndex.ToString());
                if (input is null)
                    return;

                if (!int.TryParse(input, out int newIndex))
                {
                    await vm.View.MessageDialog($"\"{input}\" is not a integer");
                    return;
                }
                if (newIndex < 0 || newIndex >= list.Count)
                {
                    await vm.View.MessageDialog($"{newIndex} is out of range of the list");
                    return;
                }

                // HACK: I don't fully understand why it doesn't work if you don't do this
                if (oldIndex > newIndex)
                    (oldIndex, newIndex) = (newIndex, oldIndex);

                object? temp = list[newIndex];
                list[newIndex] = list[oldIndex];
                list[oldIndex] = temp;
            }
        }
    }

    public async void ContextMenu_Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            MainViewModel.TreeDataGridItem? item = GetItemFromTreeDataGridControl(e.Source);
            if (item is not null && vm.Data is not null)
            {
                // TODO: Maybe do something about all references to this.
                UndertaleResource resource = (item.Value as UndertaleResource)!;

                if (await vm.ShowMessageDialog($"Delete {resource}?\nNote that the code often references objects by ID, " +
                    $"so this operation is likely to break stuff because other items will shift up!",
                    ok: false, yes: true, no: true) == DialogResult.Yes)
                {
                    vm.Data[resource.GetType()].Remove(resource);

                    // TODO: Close tabs, remove histories
                }
            }
        }
    }

    private void TabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            object? tabSelected = e.AddedItems.Count > 0 ? e.AddedItems[0] : null;
            foreach (TabItemViewModel tab in vm.Tabs)
            {
                tab.IsSelected = (tab == tabSelected);
            }
        }
    }

    private void TabControl_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Middle)
        {
            if (DataContext is MainViewModel vm)
            {
                if (e.Source is Control control)
                {
                    TabStrip? tabControl = control.FindLogicalAncestorOfType<TabStrip>();
                    if (tabControl is not null && tabControl == sender)
                    {
                        TabStripItem? tabItem = control.FindLogicalAncestorOfType<TabStripItem>();
                        if (tabItem is not null && tabItem.DataContext is TabItemViewModel vmTabItem)
                        {
                            vm.TabClose(vmTabItem);
                        }
                    }
                }
            }
        }
    }

    private void TabStrip_DragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Control control)
        {
            var handler = control.FindResource("TabStripItemDropHandler") as TabStripItemDropHandler;
            handler?.OnDragOver(sender, e, e.Data.Get("Context"), vm);
        }
    }

    private void TabMenu_Close_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            if (e.Source is Control control)
            {
                TabStripItem? tabItem = control.FindLogicalAncestorOfType<TabStripItem>();
                if (tabItem is not null && tabItem.DataContext is TabItemViewModel vmTabItem)
                {
                    vm.TabClose(vmTabItem);
                }
            }
        }
    }

    private void TabMenu_Detach_Click(object? sender, RoutedEventArgs e)
    {
        // Tab detach is not supported on Android - tabs always stay in MainWindow
    }

    private async void CommandTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            if (e.Key == Key.Enter)
            {
                object? result = await vm.Scripting.RunScript(vm.CommandTextBoxText);
                vm.CommandTextBoxText = result?.ToString() ?? "";
            }
    }

    private int _gri0State1 = 0;

    private void AdjustView_OnClick(object? sender, RoutedEventArgs e)
    {
        SetGri0State1((_gri0State1 + 1) % 3);
    }

    private void SetGri0State1(int state)
    {
        _gri0State1 = state;
        switch (_gri0State1)
        {
            case 0:
                Grid0.ColumnDefinitions = new ColumnDefinitions("1*,Auto,0");
                break;
            case 1:
                Grid0.ColumnDefinitions = new ColumnDefinitions("28,Auto,1*");
                break;
            case 2:
                Grid0.ColumnDefinitions = new ColumnDefinitions("1*,Auto,3*");
                break;
        }
        UpdateAndroidTouchPadding();
    }

    private void UpdateAndroidTouchPadding()
    {
        if (!OperatingSystem.IsAndroid())
            return;

        bool isPortrait = Bounds.Height > Bounds.Width;
        bool shouldHavePadding = isPortrait && _gri0State1 == 0;

        if (shouldHavePadding)
            MainTreeDataGrid.Classes.Add("TouchPadding");
        else
            MainTreeDataGrid.Classes.Remove("TouchPadding");
    }

    private void ExpandRootNode()
    {
        if (MainTreeDataGrid.Source?.Rows is { } rows && rows.Count > 0)
        {
            if (rows[0] is HierarchicalRow<MainViewModel.TreeDataGridItem> hierarchicalRow)
            {
                hierarchicalRow.IsExpanded = true;
            }
        }
    }

    public void ApplyCustomBackground()
    {
        var settings = SettingsFile.Instance;
        if (settings is null) return;

        bool hasBackground = !string.IsNullOrEmpty(settings.BackgroundImagePath)
                             && File.Exists(settings.BackgroundImagePath);

        if (!hasBackground)
        {
            CustomBackgroundImage.Source = null;
            return;
        }

        try
        {
            var bitmap = new Bitmap(settings.BackgroundImagePath);
            CustomBackgroundImage.Source = bitmap;
            CustomBackgroundImage.Opacity = Math.Clamp(settings.BackgroundOpacity, 0.0, 1.0);

            CustomBackgroundImage.Stretch = settings.BackgroundStretchMode switch
            {
                "None" => Avalonia.Media.Stretch.None,
                "Fill" => Avalonia.Media.Stretch.Fill,
                "Uniform" => Avalonia.Media.Stretch.Uniform,
                _ => Avalonia.Media.Stretch.UniformToFill
            };
        }
        catch (Exception)
        {
            CustomBackgroundImage.Source = null;
        }
    }

    // Project menu click handlers
    private void ProjectNew_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.ProjectNew();
    }

    private void ProjectOpen_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.ProjectOpen();
    }

    private void ProjectSave_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.ProjectSave();
    }

    private void ProjectViewAssets_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.ProjectViewAssets();
    }

    private void ProjectClose_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.ProjectClose();
    }

    // IView overlay dialog implementations
    private void SetOverlayActive(bool active)
    {
        if (DataContext is MainViewModel vm)
            vm.IsOverlayActive = active;
    }

    public async Task<object?> ShowOverlayDialog(UserControl dialog)
    {
        _overlayDialogTcs = new TaskCompletionSource<object?>();
        var overlay = this.Find<Panel>("DialogOverlay");
        var content = this.Find<ContentControl>("DialogContent");

        overlay.IsVisible = true;
        content.Content = dialog;
        SetOverlayActive(true);

        if (dialog is IOverlayDialog overlayDialog)
        {
            overlayDialog.CloseRequested += () =>
            {
                overlay.IsVisible = false;
                content.Content = null;
                SetOverlayActive(false);
                _overlayDialogTcs?.SetResult(null);
                _overlayDialogTcs = null;
            };
        }

        return await _overlayDialogTcs.Task;
    }

    public async Task<TResult> ShowOverlayDialog<TResult>(UserControl dialog, Func<TResult> getResult)
    {
        _overlayDialogTcs = new TaskCompletionSource<object?>();
        var overlay = this.Find<Panel>("DialogOverlay");
        var content = this.Find<ContentControl>("DialogContent");

        overlay.IsVisible = true;
        content.Content = dialog;
        SetOverlayActive(true);

        if (dialog is IOverlayDialog overlayDialog)
        {
            overlayDialog.CloseRequested += () =>
            {
                var result = getResult();
                overlay.IsVisible = false;
                content.Content = null;
                SetOverlayActive(false);
                _overlayDialogTcs?.SetResult(null);
                _overlayDialogTcs = null;
            };
        }

        await _overlayDialogTcs.Task;
        return getResult();
    }

    public void CloseOverlayDialog()
    {
        var overlay = this.Find<Panel>("DialogOverlay");
        var content = this.Find<ContentControl>("DialogContent");

        overlay.IsVisible = false;
        content.Content = null;
        SetOverlayActive(false);
        _overlayDialogTcs?.SetResult(null);
        _overlayDialogTcs = null;
    }
}