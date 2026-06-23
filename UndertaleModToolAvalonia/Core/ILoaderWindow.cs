namespace UndertaleModToolAvalonia;

public interface ILoaderWindow
{
    public void EnsureShown();
    void SetMessage(string message);
    void SetStatus(string status);
    void SetValue(int value);
    void SetMaximum(int maximum);
    void SetText(string text);
    void SetTextToMessageAndStatus(string status);
    void Close();
}
