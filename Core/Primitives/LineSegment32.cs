using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace MiniScheditor.Core;

[StructLayout(LayoutKind.Sequential)]
public readonly struct LineSegment32 : IEquatable<LineSegment32>
{
    private readonly Vector128<int> _vector;

    public Point32 Start => new Point32(_vector.GetElement(0), _vector.GetElement(1));
    public Point32 End => new Point32(_vector.GetElement(2), _vector.GetElement(3));

    public LineSegment32(Point32 start, Point32 end)
    {
        _vector = Vector128.Create(start.X, start.Y, end.X, end.Y);
    }

    public bool Equals(LineSegment32 other) => _vector.Equals(other._vector);
    public override bool Equals(object? obj) => obj is LineSegment32 e && Equals(e);
    public override int GetHashCode() => _vector.GetHashCode();
    public static bool operator ==(LineSegment32 left, LineSegment32 right) => left.Equals(right);
    public static bool operator !=(LineSegment32 left, LineSegment32 right) => !(left == right);

    public override string ToString() => $"{Start} -> {End}";
}
