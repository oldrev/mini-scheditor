using Avalonia;
using Avalonia.Media;
using MiniScheditor.Models;

namespace MiniScheditor.Graphics;

public class JunctionItem : SchematicItem
{
    public JunctionItem(Junction junction) : base(junction)
    {
    }

    public override void Draw(DrawingContext context, double scale)
    {
        var junction = (Junction)Model;
        double r = 3 * scale;
        var brush = junction.IsSelected ? Brushes.Blue : Brushes.Green;
        context.DrawEllipse(brush, null, new Point(junction.Position.X, junction.Position.Y), r, r);
    }
}
