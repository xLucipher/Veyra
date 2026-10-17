using System.Runtime.InteropServices;

namespace Veyra.Interop;

/// <summary>Win32-RECT. Da die Anwendung Per-Monitor-DPI-v2 ist, sind alle Werte physische Pixel.</summary>
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

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
    public readonly bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>True, wenn dieses Rechteck <paramref name="other"/> vollständig überdeckt (mit Toleranz in Pixeln).</summary>
    public readonly bool Covers(RECT other, int tolerance = 0) =>
        Left <= other.Left + tolerance &&
        Top <= other.Top + tolerance &&
        Right >= other.Right - tolerance &&
        Bottom >= other.Bottom - tolerance;

    public readonly bool Intersects(RECT other) =>
        Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    public readonly bool Equals(RECT other) =>
        Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

    public override readonly bool Equals(object? obj) => obj is RECT r && Equals(r);
    public override readonly int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
    public override readonly string ToString() => $"[{Left},{Top} {Width}x{Height}]";
}

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}
