using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MiniScheditor.Core;

public readonly struct Size32
{
    public int Width { get; }
    public int Height { get; }
    public Size32(int width, int height)
    {
        this.Width = width;
        this.Height = height;
    }

    public bool IsEmpty
    {
        get { return Width == 0.0 && Height == 0.0; }
    }

    public static Size32 Empty => new Size32(0, 0);

    public static bool operator ==(Size32 lhs, Size32 rhs) => lhs.Width == rhs.Width && lhs.Height == rhs.Height;
    public static bool operator !=(Size32 lhs, Size32 rhs) => lhs.Width != rhs.Width || lhs.Height != rhs.Height;

    public static Size32 operator /(Size32 size, int value)
    {
        if (value == 0)
        {
            throw new DivideByZeroException();
        }
        return new Size32(size.Width / value, size.Height / value);
    }

    public static Size32 operator *(Size32 size, int value) => new Size32(size.Width * value, size.Height * value);

    public override int GetHashCode()
    {
        return base.GetHashCode();
    }

    public override bool Equals(object? obj)
    {
        if (obj is null)
        {
            return false;
        }

        if (!(obj is Size32))
        {
            return false;
        }

        Size32 comp = (Size32)obj;
        return comp.Width == this.Width && comp.Height == this.Height && comp.GetType().Equals(this.GetType());
    }

    public override string ToString() => string.Format(CultureInfo.CurrentCulture, "Width = {0}, Height = {1}", Width, Height);
}