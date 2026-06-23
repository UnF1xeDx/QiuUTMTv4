using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Xaml.Interactions.DragAndDrop;

namespace UndertaleModToolAvalonia;

public class TabStripItemDropHandler : DropHandlerBase
{
    public override bool Validate(object? sender, DragEventArgs e, object? sourceContext, object? targetContext, object? state)
    {
        if (sourceContext is TabItemViewModel)
        {
            return true;
        }

        return false;
    }

    public override bool Execute(object? sender, DragEventArgs e, object? sourceContext, object? targetContext, object? state)
    {
        if (targetContext is MainViewModel mainVM)
        {
            if (e.Source is Control control && sourceContext is TabItemViewModel draggedTabItem)
            {
                int draggedIndex = mainVM.Tabs.IndexOf(draggedTabItem);
                int droppedIndex = mainVM.Tabs.Count - 1;

                var sourceTabStripItem = control.FindLogicalAncestorOfType<TabStripItem>();
                if (sourceTabStripItem is not null && sourceTabStripItem.DataContext is TabItemViewModel droppedTabItem)
                {
                    droppedIndex = mainVM.Tabs.IndexOf(droppedTabItem);
                }

                MoveItem(mainVM.Tabs, draggedIndex, droppedIndex);

                mainVM.TabSelected = draggedTabItem;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Called from the TabStrip's DragOver event.
    /// Tab detach is not supported on Android - tabs always stay in MainWindow.
    /// </summary>
    public void OnDragOver(object? sender, DragEventArgs e, object? sourceContext, object? targetContext)
    {
        // No-op: tab detach is disabled for Android compatibility
    }
}
