using Android.App;
using Android.Graphics;
using IO.Github.Rosemoe.Sora.Event;
using IO.Github.Rosemoe.Sora.Langs.Textmate;
using IO.Github.Rosemoe.Sora.Langs.Textmate.Registry;
using IO.Github.Rosemoe.Sora.Langs.Textmate.Registry.Model;
using IO.Github.Rosemoe.Sora.Langs.Textmate.Registry.Provider;
using IO.Github.Rosemoe.Sora.Widget;
using Org.Eclipse.Tm4e.Core.Registry;
using UndertaleModToolAvalonia.NativeViews;

namespace UndertaleModToolAvalonia.Android.NativeViews;

using System;
using Avalonia.Platform;
using Avalonia.Android;

public class SoraEditorAndroid : ISoraEditorAndroid
{
    private static bool _doMeOnlyOnceFlag;

    private Activity activity;

    public SoraEditorAndroid(Activity activity)
    {
        this.activity = activity;
    }
    public static void DoMeOnlyOnce()
    {
        if (!_doMeOnlyOnceFlag)
        {
            _doMeOnlyOnceFlag = true;
            FileProviderRegistry.Instance.AddFileProvider(
                new LocalFileProvider(AppContext.BaseDirectory)
            );
            var themeRegistry = ThemeRegistry.Instance;
            var themeName = "solarized_dark";
            var themeAssetsPath = "textmate/" + themeName + ".json";
            var themeModel = new ThemeModel(
                IThemeSource.FromInputStream(
                    FileProviderRegistry.Instance.TryGetInputStream(themeAssetsPath), themeAssetsPath, null
                ),
                themeName
            );
            themeModel.Dark = true;
            themeRegistry.LoadTheme(themeModel);
            themeRegistry.SetTheme(themeName);
            GrammarRegistry.Instance.LoadGrammars("textmate/language.json");
        }
    }
    public IPlatformHandle CreateControl(IPlatformHandle parent, Func<IPlatformHandle> createDefault)
    {
        DoMeOnlyOnce();
        var parentContext = (parent as AndroidViewControlHandle)?.View.Context
                            ?? global::Android.App.Application.Context;

        var codeEditor = new CodeEditor(parentContext);
        codeEditor.TypefaceText = Typeface.Monospace;
        codeEditor.NonPrintablePaintingFlags = (
            CodeEditor.FlagDrawWhitespaceLeading | CodeEditor.FlagDrawLineSeparator |
            CodeEditor.FlagDrawWhitespaceInSelection);
        codeEditor.ColorScheme = TextMateColorScheme.Create(ThemeRegistry.Instance);
        var languageScopeName = "source.gml";
        var language = TextMateLanguage.Create(
            languageScopeName, true
        );
        codeEditor.EditorLanguage = language;

        return new AndroidViewControlHandle(codeEditor);
    }

    public void SetText(IPlatformHandle androidViewControlHandle, string text)
    {
        var codeEditor = (androidViewControlHandle as AndroidViewControlHandle).View as CodeEditor;
        codeEditor.SetText(text);
    }

    public string GetText(IPlatformHandle androidViewControlHandle)
    {
        var codeEditor = (androidViewControlHandle as AndroidViewControlHandle).View as CodeEditor;
        return codeEditor.Text.ToString();
    }

    public void SetOnTextChanged(IPlatformHandle androidViewControlHandle, Action<string> callback)
    {
        var codeEditor = (androidViewControlHandle as AndroidViewControlHandle).View as CodeEditor;
        var eventClass = Java.Lang.Class.FromType(typeof(ContentChangeEvent));
        codeEditor.SubscribeAlways(
            eventClass,
            new TextChangedReceiver(callback, codeEditor)
        );
    }

    private class TextChangedReceiver : Java.Lang.Object, EventManager.INoUnsubscribeReceiver
    {
        private readonly Action<string> _callback;
        private readonly CodeEditor _editor;

        public TextChangedReceiver(Action<string> callback, CodeEditor editor)
        {
            _callback = callback;
            _editor = editor;
        }

        public void OnEvent(Java.Lang.Object? evt)
        {
            _callback?.Invoke(_editor.Text.ToString());
        }
    }
}
