using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace MiniScheditor.Core;

public class Point32Buffer
{
    private int[] _xs;
    private int[] _ys;
    public int Count { get; private set; }

    public ReadOnlySpan<int> Xs => _xs.AsSpan(0, Count);
    public ReadOnlySpan<int> Ys => _ys.AsSpan(0, Count);

    public Point32Buffer(int capacity = 16)
    {
        _xs = new int[capacity];
        _ys = new int[capacity];
        Count = 0;
    }

    public void Add(int x, int y)
    {
        if (Count >= _xs.Length)
        {
            int newSize = _xs.Length * 2;
            Array.Resize(ref _xs, newSize);
            Array.Resize(ref _ys, newSize);
        }
        _xs[Count] = x;
        _ys[Count] = y;
        Count++;
    }

    public void AddRange(in ReadOnlySpan<Point32> points)
    {
        int newCount = Count + points.Length;
        if (newCount > _xs.Length)
        {
            int newSize = Math.Max(_xs.Length * 2, newCount);
            Array.Resize(ref _xs, newSize);
            Array.Resize(ref _ys, newSize);
        }
        for (int i = 0; i < points.Length; i++)
        {
            _xs[Count + i] = points[i].X;
            _ys[Count + i] = points[i].Y;
        }
        Count = newCount;
    }

    public void Clear() => Count = 0;
}

