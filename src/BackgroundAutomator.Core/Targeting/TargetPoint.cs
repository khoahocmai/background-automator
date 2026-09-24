namespace BackgroundAutomator.Core.Targeting;

/// <summary>
/// Represents a 2D client coordinate strictly associated with a specific window handle (HWND).
/// Critical invariant: A client coordinate must always be interpreted relative to a specific HWND.
/// </summary>
public readonly record struct TargetPoint(IntPtr Hwnd, int ClientX, int ClientY)
{
    public static TargetPoint Empty => new(IntPtr.Zero, 0, 0);

    public bool IsEmpty => Hwnd == IntPtr.Zero;

    public override string ToString() =>
        $"[HWND {HwndFormatter.Format(Hwnd)}: ({ClientX}, {ClientY})]";
}
