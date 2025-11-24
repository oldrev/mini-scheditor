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

public partial class SchematicCanvas : Control
{
    public static readonly StyledProperty<SchematicDocument?> DocumentProperty =
        AvaloniaProperty.Register<SchematicCanvas, SchematicDocument?>(nameof(Document));

    public SchematicDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public static readonly StyledProperty<EditTool> ActiveToolProperty =
        AvaloniaProperty.Register<SchematicCanvas, EditTool>(nameof(ActiveTool), SelectTool.Instance);

    public EditTool ActiveTool
    {
        get => GetValue(ActiveToolProperty);
        set => SetValue(ActiveToolProperty, value ?? SelectTool.Instance);
    }

    public static readonly StyledProperty<GridDisplayMode> GridDisplayModeProperty =
        AvaloniaProperty.Register<SchematicCanvas, GridDisplayMode>(nameof(GridDisplayMode), GridDisplayMode.Lines);

    public GridDisplayMode GridDisplayMode
    {
        get => GetValue(GridDisplayModeProperty);
        set => SetValue(GridDisplayModeProperty, value);
    }

    public static readonly StyledProperty<bool> ShowPageBorderProperty =
        AvaloniaProperty.Register<SchematicCanvas, bool>(nameof(ShowPageBorder));

    public bool ShowPageBorder
    {
        get => GetValue(ShowPageBorderProperty);
        set => SetValue(ShowPageBorderProperty, value);
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

    private ComponentPlacementTool? ComponentPlacementTool => ActiveTool as ComponentPlacementTool;

    // Reusable buffer for query results to avoid allocations per frame
    private readonly List<SchematicObject> _visibleObjects = new List<SchematicObject>(1000);
    private readonly HashSet<SchematicObject> _transientSelections = new HashSet<SchematicObject>();
    private readonly List<SchematicObject> _hitTestResults = new List<SchematicObject>();

    // 1.27mm grid base size (standard 0.05" spacing)
    private const int BASE_GRID_SIZE = 1270000;
    private const int A4_WIDTH = 297000000;
    private const int A4_HEIGHT = 210000000;

    static SchematicCanvas()
    {
        AffectsRender<SchematicCanvas>(DocumentProperty);
        AffectsRender<SchematicCanvas>(ActiveToolProperty);
        AffectsRender<SchematicCanvas>(GridDisplayModeProperty);
        AffectsRender<SchematicCanvas>(ShowPageBorderProperty);
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

            if (ActiveTool.ResetToSelectOnEscape && ActiveTool is not SelectTool)
            {
                ActiveTool = SelectTool.Instance;
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

                _transientSelections.Clear();

                if (toRemove.Count > 0)
                {
                    InvalidateVisual();
                }
            }
        }
    }


    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActiveToolProperty)
        {
            if (change.NewValue is EditTool newTool && newTool is not WireTool)
            {
                _isWiring = false;
            }
            Focus();
            InvalidateVisual();
        }
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

        if (ShowPageBorder)
        {
            double rectWidth = A4_WIDTH * _scale;
            double rectHeight = A4_HEIGHT * _scale;
            double x = _offsetX - rectWidth / 2;
            double y = _offsetY - rectHeight / 2;
            var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 1,
                new DashStyle(new double[] { 6, 4 }, 0));
            context.DrawRectangle(null, borderPen, new Rect(x, y, rectWidth, rectHeight));
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

        // Draw Component Preview
        if (ComponentPlacementTool != null)
        {
            var symbol = ComponentPlacementTool.Symbol;
            // Snap to grid
            int snapX = (int)Math.Round(MouseWorldPosition.X / (double)BASE_GRID_SIZE) * BASE_GRID_SIZE;
            int snapY = (int)Math.Round(MouseWorldPosition.Y / (double)BASE_GRID_SIZE) * BASE_GRID_SIZE;
            var previewPos = new Point32(snapX, snapY);

            // Draw with gray color
            var previewPen = new Pen(Brushes.Gray, 1);
            foreach (var prim in symbol.Primitives)
            {
                if (prim is SymbolLine line)
                {
                    double x1 = (previewPos.X + line.Start.X * SYMBOL_SCALE) * _scale + _offsetX;
                    double y1 = (previewPos.Y + line.Start.Y * SYMBOL_SCALE) * _scale + _offsetY;
                    double x2 = (previewPos.X + line.End.X * SYMBOL_SCALE) * _scale + _offsetX;
                    double y2 = (previewPos.Y + line.End.Y * SYMBOL_SCALE) * _scale + _offsetY;
                    context.DrawLine(previewPen, new Point(x1, y1), new Point(x2, y2));
                }
                else if (prim is SymbolRect rect)
                {
                    double x = (previewPos.X + rect.Rect.X * SYMBOL_SCALE) * _scale + _offsetX;
                    double y = (previewPos.Y + rect.Rect.Y * SYMBOL_SCALE) * _scale + _offsetY;
                    double w = rect.Rect.Width * SYMBOL_SCALE * _scale;
                    double h = rect.Rect.Height * SYMBOL_SCALE * _scale;
                    var brush = rect.IsFilled ? Brushes.Gray : null;
                    context.DrawRectangle(brush, previewPen, new Rect(x, y, w, h));
                }
                else if (prim is SymbolCircle circle)
                {
                    double cx = (previewPos.X + circle.Center.X * SYMBOL_SCALE) * _scale + _offsetX;
                    double cy = (previewPos.Y + circle.Center.Y * SYMBOL_SCALE) * _scale + _offsetY;
                    double r = circle.Radius * SYMBOL_SCALE * _scale;
                    var brush = circle.IsFilled ? Brushes.Gray : null;
                    context.DrawEllipse(brush, previewPen, new Point(cx, cy), r, r);
                }
            }

            // Draw preview pins
            foreach (var pin in symbol.Pins)
            {
                double px = (previewPos.X + pin.Position.X * SYMBOL_SCALE) * _scale + _offsetX;
                double py = (previewPos.Y + pin.Position.Y * SYMBOL_SCALE) * _scale + _offsetY;
                context.DrawEllipse(Brushes.Gray, null, new Point(px, py), 3, 3);
            }
        }
    }

    private void DrawGrid(DrawingContext context, Rect32 visibleRect)
    {
        if (GridDisplayMode == GridDisplayMode.None)
        {
            return;
        }

        if (GridDisplayMode == GridDisplayMode.Lines)
        {
            DrawGridLines(context, visibleRect);
        }
        else
        {
            DrawGridMarkers(context, visibleRect, GridDisplayMode == GridDisplayMode.Crosses);
        }
    }

    private void DrawGridLines(DrawingContext context, Rect32 visibleRect)
    {
        double minPixelSpacing = 20.0;
        long currentGridSize = BASE_GRID_SIZE;

        while (currentGridSize * _scale < minPixelSpacing)
        {
            currentGridSize *= 2;
        }

        long startX = (long)Math.Floor(visibleRect.X / (double)currentGridSize) * currentGridSize;
        long startY = (long)Math.Floor(visibleRect.Y / (double)currentGridSize) * currentGridSize;

        var pen = new Pen(Brushes.LightGray, 1);

        for (long x = startX; x <= visibleRect.Right; x += currentGridSize)
        {
            double screenX = x * _scale + _offsetX;
            context.DrawLine(pen, new Point(screenX, 0), new Point(screenX, Bounds.Height));
        }

        for (long y = startY; y <= visibleRect.Bottom; y += currentGridSize)
        {
            double screenY = y * _scale + _offsetY;
            context.DrawLine(pen, new Point(0, screenY), new Point(Bounds.Width, screenY));
        }
    }

    private void DrawGridMarkers(DrawingContext context, Rect32 visibleRect, bool drawCross)
    {
        // Use same adaptive spacing logic as grid lines so density matches
        double minPixelSpacing = 20.0;
        long currentGridSize = BASE_GRID_SIZE;
        while (currentGridSize * _scale < minPixelSpacing)
        {
            currentGridSize *= 2;
        }

        long startX = (long)Math.Floor(visibleRect.X / (double)currentGridSize) * currentGridSize;
        long startY = (long)Math.Floor(visibleRect.Y / (double)currentGridSize) * currentGridSize;

        var pen = new Pen(Brushes.LightGray, 1);
        var brush = Brushes.LightGray;

        // �̶����سߴ磬�������ű仯 (�������� 1px һ��)
        double crossHalf = 3.0;   // ���߳��ȣ����� 6px ʮ��
        double dotRadius = 1.5;   // ֱ�� 3px �ĵ�

        for (long x = startX; x <= visibleRect.Right; x += currentGridSize)
        {
            double screenX = x * _scale + _offsetX;
            for (long y = startY; y <= visibleRect.Bottom; y += currentGridSize)
            {
                double screenY = y * _scale + _offsetY;

                if (drawCross)
                {
                    context.DrawLine(pen, new Point(screenX - crossHalf, screenY), new Point(screenX + crossHalf, screenY));
                    context.DrawLine(pen, new Point(screenX, screenY - crossHalf), new Point(screenX, screenY + crossHalf));
                }
                else
                {
                    context.DrawEllipse(brush, null, new Point(screenX, screenY), dotRadius, dotRadius);
                }
            }
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

            if (obj.IsSelected)
            {
                // 2px border (4px total extra width)
                var borderPen = new Pen(Brushes.Blue, thickness + 4);
                context.DrawLine(borderPen, new Point(x1, y1), new Point(x2, y2));

                // Wire color Xor (Green 008000 xor White FFFFFF = FF7FFF)
                var xorColor = Color.FromRgb(255, 127, 255);
                var wirePen = new Pen(new SolidColorBrush(xorColor), thickness);
                context.DrawLine(wirePen, new Point(x1, y1), new Point(x2, y2));
            }
            else
            {
                var wirePen = new Pen(Brushes.Green, thickness);
                context.DrawLine(wirePen, new Point(x1, y1), new Point(x2, y2));
            }
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
            foreach (var j in junctionSearch) if (j is Junction) exists = true;

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

    private void UpdateHoverSelection(Point position)
    {
        if (Document == null) return;
        if (ActiveTool is not SelectTool) return;

        // Check for explicit selection
        bool hasExplicitSelection = false;
        foreach (var layer in Document.Layers)
        {
            foreach (var obj in layer.Objects)
            {
                if (obj.IsSelected && !_transientSelections.Contains(obj))
                {
                    hasExplicitSelection = true;
                    break;
                }
            }
            if (hasExplicitSelection) break;
        }

        if (hasExplicitSelection)
        {
            ClearTransientSelections();
            return;
        }

        double worldX = (position.X - _offsetX) / _scale;
        double worldY = (position.Y - _offsetY) / _scale;
        var clickPoint = new Point32((int)worldX, (int)worldY);

        var hitRect = new Rect32(clickPoint.X - 100000, clickPoint.Y - 100000, 200000, 200000);
        _hitTestResults.Clear();
        Document.SpatialIndex.Query(hitRect, _hitTestResults);

        SchematicObject? hitObject = null;
        if (_hitTestResults.Count > 0)
        {
            hitObject = _hitTestResults[0];
        }

        if (hitObject != null && _transientSelections.Contains(hitObject))
        {
            return;
        }

        ClearTransientSelections();

        if (hitObject != null && !hitObject.IsSelected)
        {
            hitObject.IsSelected = true;
            _transientSelections.Add(hitObject);
            InvalidateVisual();
        }
    }

    private void ClearTransientSelections()
    {
        if (_transientSelections.Count > 0)
        {
            foreach (var obj in _transientSelections)
            {
                obj.IsSelected = false;
            }
            _transientSelections.Clear();
            InvalidateVisual();
        }
    }
}