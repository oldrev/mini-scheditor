using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

namespace MiniScheditor.Core;

public readonly struct Polygon32 : IEquatable<Polygon32>, IReadOnlyList<Point32>
{
    public int[] Xs { get; }
    public int[] Ys { get; }
    public int Count => Xs?.Length ?? 0;

    public Point32 this[int index] => new Point32(Xs[index], Ys[index]);

    public Polygon32(int[] xs, int[] ys)
    {
        if (xs.Length != ys.Length) throw new ArgumentException("Arrays must have same length");
        Xs = xs;
        Ys = ys;
    }

    public Polygon32(Point32[] points)
    {
        Xs = new int[points.Length];
        Ys = new int[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            Xs[i] = points[i].X;
            Ys[i] = points[i].Y;
        }
    }

    public bool Equals(Polygon32 other)
    {
        if (Count != other.Count)
        {
            return false;
        }
        if (Xs == other.Xs && Ys == other.Ys)
        {
            return true;
        }
        return Xs.SequenceEqual(other.Xs) && Ys.SequenceEqual(other.Ys);
    }

    public override bool Equals(object? obj) => obj is Polygon32 p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(Xs, Ys);
    public static bool operator ==(Polygon32 left, Polygon32 right) => left.Equals(right);
    public static bool operator !=(Polygon32 left, Polygon32 right) => !(left == right);

    public IEnumerator<Point32> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return new Point32(Xs[i], Ys[i]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerable<Line32> Edges
    {
        get
        {
            if (Count < 2) yield break;
            for (int i = 0; i < Count; i++)
            {
                yield return new Line32(this[i], this[(i + 1) % Count]);
            }
        }
    }

    public override string ToString() => $"Polygon32 with {Count} points";
}

