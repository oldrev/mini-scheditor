# MiniScheditor — Avalonia Schematic Editor Demo

[中文](README.zh.md)

![Screen shot](Assets/screenshot.png)

MiniScheditor is a compact .NET + Avalonia demo application that explores algorithms and techniques for building an interactive electronic schematic (circuit) editor. It focuses on a simple, high-quality drawing canvas, efficient spatial indexing, snapping and wiring behaviors, and an extendable symbol library. This project is intentionally small and educational — a playground for UI/UX and geometry techniques relevant to schematic editors.


## Key features

- Interactive canvas with panning, zooming and snapping
- Orthogonal wire placement supporting click-click wiring and pin snapping
- Component placement with symbol previews and grid snapping
- Visual selection and drag for components
- Efficient spatial queries using a QuadTree to render and hit-test visible objects
- Simple symbol library (resistor, capacitor, diode, NPN transistor) as an example

## Where to look in the source

- `Controls/SchematicCanvas.cs` — the heart of the UI: rendering, input handling, selection, panning, zoom/center logic, wire placement, hit-testing and more.
- `Models/SchematicElements.cs` — models for the domain objects: Component, Wire, Junction, Document, Layer
- `Models/Symbols.cs` — a small built-in symbol library (Resistor, Capacitor, Diode, NPN) and primitives used to render symbols
- `Models/EditTool.cs` — tools (Select, Wire, Component placement) and the pattern used to add new tools
- `Core/QuadTree.cs` — spatial indexing used by the canvas for efficient queries and rendering
- `Views/MainWindow.axaml` + `Program.cs` — app entry + window wiring (Avalonia)

## Coordinate system / units and grid

MiniScheditor uses an integer-based world coordinate system where 1 unit is 100,000 nm = 0.1 mm. This allows very fine-grained geometry while using integer arithmetic for object bounds (avoid some floating inaccuracies). The base grid size uses 1.27 mm spacing (1270000 world units) which is common in PCB/schematic tooling. Symbol coordinates are authored in smaller logical units (scaled when rendering).

## User interactions / Controls

While running the app (desktop):

- Left-click: select objects or place parts (depending on active tool)
- Click-click wiring: use the Wire tool to click start and click end to place orthogonal wire segments. The system snaps to component pins and nearby wire segments.
- Middle mouse button + drag: pan the canvas
- Mouse wheel: zoom in/out (mouse-cursor-centered zoom)
- ESC: cancel the current tool or operation (wiring, selection box, drag)
- Delete: remove selected objects

Tools are implemented as `EditTool` subclasses; the canvas responds to the currently active tool.

## How it works (high-level)

- Rendering: `SchematicCanvas.Render` draws the grid, page border, origin cross, visible objects and previews. Visible objects are obtained by querying the QuadTree with the currently visible world rectangle.
- Spatial indexing: a `QuadTree<T>` holds object bounds for efficient visibility/hit-testing. This keeps rendering and hit-tests fast even when the document grows large.
- Hit testing/snapping: mouse positions are transformed from screen→world using the canvas scale/offset. The canvas attempts multiple snap strategies: pins → wires → grid. This gives a practical and pleasant editing experience.
- Wiring and junction logic: when placing wires, the editor detects when a new junction is necessary (e.g., wire intersections or ≥3 endpoints) and can add `Junction` objects.

## Limitations / TODOs (observed in code) 

- QuadTree currently does not handle component moves efficiently — the code comments note that components are moved without updating the QuadTree (naive approach). In a real editor you should remove and re-insert moved objects, or use a spatial index that supports updates.
- There is limited persistence (no built-in save/load) — this project focuses on interactive editing primitives.
- Selection and overlap logic are intentionally simple for the demo; real-world edge cases (z-ordering, multi-layer rules, complex selection heuristics) would need additional work.

## Build & run (cross-platform)

Requirements:
- .NET SDK 8/9 (project targets net9.0 in this demo)

To build and run from a terminal in the repository root:

```pwsh
dotnet restore
dotnet build -c Debug
dotnet run --project MiniScheditor.csproj
```

On first run you should see a desktop window with a blank schematic canvas. Use the UI controls (tool selection, panning, zooming) to interact with it.

## Extending the demo — ideas to explore

- Add persistence (save/load to JSON / XML / custom format) so documents can be re-opened.
- Implement undo/redo for edit operations.
- Improve QuadTree to support removing/reinserting on move or use a dynamic R-tree for better updates.
- Add advanced snapping (45-degree, multi-grid, component rotation, anchor points).
- Add more symbol shapes and a symbol editor for authoring new parts.
- Add electrical netlist tracking, connection validation and simulation hooks.

## License & contribution

This code is provided as a demo for educational/video purposes.

Public Domain (no restrictions).

Author: Li Wei (email: oldrev@gmail.com)