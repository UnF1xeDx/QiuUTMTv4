using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace UndertaleModToolAvalonia.NativeViews;

public class SoraEditorControl: NativeControlHost
{
    private IPlatformHandle? _impl;
    private string _initialText = string.Empty;

    public SoraEditorControl()
    {
    }

    public string Text
    {
        get => Factory?.GetText(_impl) ?? _initialText;
        set
        {
            if (_impl is not null)
                Factory?.SetText(_impl, value);
            else
                _initialText = value;
        }
    }

    public static readonly StyledProperty<TextDocument> DocumentProperty = TextView.DocumentProperty.AddOwner<SoraEditorControl>();
    public TextDocument Document
    {
        get => this.GetValue<TextDocument>(SoraEditorControl.DocumentProperty);
        set => this.SetValue(SoraEditorControl.DocumentProperty, value);
    }

    /// <summary>
    /// Raised when the text in the native editor changes.
    /// </summary>
    public event EventHandler? TextChanged;

    public static ISoraEditorAndroid? Factory
    {
        get => ISoraEditorAndroid.Implementation;
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (Factory is null)
            return base.CreateNativeControlCore(parent);

        _impl = Factory.CreateControl(parent, () => base.CreateNativeControlCore(parent));

        // Set initial text if it was set before the control was created
        if (!string.IsNullOrEmpty(_initialText))
        {
            Factory.SetText(_impl, _initialText);
        }

        // Sync Document text to native editor
        var doc = Document;
        if (doc != null)
        {
            Factory.SetText(_impl, doc.Text);
        }

        // Hook up native text changed callback
        Factory.SetOnTextChanged(_impl, OnNativeTextChanged);

        return _impl;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _impl = null;
        base.DestroyNativeControlCore(control);
    }

    /// <summary>
    /// Called from the native editor when text changes.
    /// </summary>
    private void OnNativeTextChanged(string newText)
    {
        // Sync back to Document if bound
        var doc = Document;
        if (doc != null && doc.Text != newText)
        {
            doc.Text = newText;
        }

        // Raise TextChanged event
        TextChanged?.Invoke(this, EventArgs.Empty);
    }
}
