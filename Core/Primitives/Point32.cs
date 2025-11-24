using System;
using System.Runtime.InteropServices;

namespace MiniScheditor.Core;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Point32 : IEquatable<Point32>, IPoint32View
{
    public int X { get; }
    public int Y { get; }

    public Point32(int x, int y)
    {
        X = x;
        Y = y;
    }

    public static Point32 operator +(Point32 a, Point32 b) => new Point32(a.X + b.X, a.Y + b.Y);
    public static Point32 operator -(Point32 a, Point32 b) => new Point32(a.X - b.X, a.Y - b.Y);

    public bool Equals(Point32 other) => X == other.X && Y == other.Y;
    public override bool Equals(object? obj) => obj is Point32 p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public static bool operator ==(Point32 left, Point32 right) => left.Equals(right);
    public static bool operator !=(Point32 left, Point32 right) => !(left == right);

    public override string ToString() => $"({X}, {Y})";
}

