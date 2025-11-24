
using System;
using System.Runtime.InteropServices;

namespace MiniScheditor.Core;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Rect32 : IEquatable<Rect32>
{
    public int Left { get; }
    public int Top { get; }
    public int Right { get; }
    public int Bottom { get; }

    public int X => Left;
    public int Y => Top;
    public long Width => (long)Right - Left;
    public long Height => (long)Bottom - Top;

    public Rect32(int x, int y, int width, int height)
    {
        Left = x;
        Top = y;
        Right = x + width;
        Bottom = y + height;
    }

    private Rect32(int left, int top, int right, int bottom, bool useCoordinates)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public static Rect32 FromLTRB(int left, int top, int right, int bottom)
    {
        return new Rect32(left, top, right, bottom, true);
    }

    public bool Contains(in Point32 p)
    {
        return p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;
    }

    public bool Intersects(in Rect32 other)
    {
        return Left < other.Right && Right > other.Left &&
               Top < other.Bottom && Bottom > other.Top;
    }

    public bool Contains(in Rect32 other)
    {
        return Left <= other.Left && Right >= other.Right &&
               Top <= other.Top && Bottom >= other.Bottom;
    }

    public static Rect32 Intersect(in Rect32 a, in Rect32 b)
    {
        int x1 = Math.Max(a.X, b.X);
        int x2 = Math.Min(a.Right, b.Right);
        int y1 = Math.Max(a.Y, b.Y);
        int y2 = Math.Min(a.Bottom, b.Bottom);

        if (x2 >= x1 && y2 >= y1)
        {
            return FromLTRB(x1, y1, x2, y2);
        }
        return new Rect32(0, 0, 0, 0);
    }

    /// <summary>
    /// Creates a Rect32 from two points, safely handling width/height larger than int.MaxValue.
    /// </summary>
    public static Rect32 FromPoints(Point32 p1, Point32 p2)
    {
        int x1 = Math.Min(p1.X, p2.X);
        int y1 = Math.Min(p1.Y, p2.Y);
        int x2 = Math.Max(p1.X, p2.X);
        int y2 = Math.Max(p1.Y, p2.Y);
        return FromLTRB(x1, y1, x2, y2);
    }

    public bool Equals(Rect32 other) => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
    public override bool Equals(object? obj) => obj is Rect32 r && Equals(r);
    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
    public static bool operator ==(Rect32 left, Rect32 right) => left.Equals(right);
    public static bool operator !=(Rect32 left, Rect32 right) => !(left == right);

    public override string ToString() => $"[X={X}, Y={Y}, W={Width}, H={Height}]";
}
