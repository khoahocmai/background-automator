using BackgroundAutomator.Win32;

namespace BackgroundAutomator.Core.Keyboard;

/// <summary>
/// Supported keys for background keyboard simulation without physical cursor movement or focus stealing.
/// </summary>
public enum BackgroundKey
{
    Enter,
    Tab,
    Escape,
    Space,
    ArrowUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight,
    Backspace,
    Delete,
    Home,
    End,
    PageUp,
    PageDown
}

public static class BackgroundKeyExtensions
{
    /// <summary>
    /// Translates a <see cref="BackgroundKey"/> to its standard Win32 virtual-key code.
    /// </summary>
    public static uint ToVirtualKey(this BackgroundKey key) => key switch
    {
        BackgroundKey.Enter => NativeConstants.VK_RETURN,
        BackgroundKey.Tab => NativeConstants.VK_TAB,
        BackgroundKey.Escape => NativeConstants.VK_ESCAPE,
        BackgroundKey.Space => NativeConstants.VK_SPACE,
        BackgroundKey.ArrowUp => NativeConstants.VK_UP,
        BackgroundKey.ArrowDown => NativeConstants.VK_DOWN,
        BackgroundKey.ArrowLeft => NativeConstants.VK_LEFT,
        BackgroundKey.ArrowRight => NativeConstants.VK_RIGHT,
        BackgroundKey.Backspace => NativeConstants.VK_BACK,
        BackgroundKey.Delete => NativeConstants.VK_DELETE,
        BackgroundKey.Home => NativeConstants.VK_HOME,
        BackgroundKey.End => NativeConstants.VK_END,
        BackgroundKey.PageUp => NativeConstants.VK_PRIOR,
        BackgroundKey.PageDown => NativeConstants.VK_NEXT,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported background key.")
    };
}
