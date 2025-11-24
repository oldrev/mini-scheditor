using System.Collections.Generic;
using MiniScheditor.Core;

namespace MiniScheditor.Models;

public abstract class SymbolPrimitive { }

public class SymbolLine : SymbolPrimitive
{
    public Point32 Start { get; set; }
    public Point32 End { get; set; }

    public SymbolLine(int x1, int y1, int x2, int y2)
    {
        Start = new Point32(x1, y1);
        End = new Point32(x2, y2);
    }
}

public class SymbolRect : SymbolPrimitive
{
    public Rect32 Rect { get; set; }
    public bool IsFilled { get; set; }

    public SymbolRect(int x, int y, int w, int h, bool filled = false)
    {
        Rect = new Rect32(x, y, w, h);
        IsFilled = filled;
    }
}

public class SymbolCircle : SymbolPrimitive
{
    public Point32 Center { get; set; }
    public int Radius { get; set; }
    public bool IsFilled { get; set; }

    public SymbolCircle(int x, int y, int radius, bool filled = false)
    {
        Center = new Point32(x, y);
        Radius = radius;
        IsFilled = filled;
    }
}

public class SymbolPin
{
    public string Name { get; set; }
    public Point32 Position { get; set; }

    public SymbolPin(string name, int x, int y)
    {
        Name = name;
        Position = new Point32(x, y);
    }
}

public class Symbol
{
    public string Name { get; set; }
    public List<SymbolPrimitive> Primitives { get; } = new List<SymbolPrimitive>();
    public List<SymbolPin> Pins { get; } = new List<SymbolPin>();
    public Rect32 Bounds { get; set; }

    public Symbol(string name)
    {
        Name = name;
    }
}

public static class SymbolLibrary
{
    public static Symbol Resistor { get; }
    public static Symbol Capacitor { get; }
    public static Symbol TransistorNPN { get; }
    public static Symbol Diode { get; }

    static SymbolLibrary()
    {
        // 1 unit = 100,000 nm = 0.1mm
        // Resistor: 10mm long (100 units), 2mm wide (20 units)
        Resistor = new Symbol("Resistor");
        Resistor.Bounds = new Rect32(0, -10, 100, 20);
        // Leads
        Resistor.Primitives.Add(new SymbolLine(0, 0, 20, 0));
        Resistor.Primitives.Add(new SymbolLine(80, 0, 100, 0));
        // ZigZag
        Resistor.Primitives.Add(new SymbolLine(20, 0, 25, -10));
        Resistor.Primitives.Add(new SymbolLine(25, -10, 35, 10));
        Resistor.Primitives.Add(new SymbolLine(35, 10, 45, -10));
        Resistor.Primitives.Add(new SymbolLine(45, -10, 55, 10));
        Resistor.Primitives.Add(new SymbolLine(55, 10, 65, -10));
        Resistor.Primitives.Add(new SymbolLine(65, -10, 75, 10));
        Resistor.Primitives.Add(new SymbolLine(75, 10, 80, 0));

        Resistor.Pins.Add(new SymbolPin("1", 0, 0));
        Resistor.Pins.Add(new SymbolPin("2", 100, 0));


        // Capacitor
        Capacitor = new Symbol("Capacitor");
        Capacitor.Bounds = new Rect32(0, -15, 40, 30);
        // Leads
        Capacitor.Primitives.Add(new SymbolLine(0, 0, 15, 0));
        Capacitor.Primitives.Add(new SymbolLine(25, 0, 40, 0));
        // Plates
        Capacitor.Primitives.Add(new SymbolLine(15, -15, 15, 15));
        Capacitor.Primitives.Add(new SymbolLine(25, -15, 25, 15));

        Capacitor.Pins.Add(new SymbolPin("1", 0, 0));
        Capacitor.Pins.Add(new SymbolPin("2", 40, 0));

        // Diode
        Diode = new Symbol("Diode");
        Diode.Bounds = new Rect32(0, -15, 40, 30);
        // Leads
        Diode.Primitives.Add(new SymbolLine(0, 0, 10, 0));
        Diode.Primitives.Add(new SymbolLine(30, 0, 40, 0));
        // Triangle
        Diode.Primitives.Add(new SymbolLine(10, -10, 10, 10));
        Diode.Primitives.Add(new SymbolLine(10, -10, 30, 0));
        Diode.Primitives.Add(new SymbolLine(10, 10, 30, 0));
        // Bar
        Diode.Primitives.Add(new SymbolLine(30, -10, 30, 10));

        Diode.Pins.Add(new SymbolPin("A", 0, 0));
        Diode.Pins.Add(new SymbolPin("K", 40, 0));

        // Transistor NPN
        TransistorNPN = new Symbol("NPN");
        TransistorNPN.Bounds = new Rect32(0, -20, 40, 40);
        // Base
        TransistorNPN.Primitives.Add(new SymbolLine(0, 0, 10, 0)); // Base lead
        TransistorNPN.Primitives.Add(new SymbolLine(10, -15, 10, 15)); // Base bar
                                                                       // Collector
        TransistorNPN.Primitives.Add(new SymbolLine(10, -10, 25, -20)); // Diagonal
        TransistorNPN.Primitives.Add(new SymbolLine(25, -20, 25, -30)); // Lead up (simplified)
                                                                        // Emitter
        TransistorNPN.Primitives.Add(new SymbolLine(10, 10, 25, 20)); // Diagonal
        TransistorNPN.Primitives.Add(new SymbolLine(25, 20, 25, 30)); // Lead down
                                                                      // Arrow
        TransistorNPN.Primitives.Add(new SymbolLine(18, 15, 25, 20));
        TransistorNPN.Primitives.Add(new SymbolLine(20, 12, 25, 20));

        TransistorNPN.Pins.Add(new SymbolPin("B", 0, 0));
        TransistorNPN.Pins.Add(new SymbolPin("C", 25, -30));
        TransistorNPN.Pins.Add(new SymbolPin("E", 25, 30));
    }
}