using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MiniScheditor.Core;

public readonly struct PolyLine32 : IEquatable<PolyLine32>, IReadOnlyList<Point32>
{
    public int[] Xs { get; }
    public int[] Ys { get; }
    public int Count => Xs?.Length ?? 0;

    public Point32 this[int index] => new Point32(Xs[index], Ys[index]);

    public PolyLine32(int[] xs, int[] ys)
    {
        if (xs.Length != ys.Length) throw new ArgumentException("Arrays must have same length");
        Xs = xs;
        Ys = ys;
    }

    public PolyLine32(Point32[] points)
    {
        Xs = new int[points.Length];
        Ys = new int[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            Xs[i] = points[i].X;
            Ys[i] = points[i].Y;
        }
    }

    public bool Equals(PolyLine32 other)
    {
        if (Count != other.Count) return false;
        if (Xs == other.Xs && Ys == other.Ys) return true;
        return Xs.SequenceEqual(other.Xs) && Ys.SequenceEqual(other.Ys);
    }

    public override bool Equals(object? obj) => obj is PolyLine32 p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(Xs, Ys);
    public static bool operator ==(PolyLine32 left, PolyLine32 right) => left.Equals(right);
    public static bool operator !=(PolyLine32 left, PolyLine32 right) => !(left == right);

    public IEnumerator<Point32> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return new Point32(Xs[i], Ys[i]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"Path32 with {Count} points";
}

