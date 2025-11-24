using System;
using System.Runtime.InteropServices;

namespace MiniScheditor.Core;

[StructLayout(LayoutKind.Sequential)]
public readonly struct DenseMatrix32 : IEquatable<DenseMatrix32>
{
    private readonly int[] _data;
    public int Rows { get; }
    public int Cols { get; }

    public DenseMatrix32(int rows, int cols)
    {
        if (rows <= 0 || cols <= 0)
            throw new ArgumentException("Rows and columns must be positive.");

        Rows = rows;
        Cols = cols;
        _data = new int[rows * cols];
    }

    public DenseMatrix32(int rows, int cols, int[] data)
    {
        if (rows <= 0 || cols <= 0)
            throw new ArgumentException("Rows and columns must be positive.");
        if (data.Length != rows * cols)
            throw new ArgumentException("Data length must match rows * cols.");

        Rows = rows;
        Cols = cols;
        _data = (int[])data.Clone();
    }

    public int this[int row, int col]
    {
        get
        {
            if (row < 0 || row >= Rows || col < 0 || col >= Cols)
                throw new IndexOutOfRangeException();
            return _data[row * Cols + col];
        }
    }

    public DenseMatrix32 Set(int row, int col, int value)
    {
        if (row < 0 || row >= Rows || col < 0 || col >= Cols)
            throw new IndexOutOfRangeException();
        var newData = (int[])_data.Clone();
        newData[row * Cols + col] = value;
        return new DenseMatrix32(Rows, Cols, newData);
    }

    public static DenseMatrix32 operator +(DenseMatrix32 a, DenseMatrix32 b)
    {
        if (a.Rows != b.Rows || a.Cols != b.Cols)
            throw new ArgumentException("Matrices must have the same dimensions.");
        var result = new int[a.Rows * a.Cols];
        for (int i = 0; i < result.Length; i++)
            result[i] = a._data[i] + b._data[i];
        return new DenseMatrix32(a.Rows, a.Cols, result);
    }

    public static DenseMatrix32 operator *(DenseMatrix32 a, DenseMatrix32 b)
    {
        if (a.Cols != b.Rows)
            throw new ArgumentException("Number of columns in A must equal number of rows in B.");
        var result = new int[a.Rows * b.Cols];
        for (int i = 0; i < a.Rows; i++)
        {
            for (int j = 0; j < b.Cols; j++)
            {
                int sum = 0;
                for (int k = 0; k < a.Cols; k++)
                    sum += a[i, k] * b[k, j];
                result[i * b.Cols + j] = sum;
            }
        }
        return new DenseMatrix32(a.Rows, b.Cols, result);
    }

    public bool Equals(DenseMatrix32 other) =>
        Rows == other.Rows && Cols == other.Cols && _data.AsSpan().SequenceEqual(other._data);

    public override bool Equals(object? obj) => obj is DenseMatrix32 m && Equals(m);
    public override int GetHashCode() => HashCode.Combine(Rows, Cols, _data);
    public static bool operator ==(DenseMatrix32 left, DenseMatrix32 right) => left.Equals(right);
    public static bool operator !=(DenseMatrix32 left, DenseMatrix32 right) => !(left == right);

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Rows; i++)
        {
            for (int j = 0; j < Cols; j++)
                sb.Append(_data[i * Cols + j]).Append(' ');
            sb.AppendLine();
        }
        return sb.ToString();
    }
}