namespace BackgroundAutomator.App.Services;

/// <summary>
/// Abstraction for clipboard operations with retry and fault tolerance.
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Attempts to set text on the clipboard. Returns true if successful.
    /// </summary>
    bool SetText(string text);

    /// <summary>
    /// Attempts to get text from the clipboard. Returns null if empty or inaccessible.
    /// </summary>
    string? GetText();
}
