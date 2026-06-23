using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace UndertaleModToolAvalonia.NativeViews;

public class SoraEditorControl: NativeControlHost
{
    private IPlatformHandle? _impl;
    private string _initialText = string.Empty;
    private bool _isSyncingDocument;
    private bool _isSyncingFocus;


    public SoraEditorControl()
    {
        Focusable = true;
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

    public new bool Focus()
    {
        // Delegate focus to the native Android editor
        if (_impl is not null && Factory is not null)
        {
            Factory.RequestFocus(_impl);
            return true;
        }
        return base.Focus();
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);

        // When Avalonia sets focus to this control, delegate to the native editor
        if (!_isSyncingFocus && _impl is not null && Factory is not null)
        {
            _isSyncingFocus = true;
            try
            {
                Factory.RequestFocus(_impl);
            }
            finally
            {
                _isSyncingFocus = false;
            }
        }
    }

    /// <summary>
    /// Called when the native Android editor gains or loses focus.
    /// When the native control gains focus (e.g. from a user tap), we claim
    /// Avalonia focus so that the framework does not redirect it elsewhere.
    /// </summary>
    private void OnNativeFocusChanged(bool hasFocus)
    {
        if (_isSyncingFocus)
            return;

        if (hasFocus)
        {
            _isSyncingFocus = true;
            try
            {
                // Claim Avalonia focus so the framework knows this control is active
                base.Focus();
            }
            finally
            {
                _isSyncingFocus = false;
            }
        }
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

        // Hook up native focus changed callback for bidirectional focus sync
        Factory.SetOnFocusChanged(_impl, OnNativeFocusChanged);

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
        _isSyncingDocument = true;
        try
        {
            var doc = Document;
            if (doc != null && doc.Text != newText)
            {
                doc.Text = newText;
            }
        }
        finally
        {
            _isSyncingDocument = false;
        }

        // Raise TextChanged event
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DocumentProperty)
        {
            if (change.NewValue is TextDocument newDoc)
            {
                // Sync document text to native editor
                if (_impl is not null && Factory is not null)
                {
                    try
                    {
                        Factory.SetText(_impl, newDoc.Text);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Native control disposed, will be recreated when tab is re-selected
                    }
                }
                else
                {
                    _initialText = newDoc.Text;
                }

                // Listen for document text changes
                newDoc.TextChanged += OnDocumentTextChanged;
            }

            if (change.OldValue is TextDocument oldDoc)
            {
                oldDoc.TextChanged -= OnDocumentTextChanged;
            }
        }
        else if (change.Property == IsVisibleProperty)
        {
            // Propagate visibility changes to the native Android view so it doesn't
            // render on top of overlay dialogs
            if (_impl is not null && Factory is not null)
            {
                Factory.SetVisible(_impl, IsVisible);
            }
        }
    }

    private void OnDocumentTextChanged(object? sender, EventArgs e)
    {
        if (_isSyncingDocument || _impl is null)
            return;

        try
        {
            if (Factory is not null && sender is TextDocument doc)
            {
                Factory.SetText(_impl, doc.Text);
            }
        }
        catch (ObjectDisposedException)
        {
            // Native control has been disposed (e.g. tab switched away), ignore
        }
    }
}