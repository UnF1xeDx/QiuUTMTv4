using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace UndertaleModToolAvalonia;

/// <summary>
/// Highlights modified (dirty) lines in the code editor with a colored background.
/// Ported from the WPF ModifiedLinesBackgroundRenderer in the Genouka fork.
/// </summary>
public class ModifiedLinesBackgroundRenderer : IBackgroundRenderer
{
    private readonly HashSet<int> _modifiedLineNumbers = new();
    private string? _originalText;
    private ITextSource? _currentTextSource;
    private TextView? _textView;

    public static readonly SolidColorBrush DefaultModifiedBrush =
        new SolidColorBrush(Color.FromArgb(40, 255, 152, 0));

    public Brush ModifiedLineBrush { get; set; } = DefaultModifiedBrush;

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        _textView = textView;

        if (_modifiedLineNumbers.Count == 0)
            return;

        if (textView.Document == null)
            return;

        foreach (int lineNumber in _modifiedLineNumbers)
        {
            if (lineNumber < 1 || lineNumber > textView.Document.LineCount)
                continue;

            var line = textView.Document.GetLineByNumber(lineNumber);
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
            {
                drawingContext.DrawRectangle(ModifiedLineBrush, null,
                    new Rect(rect.X, rect.Y, textView.Bounds.Width, rect.Height));
            }
        }
    }

    /// <summary>
    /// Sets the original text to compare against. Call this when code is first loaded.
    /// </summary>
    public void SetOriginalText(string text, IDocument document)
    {
        _originalText = text;
        _currentTextSource = document;
        RecalculateModifiedLines();
        RequestRedraw();
    }

    /// <summary>
    /// Marks the document as dirty (user has made changes). Recalculates modified lines.
    /// </summary>
    public void MarkDirty()
    {
        RecalculateModifiedLines();
        RequestRedraw();
    }

    /// <summary>
    /// Clears all modified line markers (e.g., after a successful compile/save).
    /// </summary>
    public void ClearModifiedLines()
    {
        _modifiedLineNumbers.Clear();
        RequestRedraw();
    }

    private void RecalculateModifiedLines()
    {
        _modifiedLineNumbers.Clear();

        if (_originalText == null || _currentTextSource == null)
            return;

        string currentText = _currentTextSource.Text;
        if (currentText == _originalText)
            return;

        var originalLines = _originalText.Split('\n');
        var currentLines = currentText.Split('\n');

        // Simple line-by-line diff
        int maxLines = Math.Max(originalLines.Length, currentLines.Length);
        for (int i = 0; i < maxLines; i++)
        {
            string? originalLine = i < originalLines.Length ? originalLines[i] : null;
            string? currentLine = i < currentLines.Length ? currentLines[i] : null;

            if (originalLine != currentLine)
            {
                _modifiedLineNumbers.Add(i + 1); // Line numbers are 1-based
            }
        }
    }

    private void RequestRedraw()
    {
        _textView?.InvalidateLayer(Layer);
    }
}
