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

public partial class SchematicCanvas
{
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var point = e.GetCurrentPoint(this).Position;

        // Zoom center logic
        // We want to keep the world point under the mouse fixed.
        // World = Screen * InvM
        // NewScreen = World * NewM
        // We want NewScreen == Screen (at mouse pos)
        // So we adjust Offset in NewM.

        double zoomFactor = e.Delta.Y > 0 ? 1.1 : 0.9;
        double oldScale = _scale;
        double newScale = _scale * zoomFactor;

        // Clamp scale
        if (newScale < 1e-7) newScale = 1e-7;
        if (newScale > 0.1) newScale = 0.1;

        // 1. Get World Point under mouse
        var inverseTransform = GetScreenToWorldMatrix();
        var worldPoint = point.Transform(inverseTransform);

        // 2. Calculate what the screen point would be with new scale but old offset
        // We construct a matrix with new scale, same rotation, same offset
        // Actually, we want to find the NEW offset.
        // Screen = World * S * R * T
        // Screen = (World * S * R) + Offset
        // Offset = Screen - (World * S * R)

        var rotationMatrix = Matrix.CreateRotation(Math.PI * Rotation / 180.0);
        var scaleMatrix = Matrix.CreateScale(newScale, newScale);

        // Calculate (World * S * R)
        var transformedWorld = worldPoint.Transform(scaleMatrix * rotationMatrix);

        // 3. Update Offset
        _offsetX = point.X - transformedWorld.X;
        _offsetY = point.Y - transformedWorld.Y;
        _scale = newScale;

        InvalidateVisual();
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

            var inverseTransform = GetScreenToWorldMatrix();
            var worldPoint = point.Position.Transform(inverseTransform);
            double worldX = worldPoint.X;
            double worldY = worldPoint.Y;
            var clickPoint = new Point32((int)worldX, (int)worldY);

            var placementTool = ActiveTool as ComponentPlacementTool;
            if (placementTool != null)
            {
                // Snap to grid
                int snapX = (int)Math.Round(clickPoint.X / (double)BASE_GRID_SIZE) * BASE_GRID_SIZE;
                int snapY = (int)Math.Round(clickPoint.Y / (double)BASE_GRID_SIZE) * BASE_GRID_SIZE;

                var newComp = new Component(placementTool.Symbol, snapX, snapY);
                Document.AddObject(newComp);
                InvalidateVisual();
            }
            else if (ActiveTool is WireTool)
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
            else if (ActiveTool is SelectTool)
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

                    _transientSelections.Clear();
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
                    // Since selection rect is axis aligned in screen space,
                    // it might be rotated in world space.
                    // We need to find all objects inside the rotated rectangle.
                    // For now, we can approximate by transforming the 4 corners and taking the bounding box.
                    // Or better, use a polygon query if QuadTree supports it.
                    // Assuming QuadTree only supports AABB, we take the AABB of the transformed rect.

                    var inverseTransform = GetScreenToWorldMatrix();
                    var p1 = _selectionRect.TopLeft.Transform(inverseTransform);
                    var p2 = _selectionRect.TopRight.Transform(inverseTransform);
                    var p3 = _selectionRect.BottomRight.Transform(inverseTransform);
                    var p4 = _selectionRect.BottomLeft.Transform(inverseTransform);

                    double minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
                    double maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
                    double minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
                    double maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

                    var worldRect = new Rect32(
                        (int)Math.Floor(minX),
                        (int)Math.Floor(minY),
                        (int)Math.Ceiling(maxX - minX),
                        (int)Math.Ceiling(maxY - minY)
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

                    _transientSelections.Clear();
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
        var inverseTransform = GetScreenToWorldMatrix();
        var worldPoint = point.Position.Transform(inverseTransform);
        MouseWorldPosition = worldPoint;

        // Set cursor based on active tool
        if (ActiveTool is ComponentPlacementTool || ActiveTool is WireTool)
        {
            Cursor = new Cursor(StandardCursorType.Cross);
        }
        else
        {
            Cursor = new Cursor(StandardCursorType.Arrow);
        }

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
            // We need delta in world coordinates.
            // DeltaWorld = DeltaScreen * InvM (without translation)
            // Actually, simpler:
            // CurrentWorld = point.Position * InvM
            // StartWorld = _dragStart * InvM
            // DeltaWorld = CurrentWorld - StartWorld

            var startWorld = _dragStart.Transform(inverseTransform);
            var currentWorld = point.Position.Transform(inverseTransform);

            var deltaWorldX = (int)(currentWorld.X - startWorld.X);
            var deltaWorldY = (int)(currentWorld.Y - startWorld.Y);

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
        else
        {
            UpdateHoverSelection(point.Position);
        }

        // Invalidate for component placement preview
        if (ActiveTool is ComponentPlacementTool)
        {
            InvalidateVisual();
        }

        UpdateHoverSelection(point.Position);
    }


}