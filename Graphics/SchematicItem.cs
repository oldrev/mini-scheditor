using Avalonia;
using Avalonia.Media;
using MiniScheditor.Core;
using MiniScheditor.Models;

namespace MiniScheditor.Graphics;

public abstract class SchematicItem : ISpatialObject
{
    public SchematicObject Model { get; }

    protected SchematicItem(SchematicObject model)
    {
        Model = model;
    }

    public Rect32 Bounds => Model.Bounds;

    public abstract void Draw(DrawingContext context, double scale);
}
