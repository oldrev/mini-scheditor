using Avalonia;
using Avalonia.Media;
using MiniScheditor.Models;

namespace MiniScheditor.Graphics;

public class ComponentItem : SchematicItem
{
    private static readonly DashStyle _selectionDashStyle = new DashStyle(new double[] { 2, 2 }, 0);

    public ComponentItem(Component component) : base(component)
    {
    }

    public override void Draw(DrawingContext context, double scale)
    {
        var comp = (Component)Model;
        var pen = new Pen(Brushes.Black, 1 * scale);
        const int SYMBOL_SCALE = 100000;

        // Draw Symbol Primitives
        foreach (var prim in comp.Symbol.Primitives)
        {
            if (prim is SymbolLine line)
            {
                context.DrawLine(pen,
                    new Point(comp.Position.X + line.Start.X * SYMBOL_SCALE, comp.Position.Y + line.Start.Y * SYMBOL_SCALE),
                    new Point(comp.Position.X + line.End.X * SYMBOL_SCALE, comp.Position.Y + line.End.Y * SYMBOL_SCALE));
            }
            else if (prim is SymbolRect rect)
            {
                var brush = rect.IsFilled ? Brushes.Black : null;
                context.DrawRectangle(brush, pen, new Rect(
                    comp.Position.X + rect.Rect.X * SYMBOL_SCALE,
                    comp.Position.Y + rect.Rect.Y * SYMBOL_SCALE,
                    rect.Rect.Width * SYMBOL_SCALE,
                    rect.Rect.Height * SYMBOL_SCALE));
            }
            else if (prim is SymbolCircle circle)
            {
                var brush = circle.IsFilled ? Brushes.Black : null;
                double r = circle.Radius * SYMBOL_SCALE;
                context.DrawEllipse(brush, pen, new Point(
                    comp.Position.X + circle.Center.X * SYMBOL_SCALE,
                    comp.Position.Y + circle.Center.Y * SYMBOL_SCALE), r, r);
            }
        }

        // Draw Pins
        foreach (var pin in comp.Pins)
        {
            context.DrawEllipse(Brushes.Red, null, new Point(pin.Position.X, pin.Position.Y), 3 * scale, 3 * scale);
        }

        // Draw Selection Border
        if (comp.IsSelected)
        {
            var selectionPen = new Pen(Brushes.Blue, 1 * scale, _selectionDashStyle);
            context.DrawRectangle(null, selectionPen, new Rect(
                comp.Bounds.X - 5 * scale,
                comp.Bounds.Y - 5 * scale,
                comp.Bounds.Width + 10 * scale,
                comp.Bounds.Height + 10 * scale));
        }
    }
}
