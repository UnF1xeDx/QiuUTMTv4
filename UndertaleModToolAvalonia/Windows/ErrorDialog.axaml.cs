using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace UndertaleModToolAvalonia;

public partial class ErrorDialog : Window
{
    public string DialogTitle { get; set; } = "Error";
    public string Message { get; set; } = "";
    public string ErrorText { get; set; } = "";

    public ErrorDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ErrorDialog(string title, string message, string errorText) : this()
    {
        DialogTitle = title ?? "Error";
        Message = message;
        ErrorText = errorText ?? message;
    }

    private void OKButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void CopyAllButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(ErrorText))
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(ErrorText);
            }
        }
    }
}
