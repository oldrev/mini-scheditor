using Avalonia;
using Avalonia.Media;
using MiniScheditor.Models;

namespace MiniScheditor.Graphics;

public class WireItem : SchematicItem
{
    public WireItem(Wire wire) : base(wire)
    {
    }

    public override void Draw(DrawingContext context, double scale)
    {
        var wire = (Wire)Model;
        // Wire.Thickness is in world units (e.g. 100,000 nm)
        double thickness = wire.Thickness;

        // Ensure minimum 1 pixel width on screen
        // 1 screen pixel = scale world units
        if (thickness < scale) thickness = scale;

        if (wire.IsSelected)
        {
            // 2px border (4px total extra width)
            // 4px screen width = 4 * scale world units
            var borderPen = new Pen(Brushes.Blue, thickness + 4 * scale);
            context.DrawLine(borderPen, new Point(wire.Start.X, wire.Start.Y), new Point(wire.End.X, wire.End.Y));

            // Wire color Xor (Green 008000 xor White FFFFFF = FF7FFF)
            // Note: The original code used a hardcoded color.
            var xorColor = Color.FromRgb(255, 127, 255);
            var wirePen = new Pen(new SolidColorBrush(xorColor), thickness);
            context.DrawLine(wirePen, new Point(wire.Start.X, wire.Start.Y), new Point(wire.End.X, wire.End.Y));
        }
        else
        {
            var wirePen = new Pen(Brushes.Blue, thickness);
            context.DrawLine(wirePen, new Point(wire.Start.X, wire.Start.Y), new Point(wire.End.X, wire.End.Y));
        }
    }
}
