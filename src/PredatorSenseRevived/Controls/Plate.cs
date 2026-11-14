using System.Windows;
using System.Windows.Media;

namespace PredatorSenseRevived.Controls;

public enum PlateKind { Chamfer, ChamferTop, Slant, Hex }

/// <summary>
/// Angular background shape ("Lamborghini" lines): chamfered card, slanted button, or hexagon.
/// Put it first in a Grid, content on top.
/// </summary>
public sealed class Plate : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = Reg(nameof(Kind), typeof(PlateKind), PlateKind.Chamfer);
    public static readonly DependencyProperty CutProperty = Reg(nameof(Cut), typeof(double), 16.0);
    public static readonly DependencyProperty FillProperty = Reg(nameof(Fill), typeof(Brush), null);
    public static readonly DependencyProperty StrokeProperty = Reg(nameof(Stroke), typeof(Brush), null);
    public static readonly DependencyProperty StrokeThicknessProperty = Reg(nameof(StrokeThickness), typeof(double), 1.0);
    public static readonly DependencyProperty AccentAProperty = Reg(nameof(AccentA), typeof(Brush), null);
    public static readonly DependencyProperty AccentBProperty = Reg(nameof(AccentB), typeof(Brush), null);

    private static DependencyProperty Reg(string name, Type t, object? def) =>
        DependencyProperty.Register(name, t, typeof(Plate), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public PlateKind Kind { get => (PlateKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double Cut { get => (double)GetValue(CutProperty); set => SetValue(CutProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public Brush? AccentA { get => (Brush?)GetValue(AccentAProperty); set => SetValue(AccentAProperty, value); }
    public Brush? AccentB { get => (Brush?)GetValue(AccentBProperty); set => SetValue(AccentBProperty, value); }

    public Plate() { IsHitTestVisible = false; }

    public static Geometry Shape(PlateKind kind, double w, double h, double c)
    {
        c = Math.Min(c, Math.Min(w, h) / 2);
        Point[] p = kind switch
        {
            PlateKind.ChamferTop => new[] { new Point(c, 0), new Point(w - c, 0), new Point(w, c), new Point(w, h), new Point(0, h), new Point(0, c) },
            PlateKind.Slant => new[] { new Point(c, 0), new Point(w, 0), new Point(w - c, h), new Point(0, h) },
            PlateKind.Hex => new[] { new Point(w * 0.25, 0), new Point(w * 0.75, 0), new Point(w, h / 2), new Point(w * 0.75, h), new Point(w * 0.25, h), new Point(0, h / 2) },
            _ => new[] { new Point(c, 0), new Point(w, 0), new Point(w, h - c), new Point(w - c, h), new Point(0, h), new Point(0, c) },
        };
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(p[0], true, true);
            ctx.PolyLineTo(p.Skip(1).ToList(), true, true);
        }
        g.Freeze();
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, c = Cut;
        if (w <= 0 || h <= 0) return;
        double half = StrokeThickness / 2;
        var geo = Shape(Kind, Math.Max(0, w - StrokeThickness), Math.Max(0, h - StrokeThickness), c);
        dc.PushTransform(new TranslateTransform(half, half));
        dc.DrawGeometry(Fill, Stroke != null ? new Pen(Stroke, StrokeThickness) : null, geo);
        dc.Pop();
        if (Kind == PlateKind.Chamfer)
        {
            if (AccentA != null) dc.DrawLine(new Pen(AccentA, 2), new Point(0, c), new Point(c, 0));
            if (AccentB != null) dc.DrawLine(new Pen(AccentB, 2), new Point(w - c, h), new Point(w, h - c));
        }
        else if (Kind == PlateKind.ChamferTop)
        {
            if (AccentA != null) dc.DrawLine(new Pen(AccentA, 2), new Point(0, c), new Point(c, 0));
            if (AccentB != null) dc.DrawLine(new Pen(AccentB, 2), new Point(w - c, 0), new Point(w, c));
        }
    }
}
