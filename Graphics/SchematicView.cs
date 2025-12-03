using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MiniScheditor.Core;
using MiniScheditor.Models;

namespace MiniScheditor.Graphics;

public class SchematicView : Control
{
    public static readonly StyledProperty<SchematicScene?> SceneProperty =
        AvaloniaProperty.Register<SchematicView, SchematicScene?>(nameof(Scene));

    public SchematicScene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    public static readonly StyledProperty<GridDisplayMode> GridDisplayModeProperty =
        AvaloniaProperty.Register<SchematicView, GridDisplayMode>(nameof(GridDisplayMode), GridDisplayMode.Lines);

    public GridDisplayMode GridDisplayMode
    {
        get => GetValue(GridDisplayModeProperty);
        set => SetValue(GridDisplayModeProperty, value);
    }

    public static readonly StyledProperty<bool> ShowPageBorderProperty =
        AvaloniaProperty.Register<SchematicView, bool>(nameof(ShowPageBorder));

    public bool ShowPageBorder
    {
        get => GetValue(ShowPageBorderProperty);
        set => SetValue(ShowPageBorderProperty, value);
    }

    public static readonly StyledProperty<double> RotationProperty =
        AvaloniaProperty.Register<SchematicView, double>(nameof(Rotation));

    public double Rotation
    {
        get => GetValue(RotationProperty);
        set => SetValue(RotationProperty, value);
    }

    protected double _scale = 0.00001; // 1 pixel = 100,000 nm (0.1mm) -> 10 pixels = 1mm. Start zoomed out.
    protected double _offsetX = 0;
    protected double _offsetY = 0;

    // 1.00mm grid base size (standard 0.05" spacing)
    protected const int BASE_GRID_SIZE = 1000000;
    protected const int A4_WIDTH = 297000000;
    protected const int A4_HEIGHT = 210000000;

    private static readonly Pen _gridPen = new Pen(Brushes.LightGray, 1);
    private static readonly SolidColorBrush _pageBorderBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120));
    private static readonly DashStyle _pageBorderDashStyle = new DashStyle(new double[] { 6, 4 }, 0);

    static SchematicView()
    {
        AffectsRender<SchematicView>(SceneProperty);
        AffectsRender<SchematicView>(GridDisplayModeProperty);
        AffectsRender<SchematicView>(ShowPageBorderProperty);
        AffectsRender<SchematicView>(RotationProperty);
    }

    public SchematicView()
    {
        ClipToBounds = true;
    }

    public Matrix GetWorldToScreenMatrix()
    {
        return Matrix.CreateScale(_scale, _scale) *
               Matrix.CreateRotation(Math.PI * Rotation / 180.0) *
               Matrix.CreateTranslation(_offsetX, _offsetY);
    }

    public Matrix GetScreenToWorldMatrix()
    {
        if (_scale == 0) return Matrix.Identity;
        return GetWorldToScreenMatrix().Invert();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        // Fill background with Gray (invalid area)
        context.FillRectangle(Brushes.LightGray, bounds);

        if (Scene == null) return;

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

        // Intersect with WorldBounds (SceneRect)
        double iLeft = Math.Max(minX, Scene.SceneRect.Left);
        double iTop = Math.Max(minY, Scene.SceneRect.Top);
        double iRight = Math.Min(maxX, Scene.SceneRect.Right);
        double iBottom = Math.Min(maxY, Scene.SceneRect.Bottom);

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
                Scene.SceneRect.X, Scene.SceneRect.Y,
                Scene.SceneRect.Width, Scene.SceneRect.Height));

            // Draw Grid
            if (visibleWorldRect.Width > 0 && visibleWorldRect.Height > 0)
            {
                DrawGrid(context, visibleWorldRect);
            }

            if (ShowPageBorder)
            {
                var borderPen = new Pen(_pageBorderBrush, 1.0 / _scale, _pageBorderDashStyle);
                context.DrawRectangle(null, borderPen, new Rect(0, 0, A4_WIDTH, A4_HEIGHT));
            }

            // Draw Origin Cross
            var originPenX = new Pen(Brushes.Red, 1.0 / _scale);
            var originPenY = new Pen(Brushes.Green, 1.0 / _scale);
            double crossSize = 50.0 / _scale;
            context.DrawLine(originPenX, new Point(-crossSize, 0), new Point(crossSize, 0));
            context.DrawLine(originPenY, new Point(0, -crossSize), new Point(0, crossSize));
        }

        // Draw Scene Items
        if (visibleWorldRect.Width > 0 && visibleWorldRect.Height > 0)
        {
            using (context.PushTransform(transform))
            {
                Scene.Draw(context, visibleWorldRect, 1.0 / _scale);
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
}
