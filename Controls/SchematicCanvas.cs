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

    public static readonly StyledProperty<double> RotationProperty =
        AvaloniaProperty.Register<SchematicCanvas, double>(nameof(Rotation));

    public double Rotation
    {
        get => GetValue(RotationProperty);
        set => SetValue(RotationProperty, value);
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

    private Matrix GetWorldToScreenMatrix()
    {
        return Matrix.CreateScale(_scale, _scale) *
               Matrix.CreateRotation(Math.PI * Rotation / 180.0) *
               Matrix.CreateTranslation(_offsetX, _offsetY);
    }

    private Matrix GetScreenToWorldMatrix()
    {
        if (_scale == 0) return Matrix.Identity;
        return GetWorldToScreenMatrix().Invert();
    }

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
        AffectsRender<SchematicCanvas>(RotationProperty);
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

        var transform = GetWorldToScreenMatrix();
        var inverseTransform = GetScreenToWorldMatrix();

        // Calculate visible world rect (AABB of the rotated view)
        var p1 = new Point(0, 0).Transform(inverseTransform);
        var p2 = new Point(bounds.Width, 0).Transform(inverseTransform);
        var p3 = new Point(bounds.Width, bounds.Height).Transform(inverseTransform);
        var p4 = new Point(0, bounds.Height).Transform(inverseTransform);

        double minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        double maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        double minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        double maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

        // Intersect with WorldBounds
        double iLeft = Math.Max(minX, Document.WorldBounds.Left);
        double iTop = Math.Max(minY, Document.WorldBounds.Top);
        double iRight = Math.Min(maxX, Document.WorldBounds.Right);
        double iBottom = Math.Min(maxY, Document.WorldBounds.Bottom);

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

        using (context.PushTransform(transform))
        {
            // Draw White Background for WorldBounds
            context.FillRectangle(Brushes.White, new Rect(
                Document.WorldBounds.X, Document.WorldBounds.Y,
                Document.WorldBounds.Width, Document.WorldBounds.Height));

            // Draw Grid
            if (visibleWorldRect.Width > 0 && visibleWorldRect.Height > 0)
            {
                DrawGrid(context, visibleWorldRect);
            }

            if (ShowPageBorder)
            {
                using (context.PushTransform(transform))
                {
                    var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 120)), 1.0 / _scale,
                        new DashStyle(new double[] { 6, 4 }, 0));
                    context.DrawRectangle(null, borderPen, new Rect(0, 0, A4_WIDTH, A4_HEIGHT));
                }
            }

            // Draw Origin Cross
            var originPen = new Pen(Brushes.Red, 1.0 / _scale);
            double crossSize = 50.0 / _scale;
            context.DrawLine(originPen, new Point(-crossSize, 0), new Point(crossSize, 0));
            context.DrawLine(originPen, new Point(0, -crossSize), new Point(0, crossSize));
        }

        // Query QuadTree
        _visibleObjects.Clear();
        if (visibleWorldRect.Width > 0 && visibleWorldRect.Height > 0)
        {
            Document.SpatialIndex.Query(visibleWorldRect, _visibleObjects);
        }

        // Draw Objects
        double penScale = 1.0 / _scale;
        using (context.PushTransform(transform))
        {
            var span = CollectionsMarshal.AsSpan(_visibleObjects);
            foreach (var obj in span)
            {
                DrawObject(context, obj, penScale);
            }
        }

        // Draw Selection Rect (Screen Space)
        if (_isSelecting)
        {
            context.DrawRectangle(new Pen(Brushes.Blue, 1), _selectionRect);
            context.FillRectangle(new SolidColorBrush(Colors.Blue, 0.2), _selectionRect);
        }

        // Draw Wire being placed (World Space)
        if (_isWiring)
        {
            using (context.PushTransform(transform))
            {
                context.DrawLine(new Pen(Brushes.Green, 2 * penScale),
                    new Point(_wireStart.X, _wireStart.Y),
                    new Point(_wireEnd.X, _wireEnd.Y));
            }
        }

        // Draw Component Preview (World Space)
        if (ComponentPlacementTool != null)
        {
            var symbol = ComponentPlacementTool.Symbol;
            // Snap to grid
            // MouseWorldPosition is already updated in OnPointerMoved using inverse matrix
            int snapX = (int)Math.Round(MouseWorldPosition.X / (double)BASE_GRID_SIZE) * BASE_GRID_SIZE;
            int snapY = (int)Math.Round(MouseWorldPosition.Y / (double)BASE_GRID_SIZE) * BASE_GRID_SIZE;
            var previewPos = new Point32(snapX, snapY);

            using (context.PushTransform(transform))
            {
                var previewPen = new Pen(Brushes.Gray, 1 * penScale);
                foreach (var prim in symbol.Primitives)
                {
                    if (prim is SymbolLine line)
                    {
                        context.DrawLine(previewPen,
                            new Point(previewPos.X + line.Start.X * SYMBOL_SCALE, previewPos.Y + line.Start.Y * SYMBOL_SCALE),
                            new Point(previewPos.X + line.End.X * SYMBOL_SCALE, previewPos.Y + line.End.Y * SYMBOL_SCALE));
                    }
                    else if (prim is SymbolRect rect)
                    {
                        var brush = rect.IsFilled ? Brushes.Gray : null;
                        context.DrawRectangle(brush, previewPen, new Rect(
                            previewPos.X + rect.Rect.X * SYMBOL_SCALE,
                            previewPos.Y + rect.Rect.Y * SYMBOL_SCALE,
                            rect.Rect.Width * SYMBOL_SCALE,
                            rect.Rect.Height * SYMBOL_SCALE));
                    }
                    else if (prim is SymbolCircle circle)
                    {
                        var brush = circle.IsFilled ? Brushes.Gray : null;
                        double r = circle.Radius * SYMBOL_SCALE;
                        context.DrawEllipse(brush, previewPen, new Point(
                            previewPos.X + circle.Center.X * SYMBOL_SCALE,
                            previewPos.Y + circle.Center.Y * SYMBOL_SCALE), r, r);
                    }
                }

                // Draw preview pins
                foreach (var pin in symbol.Pins)
                {
                    context.DrawEllipse(Brushes.Gray, null, new Point(
                        previewPos.X + pin.Position.X * SYMBOL_SCALE,
                        previewPos.Y + pin.Position.Y * SYMBOL_SCALE), 3 * penScale, 3 * penScale);
                }
            }
        }
    }

    private void DrawGrid(DrawingContext context, Rect32 visibleRect)
    {
        if (GridDisplayMode == GridDisplayMode.None)
        {
            return;
        }

        // Calculate grid size based on screen pixels
        // We need to know how big the grid is on screen.
        // Since we are drawing in world space, we check: gridWorldSize * _scale
        double minPixelSpacing = 20.0;
        long currentGridSize = BASE_GRID_SIZE;

        while (currentGridSize * _scale < minPixelSpacing)
        {
            currentGridSize *= 2;
        }

        long startX = (long)Math.Floor(visibleRect.X / (double)currentGridSize) * currentGridSize;
        long startY = (long)Math.Floor(visibleRect.Y / (double)currentGridSize) * currentGridSize;

        double penThickness = 1.0 / _scale;
        var pen = new Pen(Brushes.LightGray, penThickness);

        if (GridDisplayMode == GridDisplayMode.Lines)
        {
            for (long x = startX; x <= visibleRect.Right; x += currentGridSize)
            {
                context.DrawLine(pen, new Point(x, visibleRect.Y), new Point(x, visibleRect.Bottom));
            }

            for (long y = startY; y <= visibleRect.Bottom; y += currentGridSize)
            {
                context.DrawLine(pen, new Point(visibleRect.X, y), new Point(visibleRect.Right, y));
            }
        }
        else
        {
            // Markers
            double crossHalf = 3.0 / _scale;
            double dotRadius = 1.5 / _scale;
            var brush = Brushes.LightGray;
            bool drawCross = GridDisplayMode == GridDisplayMode.Crosses;

            for (long x = startX; x <= visibleRect.Right; x += currentGridSize)
            {
                for (long y = startY; y <= visibleRect.Bottom; y += currentGridSize)
                {
                    if (drawCross)
                    {
                        context.DrawLine(pen, new Point(x - crossHalf, y), new Point(x + crossHalf, y));
                        context.DrawLine(pen, new Point(x, y - crossHalf), new Point(x, y + crossHalf));
                    }
                    else
                    {
                        context.DrawEllipse(brush, null, new Point(x, y), dotRadius, dotRadius);
                    }
                }
            }
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

    private void DrawObject(DrawingContext context, SchematicObject obj, double penScale)
    {
        // Default pen for unselected objects
        var pen = new Pen(Brushes.Black, 1 * penScale);

        if (obj is Component comp)
        {
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
                context.DrawEllipse(Brushes.Red, null, new Point(pin.Position.X, pin.Position.Y), 3 * penScale, 3 * penScale);
            }

            // Draw Selection Border
            if (obj.IsSelected)
            {
                var selectionPen = new Pen(Brushes.Blue, 1 * penScale, new DashStyle(new double[] { 4, 2 }, 0));
                context.DrawRectangle(null, selectionPen, new Rect(
                    comp.Bounds.X - 5 * penScale,
                    comp.Bounds.Y - 5 * penScale,
                    comp.Bounds.Width + 10 * penScale,
                    comp.Bounds.Height + 10 * penScale));
            }
        }
        else if (obj is Wire wire)
        {
            // Wire.Thickness is in world units (e.g. 100,000 nm)
            double thickness = wire.Thickness;

            // Ensure minimum 1 pixel width on screen
            // 1 screen pixel = penScale world units
            if (thickness < penScale) thickness = penScale;

            if (obj.IsSelected)
            {
                // 2px border (4px total extra width)
                // 4px screen width = 4 * penScale world units
                var borderPen = new Pen(Brushes.Blue, thickness + 4 * penScale);
                context.DrawLine(borderPen, new Point(wire.Start.X, wire.Start.Y), new Point(wire.End.X, wire.End.Y));

                // Wire color Xor (Green 008000 xor White FFFFFF = FF7FFF)
                var xorColor = Color.FromRgb(255, 127, 255);
                var wirePen = new Pen(new SolidColorBrush(xorColor), thickness);
                context.DrawLine(wirePen, new Point(wire.Start.X, wire.Start.Y), new Point(wire.End.X, wire.End.Y));
            }
            else
            {
                var wirePen = new Pen(Brushes.Green, thickness);
                context.DrawLine(wirePen, new Point(wire.Start.X, wire.Start.Y), new Point(wire.End.X, wire.End.Y));
            }
        }
        else if (obj is Junction junction)
        {
            double r = 3 * penScale;
            var brush = obj.IsSelected ? Brushes.Blue : Brushes.Green;
            context.DrawEllipse(brush, null, new Point(junction.Position.X, junction.Position.Y), r, r);
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
        var inverseTransform = GetScreenToWorldMatrix();
        var worldPoint = screenPos.Transform(inverseTransform);
        double worldX = worldPoint.X;
        double worldY = worldPoint.Y;

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

        var inverseTransform = GetScreenToWorldMatrix();
        var worldPoint = position.Transform(inverseTransform);
        double worldX = worldPoint.X;
        double worldY = worldPoint.Y;
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