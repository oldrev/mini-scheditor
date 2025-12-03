using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MiniScheditor.Core;
using MiniScheditor.Models;
using MiniScheditor.Graphics;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MiniScheditor.Controls;

public partial class SchematicCanvas : SchematicView
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

    // GridDisplayMode, ShowPageBorder, Rotation are inherited from SchematicView

    public static readonly StyledProperty<Point> MouseWorldPositionProperty =
        AvaloniaProperty.Register<SchematicCanvas, Point>(nameof(MouseWorldPosition));

    public Point MouseWorldPosition
    {
        get => GetValue(MouseWorldPositionProperty);
        set => SetValue(MouseWorldPositionProperty, value);
    }

    // _scale, _offsetX, _offsetY are inherited from SchematicView
    private bool _isPanning;
    private Point _lastMousePos;

    // GetWorldToScreenMatrix and GetScreenToWorldMatrix are inherited from SchematicView

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
    private readonly HashSet<SchematicObject> _transientSelections = new HashSet<SchematicObject>();
    private readonly List<SchematicObject> _hitTestResults = new List<SchematicObject>();

    // Cached pens and styles for performance
    private static readonly Pen _selectionRectPen = new Pen(Brushes.Blue, 1);
    private static readonly DashStyle _componentSelectionDashStyle = new DashStyle(new double[] { 4, 2 }, 0);

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
        else if (change.Property == DocumentProperty)
        {
            if (change.OldValue is SchematicDocument oldDoc)
            {
                oldDoc.ObjectAdded -= OnObjectAdded;
                oldDoc.ObjectRemoved -= OnObjectRemoved;
            }

            if (change.NewValue is SchematicDocument doc)
            {
                var scene = new SchematicScene(doc.WorldBounds);
                scene.SyncFromDocument(doc);
                Scene = scene;

                doc.ObjectAdded += OnObjectAdded;
                doc.ObjectRemoved += OnObjectRemoved;
            }
            else
            {
                Scene = null;
            }
        }
    }

    private void OnObjectAdded(SchematicObject obj)
    {
        if (Scene != null)
        {
            var item = SchematicScene.CreateItem(obj);
            Scene.AddItem(item);
            InvalidateVisual();
        }
    }

    private void OnObjectRemoved(SchematicObject obj)
    {
        if (Scene != null)
        {
            var item = Scene.GetItemForModel(obj);
            if (item != null)
            {
                Scene.RemoveItem(item);
                InvalidateVisual();
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        // Draw Scene (Background, Grid, Items)
        base.Render(context);

        if (Document == null) return;

        var transform = GetWorldToScreenMatrix();
        double penScale = 1.0 / _scale;

        // Draw Selection Rect (Screen Space)
        if (_isSelecting)
        {
            context.DrawRectangle(_selectionRectPen, _selectionRect);
            context.FillRectangle(new SolidColorBrush(Colors.Blue, 0.2), _selectionRect);
        }

        // Draw Wire being placed (World Space)
        if (_isWiring)
        {
            using (context.PushTransform(transform))
            {
                context.DrawLine(new Pen(Brushes.Blue, 2 * penScale),
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




    // 1 unit in Symbol coordinates = 1 nm (World Unit)
    private const int SYMBOL_SCALE = 100000;

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
