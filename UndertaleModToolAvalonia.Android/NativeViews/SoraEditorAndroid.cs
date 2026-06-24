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

        // Ensure the editor is focusable so it can receive touch events and show the
        // text actions popup (copy/cut/paste/select all) on long press
        codeEditor.Focusable = true;
        codeEditor.FocusableInTouchMode = true;

        // Ensure the editor is important for autofill so the system properly manages
        // the input connection and text selection actions
        codeEditor.ImportantForAutofill = (global::Android.Views.ImportantForAutofill)2;

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

    public void SetVisible(IPlatformHandle androidViewControlHandle, bool visible)
    {
        var codeEditor = (androidViewControlHandle as AndroidViewControlHandle)?.View as CodeEditor;
        if (codeEditor != null)
        {
            codeEditor.Visibility = visible ? (global::Android.Views.ViewStates)0 : (global::Android.Views.ViewStates)4;
        }
    }

    public void RequestFocus(IPlatformHandle androidViewControlHandle)
    {
        var codeEditor = (androidViewControlHandle as AndroidViewControlHandle)?.View as CodeEditor;
        if (codeEditor != null)
        {
            codeEditor.RequestFocus();
        }
    }

    public void SetOnFocusChanged(IPlatformHandle androidViewControlHandle, Action<bool> callback)
    {
        var view = (androidViewControlHandle as AndroidViewControlHandle)?.View;
        if (view != null)
        {
            view.FocusChange += (s, e) => callback(e.HasFocus);
        }
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

    private class FocusChangeListener : Java.Lang.Object, global::Android.Views.View.IOnFocusChangeListener
    {
        private readonly Action<bool> _callback;

        public FocusChangeListener(Action<bool> callback)
        {
            _callback = callback;
        }

        public void OnFocusChange(global::Android.Views.View? v, bool hasFocus)
        {
            _callback?.Invoke(hasFocus);
        }
    }
}