using System.Windows;

namespace BackgroundAutomator.Core.TextDetection;

/// <summary>
/// Runtime/invocation-local metadata for a modern terminal control (e.g. Windows Terminal TermControl).
/// </summary>
public sealed record TerminalPaneInfo(
    string RawText,
    bool HasKeyboardFocus,
    Rect BoundingRectangle = default,
    object? ElementReference = null,
    string ClassName = "TermControl");
