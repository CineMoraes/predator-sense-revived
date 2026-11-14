using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PredatorSenseRevived.Controls;

/// <summary>
/// Background texture: overlapping dragon scales with 3D shading.
/// Each scale: soft shadow on the scale below, vertical gradient (lit from above), bright rim on the lower edge,
/// small specular highlight. Rows overlap like roof tiles: the upper row covers the top of the lower row.
/// One tile = 24 x 24 units (two staggered rows), scaled to <see cref="TilePx"/> pixels.
/// </summary>
public static class ScaleTexture
{
    public const double TilePx = 44;

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static Geometry Shape(double x, double y, double dy = 0, bool closed = true)
    {
        y += dy;
        string d = $"M{F(x)},{F(y)} Q{F(x)},{F(y + 14)} {F(x + 12)},{F(y + 17)} Q{F(x + 24)},{F(y + 14)} {F(x + 24)},{F(y)}" + (closed ? " Z" : "");
        var g = Geometry.Parse(d);
        g.Freeze();
        return g;
    }

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    private static readonly Brush ShadowFill = Frozen(new SolidColorBrush(Color.FromArgb(0xB0, 0x00, 0x00, 0x00)));
    private static readonly Brush BodyFill = Frozen(new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromRgb(0x2E, 0x42, 0x70), 0.0),
        new(Color.FromRgb(0x1B, 0x27, 0x46), 0.45),
        new(Color.FromRgb(0x0C, 0x13, 0x24), 1.0),
    }, new Point(0.5, 0), new Point(0.5, 1)));
    private static readonly Pen RimPen = new(Frozen(new LinearGradientBrush(Color.FromArgb(0x10, 0x6F, 0xA0, 0xFF), Color.FromArgb(0xC0, 0x6F, 0xA0, 0xFF), 90)), 0.7);
    private static readonly Pen RidgePen = new(Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0x9F, 0xC0, 0xFF))), 0.45);
    private static readonly Brush Shine = Frozen(new RadialGradientBrush(Color.FromArgb(0x55, 0xC8, 0xDA, 0xFF), Color.FromArgb(0x00, 0xC8, 0xDA, 0xFF)));

    private static void Scale(DrawingGroup g, double x, double y)
    {
        g.Children.Add(new GeometryDrawing(ShadowFill, null, Shape(x, y, 2.2)));        // shadow on the scale below
        g.Children.Add(new GeometryDrawing(BodyFill, null, Shape(x, y)));                // body
        g.Children.Add(new GeometryDrawing(null, RimPen, Shape(x, y, 0, false)));        // lit lower edge
        var ridge = Geometry.Parse($"M{F(x + 12)},{F(y + 2)} L{F(x + 12)},{F(y + 14)}"); // centre ridge
        g.Children.Add(new GeometryDrawing(null, RidgePen, ridge));
        g.Children.Add(new GeometryDrawing(Shine, null, new EllipseGeometry(new Point(x + 8, y + 6), 5, 3.2))); // highlight
    }

    public static DrawingBrush Build()
    {
        var g = new DrawingGroup();
        // Back to front: lower row first, upper rows last (they cover the top of the row below).
        Scale(g, -12, 12); Scale(g, 12, 12);
        Scale(g, 0, 0);
        Scale(g, -12, -12); Scale(g, 12, -12);
        g.ClipGeometry = new RectangleGeometry(new Rect(0, 0, 24, 24));
        var b = new DrawingBrush(g)
        {
            TileMode = TileMode.Tile, Stretch = Stretch.Fill,
            Viewbox = new Rect(0, 0, 24, 24), ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, TilePx, TilePx), ViewportUnits = BrushMappingMode.Absolute,
        };
        b.Freeze();
        return b;
    }
}
