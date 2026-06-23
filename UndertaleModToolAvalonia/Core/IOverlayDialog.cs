using System;
using Avalonia.Controls;

namespace UndertaleModToolAvalonia;

public interface IOverlayDialog
{
    event Action? CloseRequested;
}
