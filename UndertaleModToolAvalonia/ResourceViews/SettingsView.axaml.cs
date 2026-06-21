using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace UndertaleModToolAvalonia;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (sender, args) =>
        {
            if (DataContext is SettingsViewModel vm)
            {
                vm.MainVM.Settings?.Save();
            }
        };
    }

    private async void BackgroundBrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select background image",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Image files")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp" }
                }
            }
        });

        if (files.Count > 0 && DataContext is SettingsViewModel vm)
        {
            vm.MainVM.Settings.BackgroundImagePath = files[0].TryGetLocalPath() ?? "";
            vm.MainVM.Settings.Save();
            ApplyCustomBackground();
        }
    }

    private void BackgroundClearButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.MainVM.Settings.BackgroundImagePath = "";
            vm.MainVM.Settings.Save();
            ApplyCustomBackground();
        }
    }

    private void ApplyCustomBackground()
    {
        var mainView = this.FindLogicalAncestorOfType<MainView>();
        mainView?.ApplyCustomBackground();
    }
}