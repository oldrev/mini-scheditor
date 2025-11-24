using MiniScheditor.Core;
using MiniScheditor.Models;
using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia;

namespace MiniScheditor.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public SchematicDocument Document { get; }

    [ObservableProperty]
    private EditTool _activeTool = SelectTool.Instance;

    [ObservableProperty]
    private GridDisplayMode _gridMode = GridDisplayMode.Lines;

    [ObservableProperty]
    private bool _showPageBorder;

    public IEnumerable<GridDisplayMode> GridModes => Enum.GetValues<GridDisplayMode>();

    [ObservableProperty]
    private Point _mousePosition;

    [ObservableProperty]
    private string _statusText = "Ready";

    partial void OnMousePositionChanged(Point value)
    {
        StatusText = $"X: {value.X / 1000000.0:F3} mm, Y: {value.Y / 1000000.0:F3} mm";
    }

    public MainWindowViewModel()
    {
        Document = new SchematicDocument();

        // Add some sample components
        // Grid is 2,500,000 nm

        // Component 1 at (0,0)
        var c1 = new Component(SymbolLibrary.Resistor, 0, 0) { Name = "R1" };
        Document.AddObject(c1);

        // Component 2 at (20mm, 0) -> 20,000,000 nm
        var c2 = new Component(SymbolLibrary.Resistor, 20000000, 0) { Name = "R2" };
        Document.AddObject(c2);

        // Component 3 - Capacitor
        var c3 = new Component(SymbolLibrary.Capacitor, 0, 10000000) { Name = "C1" };
        Document.AddObject(c3);

        // Component 4 - Transistor
        var q1 = new Component(SymbolLibrary.TransistorNPN, 10000000, 10000000) { Name = "Q1" };
        Document.AddObject(q1);

        // Wire connecting them
        // From (10mm, 0) to (20mm, 0) - connecting R1 pin 2 to R2 pin 1
        // R1 pin 2 is at (0+10000000, 0) = (10000000, 0)
        // R2 pin 1 is at (20000000+0, 0) = (20000000, 0)
        var w1 = new Wire(
            new Point32(10000000, 0),
            new Point32(20000000, 0)
        );
        Document.AddObject(w1);
    }

    [RelayCommand]
    public void SetToolSelect()
    {
        ActiveTool = SelectTool.Instance;
    }

    [RelayCommand]
    public void SetToolWire()
    {
        ActiveTool = new WireTool();
    }

    [RelayCommand]
    public void SetToolComponent(string type)
    {
        EditTool tool = type switch
        {
            "Resistor" => new ComponentPlacementTool(SymbolLibrary.Resistor),
            "Capacitor" => new ComponentPlacementTool(SymbolLibrary.Capacitor),
            "NPN" => new ComponentPlacementTool(SymbolLibrary.TransistorNPN),
            "Diode" => new ComponentPlacementTool(SymbolLibrary.Diode),
            _ => SelectTool.Instance
        };
        ActiveTool = tool;
    }
}

