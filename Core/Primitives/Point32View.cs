namespace MiniScheditor.Core;

public readonly ref struct Point32View : IPoint32View
{
    public int X { get; }
    public int Y { get; }

    public Point32View(int x, int y)
    {
        X = x;
        Y = y;
    }
}

