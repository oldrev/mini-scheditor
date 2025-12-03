using System;
using System.Collections.Generic;
using MiniScheditor.Core;

namespace MiniScheditor.Models;

public abstract class SchematicObject : ISpatialObject
{
    public Rect32 Bounds { get; protected set; }
    public bool IsSelected { get; set; }
    public int LayerId { get; set; }
}

public class Pin
{
    public string Name { get; set; }
    public Point32 RelativePosition { get; set; }
    public Component Parent { get; }

    public Point32 Position => Parent.Position + RelativePosition;

    public Pin(Component parent, string name, int relX, int relY)
    {
        Parent = parent;
        Name = name;
        RelativePosition = new Point32(relX, relY);
    }
}

public class Component : SchematicObject
{
    public string Name { get; set; } = "Component";
    public Symbol Symbol { get; set; }
    public Point32 Position { get; set; }
    public List<Pin> Pins { get; } = new List<Pin>();

    private const int SYMBOL_SCALE = 100000;

    public Component(Symbol symbol, int x, int y)
    {
        Symbol = symbol;
        Position = new Point32(x, y);
        UpdateBounds();

        // Initialize Pins from Symbol
        foreach (var symPin in symbol.Pins)
        {
            Pins.Add(new Pin(this, symPin.Name, symPin.Position.X * SYMBOL_SCALE, symPin.Position.Y * SYMBOL_SCALE));
        }
    }

    public void MoveTo(int x, int y)
    {
        Position = new Point32(x, y);
        UpdateBounds();
    }

    private void UpdateBounds()
    {
        // Component bounds are Symbol bounds translated by Position
        Bounds = new Rect32(
            Position.X + Symbol.Bounds.X * SYMBOL_SCALE,
            Position.Y + Symbol.Bounds.Y * SYMBOL_SCALE,
            (int)(Symbol.Bounds.Width * SYMBOL_SCALE),
            (int)(Symbol.Bounds.Height * SYMBOL_SCALE)
        );
    }
}

public class Wire : SchematicObject
{
    public Point32 Start { get; set; }
    public Point32 End { get; set; }
    public int Thickness { get; set; } = 100000; // 0.1mm

    public Wire(Point32 start, Point32 end)
    {
        Start = start;
        End = end;
        UpdateBounds();
    }

    public void UpdateBounds()
    {
        int x = System.Math.Min(Start.X, End.X);
        int y = System.Math.Min(Start.Y, End.Y);
        int w = System.Math.Abs(Start.X - End.X);
        int h = System.Math.Abs(Start.Y - End.Y);
        // Add some padding for thickness
        int padding = Thickness / 2;
        Bounds = new Rect32(x - padding, y - padding, w + Thickness, h + Thickness);
    }
}

public class Junction : SchematicObject
{
    public Point32 Position { get; set; }

    public Junction(Point32 position)
    {
        Position = position;
        // Size of the dot: 0.5mm = 500,000 nm
        int size = 500000;
        Bounds = new Rect32(Position.X - size / 2, Position.Y - size / 2, size, size);
    }
}

public class Layer
{
    public int Id { get; set; }
    public string Name { get; set; } = "Layer";
    public bool IsVisible { get; set; } = true;
    public List<SchematicObject> Objects { get; } = new List<SchematicObject>();
}

public class SchematicDocument
{
    public event Action<SchematicObject>? ObjectAdded;
    public event Action<SchematicObject>? ObjectRemoved;

    public ISpatialIndex<SchematicObject> SpatialIndex { get; }
    public List<Layer> Layers { get; }
    public Rect32 WorldBounds { get; }

    // 2 meters bounds
    public SchematicDocument()
    {
        // +/- 2 meters (approx)
        // int.MaxValue is ~2.14 billion.
        // We use FromLTRB to avoid int overflow in Width calculation if we used (x,y,w,h)
        WorldBounds = Rect32.FromLTRB(-2000000000, -2000000000, 2000000000, 2000000000);
        SpatialIndex = new QuadTree<SchematicObject>(0, WorldBounds);
        Layers = new List<Layer>
            {
                new Layer { Id = 0, Name = "Default" }
            };
    }

    public void AddObject(SchematicObject obj)
    {
        Layers[0].Objects.Add(obj);
        SpatialIndex.Insert(obj);
        ObjectAdded?.Invoke(obj);
    }

    public void RemoveObject(SchematicObject obj)
    {
        foreach (var layer in Layers)
        {
            if (layer.Objects.Remove(obj)) break;
        }
        SpatialIndex.Remove(obj);
        ObjectRemoved?.Invoke(obj);
    }
}