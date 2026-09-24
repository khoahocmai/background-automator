namespace BackgroundAutomator.Win32;

/// <summary>
/// Helper for encoding and decoding Win32 mouse message coordinates and parameters.
/// Preserves signed 16-bit coordinate semantics across packing and unpacking.
/// </summary>
public static class MouseMessageHelper
{
    /// <summary>
    /// Packs 2D client coordinates (x, y) into a Win32 mouse message LPARAM.
    /// Preserves signed 16-bit coordinate values.
    /// </summary>
    /// <param name="x">Client X coordinate (signed 16-bit integer).</param>
    /// <param name="y">Client Y coordinate (signed 16-bit integer).</param>
    /// <returns>Native IntPtr LPARAM representation for mouse messages.</returns>
    public static IntPtr MakeMouseLParam(int x, int y)
    {
        uint low = (ushort)(short)x;
        uint high = (ushort)(short)y;
        return unchecked((IntPtr)(int)(low | (high << 16)));
    }

    /// <summary>
    /// Decodes the signed 16-bit X client coordinate from a Win32 mouse message LPARAM.
    /// </summary>
    public static int GetMouseX(IntPtr lParam)
    {
        return unchecked((short)((long)lParam & 0xFFFF));
    }

    /// <summary>
    /// Decodes the signed 16-bit Y client coordinate from a Win32 mouse message LPARAM.
    /// </summary>
    public static int GetMouseY(IntPtr lParam)
    {
        return unchecked((short)(((long)lParam >> 16) & 0xFFFF));
    }
}
