using System.Drawing;
using System.Runtime.InteropServices;

namespace BackgroundClicker.Win32;

/// <summary>
/// Win32 2D integer point structure (POINT).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct POINT : IEquatable<POINT>
{
    public int X;
    public int Y;

    public POINT(int x, int y)
    {
        X = x;
        Y = y;
    }

    public static implicit operator Point(POINT p) => new(p.X, p.Y);
    public static implicit operator POINT(Point p) => new(p.X, p.Y);

    public bool Equals(POINT other) => X == other.X && Y == other.Y;

    public override bool Equals(object? obj) => obj is POINT other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public override string ToString() => $"({X}, {Y})";

    public static bool operator ==(POINT left, POINT right) => left.Equals(right);
    public static bool operator !=(POINT left, POINT right) => !left.Equals(right);
}

/// <summary>
/// Win32 rectangle structure (RECT).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RECT : IEquatable<RECT>
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public RECT(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public static implicit operator Rectangle(RECT r) => new(r.Left, r.Top, r.Width, r.Height);
    public static implicit operator RECT(Rectangle r) => new(r.Left, r.Top, r.Right, r.Bottom);

    public bool Equals(RECT other) =>
        Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

    public override bool Equals(object? obj) => obj is RECT other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    public override string ToString() => $"[({Left}, {Top}) - ({Right}, {Bottom}) ({Width}x{Height})]";

    public static bool operator ==(RECT left, RECT right) => left.Equals(right);
    public static bool operator !=(RECT left, RECT right) => !left.Equals(right);
}

/// <summary>
/// Callback delegate for EnumWindows.
/// </summary>
public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

/// <summary>
/// Callback delegate for EnumChildWindows.
/// </summary>
public delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);
