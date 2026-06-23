using System;
using Avalonia.Platform;

namespace UndertaleModToolAvalonia.NativeViews;

public interface ISoraEditorAndroid
{
    public IPlatformHandle CreateControl(IPlatformHandle parent, Func<IPlatformHandle> createDefault);
    public void SetText(IPlatformHandle androidViewControlHandle, string text); 
    public string GetText(IPlatformHandle androidViewControlHandle);
    public void SetOnTextChanged(IPlatformHandle androidViewControlHandle, Action<string> callback);
    public void SetVisible(IPlatformHandle androidViewControlHandle, bool visible);
    public void RequestFocus(IPlatformHandle androidViewControlHandle);
    public void SetOnFocusChanged(IPlatformHandle androidViewControlHandle, Action<bool> callback);

    public static ISoraEditorAndroid? Implementation { get; set; }
}