using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UndertaleModToolAvalonia;

public class LoaderOverlay : ILoaderWindow
{
    private readonly UserControl _view;

    public LoaderOverlay(UserControl view)
    {
        _view = view;
        _view.Find<Panel>("DialogOverlay").IsVisible = true;
        _view.Find<ContentControl>("DialogContent").Content = null;
        _view.Find<StackPanel>("TextInputBox").IsVisible = true;
        _view.Find<Button>("ButtonOk").IsVisible = false;
        _view.Find<Button>("ButtonCancel").IsVisible = false;
        _view.Find<Button>("ButtonYes").IsVisible = false;
        _view.Find<Button>("ButtonNo").IsVisible = false;
        _view.Find<TextBox>("TextTextBox").IsVisible = false;
        _view.Find<ProgressBar>("TextBoxLoadingProgressBar").IsVisible = true;
        _view.Find<ProgressBar>("TextBoxLoadingProgressBar").IsIndeterminate = true;
        _view.Find<TextBlock>("TitleText").Text = "Loading...";
        _view.Find<TextBlock>("MessageText").Text = "";

        _view.Find<Button>("ButtonOk").Click += OnClickOk;
    }

    public void ShowOkButton(bool show = true)
    {
        _view.Find<Button>("ButtonOk").IsVisible = show;
    }

    protected void OnClickOk(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    public void EnsureShown()
    {
        _view.Find<StackPanel>("TextInputBox").IsVisible = true;
    }

    public void SetMessage(string message)
    {
        _view.Find<TextBlock>("TitleText").Text = message;
    }

    public void SetStatus(string status)
    {
        _view.Find<TextBlock>("MessageText").Text = status;
    }

    public void SetValue(int value)
    {
        _view.Find<ProgressBar>("TextBoxLoadingProgressBar").IsIndeterminate = false;
        _view.Find<ProgressBar>("TextBoxLoadingProgressBar").Value = value;
    }

    public void SetMaximum(int maximum)
    {
        _view.Find<ProgressBar>("TextBoxLoadingProgressBar").IsIndeterminate = false;
        _view.Find<ProgressBar>("TextBoxLoadingProgressBar").Maximum = maximum;
    }

    public void SetText(string text)
    {
        _view.Find<TextBlock>("MessageText").Text = text;
    }

    public string GetText()
    {
        return _view.Find<TextBlock>("MessageText").Text ?? "";
    }

    public void SetTextToMessageAndStatus(string status)
    {
        _view.Find<TextBlock>("MessageText").Text = status;
    }

    public void Close()
    {
        _view.Find<StackPanel>("TextInputBox").IsVisible = false;
        _view.Find<Panel>("DialogOverlay").IsVisible = false;
        _view.Find<TextBlock>("MessageText").Text = "";
        _view.Find<Button>("ButtonOk").Click -= OnClickOk;
    }
}
