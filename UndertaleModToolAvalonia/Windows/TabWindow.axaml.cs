using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace UndertaleModToolAvalonia;

public partial class TabWindow : Window
{
    private readonly MainViewModel _mainVM;

    public ObservableCollection<TabItemViewModel> Tabs { get; } = [];

    private TabItemViewModel? _tabSelected;
    public TabItemViewModel? TabSelected
    {
        get => _tabSelected;
        set
        {
            _tabSelected = value;
            if (value is not null)
            {
                foreach (var tab in Tabs)
                    tab.IsSelected = (tab == value);
            }
        }
    }

    public int TabSelectedIndex
    {
        get => TabSelected is null ? -1 : Tabs.IndexOf(TabSelected);
        set
        {
            if (value >= 0 && value < Tabs.Count)
                TabSelected = Tabs[value];
        }
    }

    public TabWindow(MainViewModel mainVM, TabItemViewModel tab)
    {
        InitializeComponent();

        _mainVM = mainVM;

        // Move tab from main window to this window
        _mainVM.Tabs.Remove(tab);
        tab.IsSelected = true;
        Tabs.Add(tab);
        TabSelected = tab;

        DataContext = this;

        Closed += TabWindow_Closed;
    }

    private void TabWindow_Closed(object? sender, System.EventArgs e)
    {
        // Return all tabs to main window
        foreach (var tab in Tabs)
        {
            tab.IsSelected = false;
            _mainVM.Tabs.Add(tab);
        }
        Tabs.Clear();

        // Select the last tab in main window
        if (_mainVM.Tabs.Count > 0)
            _mainVM.TabSelected = _mainVM.Tabs[^1];
    }

    private void TabMenu_Close_Click(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Control control)
        {
            TabStripItem? tabItem = control.FindLogicalAncestorOfType<TabStripItem>();
            if (tabItem?.DataContext is TabItemViewModel vmTabItem)
            {
                CloseTab(vmTabItem);
            }
        }
    }

    private void TabMenu_ReturnToMain_Click(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Control control)
        {
            TabStripItem? tabItem = control.FindLogicalAncestorOfType<TabStripItem>();
            if (tabItem?.DataContext is TabItemViewModel vmTabItem)
            {
                ReturnTabToMain(vmTabItem);
            }
        }
    }

    private void TabCloseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is TabItemViewModel vmTabItem)
        {
            CloseTab(vmTabItem);
        }
    }

    private void CloseTab(TabItemViewModel tab)
    {
        Tabs.Remove(tab);
        if (Tabs.Count == 0)
        {
            Close();
        }
        else
        {
            TabSelected = Tabs[0];
        }
    }

    private void ReturnTabToMain(TabItemViewModel tab)
    {
        Tabs.Remove(tab);
        tab.IsSelected = false;
        _mainVM.Tabs.Add(tab);
        _mainVM.TabSelected = tab;

        if (Tabs.Count == 0)
        {
            Close();
        }
        else
        {
            TabSelected = Tabs[0];
        }
    }
}
