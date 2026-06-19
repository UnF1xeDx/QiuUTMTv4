using System;
using System.ComponentModel;
using System.Reflection;
using System.Reflection.Metadata;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaEdit.Search;

namespace UndertaleModToolAvalonia;

public partial class UndertaleCodeView : UserControl, IUndertaleCodeView
{
    private static IHighlightingDefinition? GMLHighlightingDefinition = null;
    private static IHighlightingDefinition? ASMHighlightingDefinition = null;

    private readonly ModifiedLinesBackgroundRenderer _gmlModifiedRenderer = new();
    private readonly ModifiedLinesBackgroundRenderer _asmModifiedRenderer = new();

    private double _zoomFontSize = 14;
    private static double LastZoomFontSize = 14;

    public (int, int) LastCaretOffsets;

    public UndertaleCodeView()
    {
        InitializeComponent();
        
        DataContextChanged += (_, __) =>
        {
            if (DataContext is UndertaleCodeViewModel vm)
            {
                vm.View = this;
                if (vm.MainVM.Settings!.UseSoraEditor&&OperatingSystem.IsAndroid())
                {
                    ClassicGmlTab.IsVisible = false;
                    ClassicAsmTab.IsVisible = false;
                    SoraEditorGmlTab.IsSelected = true;
                }
                else
                {
                    SoraEditorGmlTab.IsVisible = false;
                    SoraEditorAsmTab.IsVisible = false;
                }
                if (vm.MainVM.Settings!.EnableSyntaxHighlighting)
                {
                    UndertaleCodeView.GMLHighlightingDefinition ??= LoadHighlightingDefinition("GML");
                    GMLTextEditor.SyntaxHighlighting = UndertaleCodeView.GMLHighlightingDefinition;

                    UndertaleCodeView.ASMHighlightingDefinition ??= LoadHighlightingDefinition("ASM");
                    ASMTextEditor.SyntaxHighlighting = UndertaleCodeView.ASMHighlightingDefinition;
                }
                else
                {
                    GMLTextEditor.SyntaxHighlighting = null;
                    ASMTextEditor.SyntaxHighlighting = null;
                }

                if (this.IsAttachedToVisualTree())
                {
                    ProcessLastGoToLocation();
                }
                else
                {
                    AttachedToLogicalTree += (_, __) =>
                    {
                        ProcessLastGoToLocation();
                    };
                }

                vm.PropertyChanged += (object? source, PropertyChangedEventArgs e) =>
                {
                    if (e.PropertyName == nameof(UndertaleCodeViewModel.LastGoToLocation) && vm.LastGoToLocation is not null)
                    {
                        ProcessLastGoToLocation();
                    }
                };
            }
        };

        InitializeTextEditor(GMLTextEditor);
        InitializeTextEditor(ASMTextEditor);

        // Install search panels
        SearchPanel.Install(GMLTextEditor);
        SearchPanel.Install(ASMTextEditor);

        // Add modified lines background renderers
        GMLTextEditor.TextArea.TextView.BackgroundRenderers.Add(_gmlModifiedRenderer);
        ASMTextEditor.TextArea.TextView.BackgroundRenderers.Add(_asmModifiedRenderer);

        // Track text changes for modified lines highlighting
        GMLTextEditor.TextChanged += (s, e) =>
        {
            _gmlModifiedRenderer.MarkDirty();
        };
        ASMTextEditor.TextChanged += (s, e) =>
        {
            _asmModifiedRenderer.MarkDirty();
        };

        // Ctrl+scroll wheel zoom
        GMLTextEditor.AddHandler(PointerWheelChangedEvent, Editor_PointerWheelChanged, RoutingStrategies.Tunnel);
        ASMTextEditor.AddHandler(PointerWheelChangedEvent, Editor_PointerWheelChanged, RoutingStrategies.Tunnel);

        // Ctrl+F and Ctrl+H keybindings
        GMLTextEditor.AddHandler(KeyDownEvent, Editor_KeyDown, RoutingStrategies.Tunnel);
        ASMTextEditor.AddHandler(KeyDownEvent, Editor_KeyDown, RoutingStrategies.Tunnel);

        // Word wrap and whitespace checkboxes
        WordWrapCheck.IsCheckedChanged += WordWrapCheck_Changed;
        ShowWhitespaceCheck.IsCheckedChanged += ShowWhitespaceCheck_Changed;
    }

    static IHighlightingDefinition LoadHighlightingDefinition(string name)
    {
        using (XmlReader reader = XmlReader.Create(AssetLoader.Open(new Uri($"avares://{Assembly.GetExecutingAssembly().FullName}/Assets/Syntax{name}.xshd"))))
        {
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
    }

    void InitializeTextEditor(AvaloniaEdit.TextEditor textEditor)
    {
        textEditor.Options.ConvertTabsToSpaces = true;
        textEditor.Options.HighlightCurrentLine = true;
        textEditor.FontSize = _zoomFontSize;
    }

    private void Editor_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            ZoomChange(e.Delta.Y > 0);
        }
    }

    private void ZoomChange(bool zoomingIn)
    {
        if (zoomingIn)
        {
            if (_zoomFontSize < 100)
                _zoomFontSize += 1;
        }
        else
        {
            if (_zoomFontSize > 5)
                _zoomFontSize -= 1;
        }

        LastZoomFontSize = _zoomFontSize;
        GMLTextEditor.FontSize = _zoomFontSize;
        ASMTextEditor.FontSize = _zoomFontSize;
    }

    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+F and Ctrl+H are handled by SearchPanel automatically when installed,
        // but we handle them here explicitly to ensure they work on both editors
        // regardless of which one has focus.
    }

    private void WordWrapCheck_Changed(object? sender, EventArgs e)
    {
        if (GMLTextEditor == null) return;
        bool value = WordWrapCheck.IsChecked ?? false;
        GMLTextEditor.WordWrap = value;
        ASMTextEditor.WordWrap = value;
    }

    private void ShowWhitespaceCheck_Changed(object? sender, EventArgs e)
    {
        if (GMLTextEditor == null) return;
        bool value = ShowWhitespaceCheck.IsChecked ?? false;
        GMLTextEditor.Options.ShowSpaces = value;
        GMLTextEditor.Options.ShowTabs = value;
        ASMTextEditor.Options.ShowSpaces = value;
        ASMTextEditor.Options.ShowTabs = value;
    }

    /// <summary>
    /// Sets the original text for modified lines tracking. Call after code is loaded.
    /// </summary>
    public void SetOriginalTextForModifiedTracking()
    {
        _gmlModifiedRenderer.SetOriginalText(GMLTextEditor.Text, GMLTextEditor.Document);
        _asmModifiedRenderer.SetOriginalText(ASMTextEditor.Text, ASMTextEditor.Document);
    }

    /// <summary>
    /// Clears modified line markers (e.g., after a successful compile/save).
    /// </summary>
    public void ClearModifiedLines()
    {
        _gmlModifiedRenderer.ClearModifiedLines();
        _asmModifiedRenderer.ClearModifiedLines();
    }

    public void ProcessLastGoToLocation()
    {
        if (DataContext is UndertaleCodeViewModel vm)
        {
            if (vm.LastGoToLocation is not null)
            {
                GoToLocation(vm.LastGoToLocation.Value);
                vm.LastGoToLocation = null;
            }
        }
    }

    public void GoToLocation((UndertaleCodeViewModel.Tab tab, int line) location)
    {
        if (DataContext is UndertaleCodeViewModel vm)
        {
            vm.SelectedTab = location.tab;
            AvaloniaEdit.TextEditor textEditor = (location.tab == UndertaleCodeViewModel.Tab.GML) ? GMLTextEditor : ASMTextEditor;

            textEditor.TextArea.Caret.Column = 0;
            textEditor.TextArea.Caret.Line = location.line;
            textEditor.Focus();

            EventHandler? func = null;
            func = (_, __) =>
            {
                textEditor.ScrollToLine(location.line);
                textEditor.LayoutUpdated -= func;
            };
            textEditor.LayoutUpdated += func;

            // HACK: I don't know how to check if the layout has updated already here or not, so I just invalidate it to call the above function.
            textEditor.InvalidateMeasure();
        }
    }

    private void GMLTextEditor_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UndertaleCodeViewModel vm && vm.MainVM.Settings!.AutomaticallyCompileAndDecompileCodeOnLostFocus)
        {
            vm.CompileAndDecompileGML(onlyIfOutdated: true);
        }
    }

    private void ASMTextEditor_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is UndertaleCodeViewModel vm && vm.MainVM.Settings!.AutomaticallyCompileAndDecompileCodeOnLostFocus)
        {
            vm.CompileAndDecompileASM(onlyIfOutdated: true);
        }
    }

    private void GMLTextEditor_TextChanged(object? sender, EventArgs e)
    {
        if (DataContext is UndertaleCodeViewModel vm)
        {
            vm.GMLOutdated = true;
        }
    }

    private void ASMTextEditor_TextChanged(object? sender, EventArgs e)
    {
        if (DataContext is UndertaleCodeViewModel vm)
        {
            vm.ASMOutdated = true;
        }
    }
}

public interface IUndertaleCodeView
{
    private UndertaleCodeView View => (UndertaleCodeView)this;

    public void SaveCaretOffsets()
    {
        View.LastCaretOffsets = (View.GMLTextEditor.CaretOffset, View.ASMTextEditor.CaretOffset);
    }

    public void RestoreCaretOffsets()
    {
        View.GMLTextEditor.CaretOffset = Math.Clamp(View.LastCaretOffsets.Item1, 0, View.GMLTextEditor.Text.Length);
        View.ASMTextEditor.CaretOffset = Math.Clamp(View.LastCaretOffsets.Item2, 0, View.ASMTextEditor.Text.Length);
    }

    public int GMLCaretOffset
    {
        get { return View.GMLTextEditor.CaretOffset; }
        set { View.GMLTextEditor.CaretOffset = value; }
    }
}