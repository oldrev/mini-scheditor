using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MiniScheditor.Core;
using MiniScheditor.Models;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MiniScheditor.Controls;

public class SchematicCanvas : Control
{
    public static readonly StyledProperty<SchematicDocument?> DocumentProperty =
        AvaloniaProperty.Register<SchematicCanvas, SchematicDocument?>(nameof(Document));

    public SchematicDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public static readonly StyledProperty<EditorTool> ActiveToolProperty =
        AvaloniaProperty.Register<SchematicCanvas, EditorTool>(nameof(ActiveTool), EditorTool.Select);

    public EditorTool ActiveTool
    {
        get => GetValue(ActiveToolProperty);
        set => SetValue(ActiveToolProperty, value);
    }

    public static readonly StyledProperty<Symbol?> ComponentToPlaceProperty =
        AvaloniaProperty.Register<SchematicCanvas, Symbol?>(nameof(ComponentToPlace));

    public Symbol? ComponentToPlace
    {
        get => GetValue(ComponentToPlaceProperty);
        set => SetValue(ComponentToPlaceProperty, value);
    }

    public static readonly StyledProperty<Point> MouseWorldPositionProperty =
        AvaloniaProperty.Register<SchematicCanvas, Point>(nameof(MouseWorldPosition));

    public Point MouseWorldPosition
    {
        get => GetValue(MouseWorldPositionProperty);
        set => SetValue(MouseWorldPositionProperty, value);
    }

    private double _scale = 0.00001; // 1 pixel = 100,000 nm (0.1mm) -> 10 pixels = 1mm. Start zoomed out.
    private double _offsetX = 0;
    private double _offsetY = 0;
    private bool _isPanning;
    private Point _lastMousePos;

    private bool _isSelecting;
    private Point _selectionStart;
    private Rect _selectionRect;

    private bool _isDragging;
    private SchematicObject? _dragObject;
    private Point _dragStart;
    private Point32 _dragObjectStartPos;

    private bool _isWiring;
    private Point32 _wireStart;
    private Point32 _wireEnd;

    // Reusable buffer for query results to avoid allocations per frame
    private readonly List<SchematicObject> _visibleObjects = new List<SchematicObject>(1000);

    // 2.5mm grid base size
    private const int BASE_GRID_SIZE = 2500000;

    static SchematicCanvas()
    {
        AffectsRender<SchematicCanvas>(DocumentProperty);
        AffectsRender<SchematicCanvas>(ActiveToolProperty);
    }

    public SchematicCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            if (_isWiring)
            {
                _isWiring = false;
                InvalidateVisual();
            }
            else if (_isDragging)
            {
                _isDragging = false;
                _dragObject = null;
                InvalidateVisual();
            }
            else if (_isSelecting)
            {
                _isSelecting = false;
                InvalidateVisual();
            }
        }
        else if (e.Key == Key.Delete)
        {
            if (Document != null)
            {
                var toRemove = new List<SchematicObject>();
                foreach (var layer in Document.Layers)
                {
                    foreach (var obj in layer.Objects)
                    {
                        if (obj.IsSelected)
                        {
                            toRemove.Add(obj);
                        }
                    }
                }

                foreach (var obj in toRemove)
                {
                    Document.RemoveObject(obj);
                }

                if (toRemove.Count > 0)
                {
                    InvalidateVisual();
                }
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus(); // Ensure we get focus on click
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _lastMousePos = point.Position;
            e.Pointer.Capture(this);
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            if (_isWiring)
            {
                _isWiring = false;
                e.Pointer.Capture(null);
                InvalidateVisual();
            }
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            if (Document == null) return;

            double worldX = (point.Position.X - _offsetX) / _scale;
            double worldY = (point.Position.Y - _offsetY) / _scale;
            var clickPoint = new Point32((int)worldX, (int)worldY);

            if (ActiveTool == EditorTool.PlaceComponent && ComponentToPlace != null)
            {
                // Snap to grid
                int snapX = (int)Math.Round(clickPoint.X / (double)BASE_GRID_SIZE) * BASE_GRID_SIZE;
                int snapY = (int)Math.Round(clickPoint.Y / (double)BASE_GRID_SIZE) * BASE_GRID_SIZE;

                var newComp = new Component(ComponentToPlace, snapX, snapY);
                Document.AddObject(newComp);
                InvalidateVisual();
            }
            else if (ActiveTool == EditorTool.Wire)
            {
                var snapPoint = GetSnapPoint(point.Position);

                if (!_isWiring)
                {
                    _isWiring = true;
                    _wireStart = snapPoint;
                    _wireEnd = _wireStart;
                    e.Pointer.Capture(this);
                }
                else
                {
                    // Constrain snapPoint if CTRL is not pressed
                    bool isCtrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                    if (!isCtrlPressed)
                    {
                        int dx = Math.Abs(snapPoint.X - _wireStart.X);
                        int dy = Math.Abs(snapPoint.Y - _wireStart.Y);
                        if (dx > dy) snapPoint = new Point32(snapPoint.X, _wireStart.Y);
                        else snapPoint = new Point32(_wireStart.X, snapPoint.Y);
                    }
                    _wireEnd = snapPoint;

                    // Finish current segment
                    if (_wireStart.X != _wireEnd.X || _wireStart.Y != _wireEnd.Y)
                    {
                        var wire = new Wire(_wireStart, _wireEnd);
                        Document.AddObject(wire);
                        
                        // Check for junctions at both ends
                        CheckAndAddJunction(_wireStart);
                        CheckAndAddJunction(_wireEnd);

                        // Check if we should stop (snapped to a pin)
                        if (IsPointOnPin(snapPoint))
                        {
                            _isWiring = false;
                            e.Pointer.Capture(null);
                        }
                        else
                        {
                            // Continue wiring
                            _wireStart = _wireEnd;
                        }
                        InvalidateVisual();
                    }
                }
            }
            else if (ActiveTool == EditorTool.Select)
            {
                // Check for hit
                // Small hit rect
                var hitRect = new Rect32(clickPoint.X - 100000, clickPoint.Y - 100000, 200000, 200000);
                var hits = new List<SchematicObject>();
                Document.SpatialIndex.Query(hitRect, hits);

                if (hits.Count > 0)
                {
                    // Start Dragging
                    _isDragging = true;
                    _dragObject = hits[0]; // Pick first
                    _dragStart = point.Position;

                    if (_dragObject is Component comp)
                    {
                        _dragObjectStartPos = comp.Position;
                    }

                    // Select it
                    foreach (var layer in Document.Layers)
                        foreach (var obj in layer.Objects) obj.IsSelected = false;

                    _dragObject.IsSelected = true;
                    e.Pointer.Capture(this);
                    InvalidateVisual();
                    return;
                }

                _isSelecting = true;
                _selectionStart = point.Position;
                _selectionRect = new Rect(_selectionStart, new Size(0, 0));
                e.Pointer.Capture(this);
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Middle)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
        }
        else if (e.InitialPressMouseButton == MouseButton.Left)
        {
            if (_isWiring)
            {
                // Do nothing on release for Click-Click mode
            }
            else if (_isDragging)
            {
                _isDragging = false;
                _dragObject = null;
                e.Pointer.Capture(null);
            }
            else if (_isSelecting)
            {
                _isSelecting = false;
                e.Pointer.Capture(null);

                // Perform selection
                if (Document != null)
                {
                    // Convert selection rect to world
                    double left = (_selectionRect.X - _offsetX) / _scale;
                    double top = (_selectionRect.Y - _offsetY) / _scale;
                    double w = _selectionRect.Width / _scale;
                    double h = _selectionRect.Height / _scale;

                    var worldRect = new Rect32(
                        (int)Math.Floor(left),
                        (int)Math.Floor(top),
                        (int)Math.Ceiling(w),
                        (int)Math.Ceiling(h)
                    );

                    var hits = new List<SchematicObject>();
                    Document.SpatialIndex.Query(worldRect, hits);

                    foreach (var layer in Document.Layers)
                    {
                        foreach (var obj in layer.Objects)
                        {
                            obj.IsSelected = false;
                        }
                    }

                    foreach (var hit in hits)
                    {
                        hit.IsSelected = true;
                    }

                    InvalidateVisual();
                }

                _selectionRect = default;
                InvalidateVisual();
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetCurrentPoint(this);

        // Update MouseWorldPosition
        double wX = (point.Position.X - _offsetX) / _scale;
        double wY = (point.Position.Y - _offsetY) / _scale;
        MouseWorldPosition = new Point(wX, wY);

        if (_isPanning)
        {
            var delta = point.Position - _lastMousePos;
            _offsetX += delta.X;
            _offsetY += delta.Y;
            _lastMousePos = point.Position;
            InvalidateVisual();
        }
        else if (_isWiring)
        {
            var snapPoint = GetSnapPoint(point.Position);

            // Constrain snapPoint if CTRL is not pressed
            bool isCtrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (!isCtrlPressed)
            {
                int dx = Math.Abs(snapPoint.X - _wireStart.X);
                int dy = Math.Abs(snapPoint.Y - _wireStart.Y);
                if (dx > dy) snapPoint = new Point32(snapPoint.X, _wireStart.Y);
                else snapPoint = new Point32(_wireStart.X, snapPoint.Y);
            }

            _wireEnd = snapPoint;
            InvalidateVisual();
        }
        else if (_isDragging && _dragObject is Component comp)
        {
            var deltaScreen = point.Position - _dragStart;
            var deltaWorldX = (int)(deltaScreen.X / _scale);
            var deltaWorldY = (int)(deltaScreen.Y / _scale);

            var newX = _dragObjectStartPos.X + deltaWorldX;
            var newY = _dragObjectStartPos.Y + deltaWorldY;

            // Snap to Grid
            // Use base grid size for snapping regardless of zoom level for consistency
            newX = (int)Math.Round(newX / (double)BASE_GRID_SIZE) * BASE_GRID_SIZE;
            newY = (int)Math.Round(newY / (double)BASE_GRID_SIZE) * BASE_GRID_SIZE;

            comp.MoveTo(newX, newY);

            // Re-insert into QuadTree (Naive approach: Remove and Add)
            // For MVP, we might just update bounds and rebuild tree occasionally or handle dynamic updates.
            // QuadTree doesn't support Move easily without Remove/Insert.
            // Let's just invalidate visual for now, but QuadTree will be stale!
            // TODO: Fix QuadTree update.

            InvalidateVisual();
        }
        else if (_isSelecting)
        {
            var cur = point.Position;
            var x = Math.Min(_selectionStart.X, cur.X);
            var y = Math.Min(_selectionStart.Y, cur.Y);
            var w = Math.Abs(_selectionStart.X - cur.X);
            var h = Math.Abs(_selectionStart.Y - cur.Y);
            _selectionRect = new Rect(x, y, w, h);
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var point = e.GetCurrentPoint(this).Position;

        // Zoom center logic
        // World = (Screen - Offset) / Scale
        // NewScale = Scale * Factor
        // NewOffset = Screen - World * NewScale

        double zoomFactor = e.Delta.Y > 0 ? 1.1 : 0.9;
        double oldScale = _scale;
        double newScale = _scale * zoomFactor;

        // Clamp scale
        if (newScale < 1e-7) newScale = 1e-7;
        if (newScale > 0.1) newScale = 0.1;

        double worldX = (point.X - _offsetX) / oldScale;
        double worldY = (point.Y - _offsetY) / oldScale;

        _offsetX = point.X - (worldX * newScale);
        _offsetY = point.Y - (worldY * newScale);
        _scale = newScale;

        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        // Fill background with Gray (invalid area)
        context.FillRectangle(Brushes.LightGray, bounds);

        if (Document == null) return;

        // Calculate World Bounds in Screen Coordinates
        double wbX = Document.WorldBounds.X * _scale + _offsetX;
        double wbY = Document.WorldBounds.Y * _scale + _offsetY;
        double wbW = Document.WorldBounds.Width * _scale;
        double wbH = Document.WorldBounds.Height * _scale;
        var worldScreenRect = new Rect(wbX, wbY, wbW, wbH);

        // Fill valid world area with White
        context.FillRectangle(Brushes.White, worldScreenRect);

        // Calculate visible world rect
        // Screen (0,0) -> World
        // Screen (W,H) -> World

        double left = (0 - _offsetX) / _scale;
        double top = (0 - _offsetY) / _scale;
        double right = (bounds.Width - _offsetX) / _scale;
        double bottom = (bounds.Height - _offsetY) / _scale;

        // Calculate intersection with WorldBounds in double domain to avoid int overflow
        // when zoomed out significantly (viewport larger than int.MaxValue)
        double iLeft = Math.Max(left, Document.WorldBounds.Left);
        double iTop = Math.Max(top, Document.WorldBounds.Top);
        double iRight = Math.Min(right, Document.WorldBounds.Right);
        double iBottom = Math.Min(bottom, Document.WorldBounds.Bottom);

        Rect32 visibleWorldRect;

        if (iRight > iLeft && iBottom > iTop)
        {
            visibleWorldRect = Rect32.FromLTRB(
                (int)Math.Floor(iLeft),
                (int)Math.Floor(iTop),
                (int)Math.Ceiling(iRight),
                (int)Math.Ceiling(iBottom)
            );
        }
        else
        {
            visibleWorldRect = new Rect32(0, 0, 0, 0);
        }

        // Draw Grid only inside valid world area
        using (context.PushClip(worldScreenRect))
        {
            if (visibleWorldRect.Width > 0 && visibleWorldRect.Height > 0)
            {
                DrawGrid(context, visibleWorldRect);
            }
        }

        // Draw Origin Cross
        var originPen = new Pen(Brushes.Red, 1);
        double crossSize = 50;
        context.DrawLine(originPen, new Point(_offsetX - crossSize, _offsetY), new Point(_offsetX + crossSize, _offsetY));
        context.DrawLine(originPen, new Point(_offsetX, _offsetY - crossSize), new Point(_offsetX, _offsetY + crossSize));

        // Query QuadTree
        _visibleObjects.Clear();
        // We can query using the clamped rect because objects are only inside WorldBounds
        if (visibleWorldRect.Width > 0 && visibleWorldRect.Height > 0)
        {
            Document.SpatialIndex.Query(visibleWorldRect, _visibleObjects);
        }

        // Draw Objects using Span for iteration
        var span = CollectionsMarshal.AsSpan(_visibleObjects);
        foreach (var obj in span)
        {
            DrawObject(context, obj);
        }

        // Draw Selection Rect
        if (_isSelecting)
        {
            context.DrawRectangle(new Pen(Brushes.Blue, 1), _selectionRect);
            context.FillRectangle(new SolidColorBrush(Colors.Blue, 0.2), _selectionRect);
        }

        // Draw Wire being placed
        if (_isWiring)
        {
            double x1 = _wireStart.X * _scale + _offsetX;
            double y1 = _wireStart.Y * _scale + _offsetY;
            double x2 = _wireEnd.X * _scale + _offsetX;
            double y2 = _wireEnd.Y * _scale + _offsetY;
            context.DrawLine(new Pen(Brushes.Green, 2), new Point(x1, y1), new Point(x2, y2));
        }
    }

    private void DrawGrid(DrawingContext context, Rect32 visibleRect)
    {
        // Adaptive Grid Logic
        // We want grid lines to be roughly 20-100 pixels apart on screen.
        // Base grid is 2.5mm (2,500,000 nm)

        double minPixelSpacing = 20.0;
        long currentGridSize = BASE_GRID_SIZE;

        // If grid is too small (dense), multiply by 2 until it's sparse enough
        while (currentGridSize * _scale < minPixelSpacing)
        {
            currentGridSize *= 2;
        }

        // Calculate grid start
        long startX = (long)Math.Floor(visibleRect.X / (double)currentGridSize) * currentGridSize;
        long startY = (long)Math.Floor(visibleRect.Y / (double)currentGridSize) * currentGridSize;

        var pen = new Pen(Brushes.LightGray, 1);

        // Vertical lines
        for (long x = startX; x <= visibleRect.Right; x += currentGridSize)
        {
            double screenX = x * _scale + _offsetX;
            context.DrawLine(pen, new Point(screenX, 0), new Point(screenX, Bounds.Height));
        }

        // Horizontal lines
        for (long y = startY; y <= visibleRect.Bottom; y += currentGridSize)
        {
            double screenY = y * _scale + _offsetY;
            context.DrawLine(pen, new Point(0, screenY), new Point(Bounds.Width, screenY));
        }
    }

    private const int SYMBOL_SCALE = 100000;

    private void DrawObject(DrawingContext context, SchematicObject obj)
    {
        // Default pen for unselected objects
        var pen = new Pen(Brushes.Black, 1);

        if (obj is Component comp)
        {
            // Draw Symbol Primitives
            foreach (var prim in comp.Symbol.Primitives)
            {
                if (prim is SymbolLine line)
                {
                    double x1 = (comp.Position.X + line.Start.X * SYMBOL_SCALE) * _scale + _offsetX;
                    double y1 = (comp.Position.Y + line.Start.Y * SYMBOL_SCALE) * _scale + _offsetY;
                    double x2 = (comp.Position.X + line.End.X * SYMBOL_SCALE) * _scale + _offsetX;
                    double y2 = (comp.Position.Y + line.End.Y * SYMBOL_SCALE) * _scale + _offsetY;
                    context.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
                }
                else if (prim is SymbolRect rect)
                {
                    double x = (comp.Position.X + rect.Rect.X * SYMBOL_SCALE) * _scale + _offsetX;
                    double y = (comp.Position.Y + rect.Rect.Y * SYMBOL_SCALE) * _scale + _offsetY;
                    double w = rect.Rect.Width * SYMBOL_SCALE * _scale;
                    double h = rect.Rect.Height * SYMBOL_SCALE * _scale;
                    var brush = rect.IsFilled ? Brushes.Black : null;
                    context.DrawRectangle(brush, pen, new Rect(x, y, w, h));
                }
                else if (prim is SymbolCircle circle)
                {
                    double cx = (comp.Position.X + circle.Center.X * SYMBOL_SCALE) * _scale + _offsetX;
                    double cy = (comp.Position.Y + circle.Center.Y * SYMBOL_SCALE) * _scale + _offsetY;
                    double r = circle.Radius * SYMBOL_SCALE * _scale;
                    var brush = circle.IsFilled ? Brushes.Black : null;
                    context.DrawEllipse(brush, pen, new Point(cx, cy), r, r);
                }
            }

            // Draw Pins
            foreach (var pin in comp.Pins)
            {
                double px = pin.Position.X * _scale + _offsetX;
                double py = pin.Position.Y * _scale + _offsetY;
                context.DrawEllipse(Brushes.Red, null, new Point(px, py), 3, 3);
            }

            // Draw Selection Border
            if (obj.IsSelected)
            {
                double x = comp.Bounds.X * _scale + _offsetX;
                double y = comp.Bounds.Y * _scale + _offsetY;
                double w = comp.Bounds.Width * _scale;
                double h = comp.Bounds.Height * _scale;

                var selectionPen = new Pen(Brushes.Blue, 1, new DashStyle(new double[] { 4, 2 }, 0));
                context.DrawRectangle(null, selectionPen, new Rect(x - 5, y - 5, w + 10, h + 10));
            }
        }
        else if (obj is Wire wire)
        {
            double x1 = wire.Start.X * _scale + _offsetX;
            double y1 = wire.Start.Y * _scale + _offsetY;
            double x2 = wire.End.X * _scale + _offsetX;
            double y2 = wire.End.Y * _scale + _offsetY;

            double thickness = wire.Thickness * _scale;
            if (thickness < 1) thickness = 1;

            var wirePen = obj.IsSelected ? new Pen(Brushes.Blue, thickness) : new Pen(Brushes.Green, thickness);
            context.DrawLine(wirePen, new Point(x1, y1), new Point(x2, y2));
        }
        else if (obj is Junction junction)
        {
            double x = junction.Position.X * _scale + _offsetX;
            double y = junction.Position.Y * _scale + _offsetY;
            double r = junction.Bounds.Width * _scale / 2;
            
            var brush = obj.IsSelected ? Brushes.Blue : Brushes.Green;
            context.DrawEllipse(brush, null, new Point(x, y), r, r);
        }
    }

    private bool IsPointOnPin(Point32 point)
    {
        if (Document == null) return false;

        // Check if any pin is exactly at this location
        // We can use the SpatialIndex to narrow down candidates
        var searchRect = new Rect32(point.X - 100, point.Y - 100, 200, 200);
        var hits = new List<SchematicObject>();
        Document.SpatialIndex.Query(searchRect, hits);

        foreach (var obj in hits)
        {
            if (obj is Component comp)
            {
                foreach (var pin in comp.Pins)
                {
                    if (pin.Position.X == point.X && pin.Position.Y == point.Y)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private Point32 GetSnapPoint(Point screenPos)
    {
        if (Document == null) return new Point32(0, 0);

        double worldX = (screenPos.X - _offsetX) / _scale;
        double worldY = (screenPos.Y - _offsetY) / _scale;

        // 1. Try to snap to Pin
        // Search radius: 15 pixels in world coordinates
        double searchRadius = 15.0 / _scale;
        var searchRect = new Rect32(
            (int)(worldX - searchRadius),
            (int)(worldY - searchRadius),
            (int)(searchRadius * 2),
            (int)(searchRadius * 2)
        );

        var hits = new List<SchematicObject>();
        Document.SpatialIndex.Query(searchRect, hits);

        Point32? bestPinPos = null;
        double minDistSq = searchRadius * searchRadius;

        foreach (var obj in hits)
        {
            if (obj is Component comp)
            {
                foreach (var pin in comp.Pins)
                {
                    double dx = pin.Position.X - worldX;
                    double dy = pin.Position.Y - worldY;
                    double distSq = dx * dx + dy * dy;

                    if (distSq < minDistSq)
                    {
                        minDistSq = distSq;
                        bestPinPos = pin.Position;
                    }
                }
            }
        }

        if (bestPinPos.HasValue)
        {
            return bestPinPos.Value;
        }

        // 2. Try to snap to Wire
        // Search radius: 10 pixels
        double wireSearchRadius = 10.0 / _scale;
        var wireSearchRect = new Rect32(
            (int)(worldX - wireSearchRadius),
            (int)(worldY - wireSearchRadius),
            (int)(wireSearchRadius * 2),
            (int)(wireSearchRadius * 2)
        );
        
        var wireHits = new List<SchematicObject>();
        Document.SpatialIndex.Query(wireSearchRect, wireHits);
        
        Point32? bestWirePoint = null;
        double minWireDistSq = wireSearchRadius * wireSearchRadius;
        
        var mousePos32 = new Point32((int)worldX, (int)worldY);

        foreach (var obj in wireHits)
        {
            if (obj is Wire wire)
            {
                // Find closest point on this wire segment to mousePos
                var closest = GetClosestPointOnSegment(mousePos32, wire.Start, wire.End);
                double distSq = DistanceSq(mousePos32, closest);
                
                if (distSq < minWireDistSq)
                {
                    minWireDistSq = distSq;
                    bestWirePoint = closest;
                }
            }
        }
        
        if (bestWirePoint.HasValue)
        {
             return bestWirePoint.Value;
        }

        // 3. Snap to Grid
        int snapX = (int)Math.Round(worldX / (double)BASE_GRID_SIZE) * BASE_GRID_SIZE;
        int snapY = (int)Math.Round(worldY / (double)BASE_GRID_SIZE) * BASE_GRID_SIZE;

        return new Point32(snapX, snapY);
    }

    private bool IsPointOnWire(Point32 point, Wire wire)
    {
        // Check if point is on the segment defined by wire.Start and wire.End
        // Assuming orthogonal wires for simplicity, but general case is also fine
        
        // Check bounding box first with small tolerance
        int tolerance = 10000; // Small tolerance
        if (point.X < Math.Min(wire.Start.X, wire.End.X) - tolerance ||
            point.X > Math.Max(wire.Start.X, wire.End.X) + tolerance ||
            point.Y < Math.Min(wire.Start.Y, wire.End.Y) - tolerance ||
            point.Y > Math.Max(wire.Start.Y, wire.End.Y) + tolerance)
        {
            return false;
        }

        // Check distance to line segment
        double dist = DistancePointToSegment(point, wire.Start, wire.End);
        return dist < tolerance;
    }

    private double DistancePointToSegment(Point32 p, Point32 v, Point32 w)
    {
        double l2 = DistanceSq(v, w);
        if (l2 == 0) return Math.Sqrt(DistanceSq(p, v));
        
        double t = ((p.X - v.X) * (w.X - v.X) + (p.Y - v.Y) * (w.Y - v.Y)) / l2;
        t = Math.Max(0, Math.Min(1, t));
        
        double px = v.X + t * (w.X - v.X);
        double py = v.Y + t * (w.Y - v.Y);
        
        return Math.Sqrt((p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py));
    }

    private double DistanceSq(Point32 p1, Point32 p2)
    {
        return (double)(p1.X - p2.X) * (p1.X - p2.X) + (double)(p1.Y - p2.Y) * (p1.Y - p2.Y);
    }

    private void CheckAndAddJunction(Point32 point)
    {
        if (Document == null) return;

        // Find wires at this point
        var searchRect = new Rect32(point.X - 10000, point.Y - 10000, 20000, 20000);
        var hits = new List<SchematicObject>();
        Document.SpatialIndex.Query(searchRect, hits);

        int endpointCount = 0;
        bool isMiddleOfAnyWire = false;

        foreach (var obj in hits)
        {
            if (obj is Wire wire)
            {
                if (IsPointOnWire(point, wire))
                {
                    bool isStart = (point.X == wire.Start.X && point.Y == wire.Start.Y);
                    bool isEnd = (point.X == wire.End.X && point.Y == wire.End.Y);

                    if (isStart || isEnd)
                    {
                        endpointCount++;
                    }
                    else
                    {
                        isMiddleOfAnyWire = true;
                    }
                }
            }
        }

        if (isMiddleOfAnyWire || endpointCount >= 3)
        {
            // Check if junction already exists
            bool exists = false;
            var junctionSearch = new List<SchematicObject>();
            Document.SpatialIndex.Query(new Rect32(point.X - 100, point.Y - 100, 200, 200), junctionSearch);
            foreach(var j in junctionSearch) if (j is Junction) exists = true;

            if (!exists)
            {
                Document.AddObject(new Junction(point));
            }
        }
    }

    private Point32 GetClosestPointOnSegment(Point32 p, Point32 v, Point32 w)
    {
        double l2 = DistanceSq(v, w);
        if (l2 == 0) return v;
        
        double t = ((p.X - v.X) * (w.X - v.X) + (p.Y - v.Y) * (w.Y - v.Y)) / l2;
        t = Math.Max(0, Math.Min(1, t));
        
        double px = v.X + t * (w.X - v.X);
        double py = v.Y + t * (w.Y - v.Y);
        
        return new Point32((int)Math.Round(px), (int)Math.Round(py));
    }
}