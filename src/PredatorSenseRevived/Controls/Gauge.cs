using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PredatorSenseRevived.Controls;

/// <summary>
/// Round CPU/GPU gauge: segmented LED ring = temperature (0-100 °C), outer arc = fan RPM (0-7000),
/// octagon face, corner brackets. Drawn in a 360 x 360 space and scaled.
/// </summary>
public sealed class Gauge : FrameworkElement
{
    public static readonly DependencyProperty TitleProperty = Reg(nameof(Title), typeof(string), "CPU");
    public static readonly DependencyProperty TemperatureProperty = Reg(nameof(Temperature), typeof(double?), null);
    public static readonly DependencyProperty RpmProperty = Reg(nameof(Rpm), typeof(double?), null);
    public static readonly DependencyProperty FanLabelProperty = Reg(nameof(FanLabel), typeof(string), "CPU FAN");

    private static DependencyProperty Reg(string n, Type t, object? d) =>
        DependencyProperty.Register(n, t, typeof(Gauge), new FrameworkPropertyMetadata(d, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public double? Temperature { get => (double?)GetValue(TemperatureProperty); set => SetValue(TemperatureProperty, value); }
    public double? Rpm { get => (double?)GetValue(RpmProperty); set => SetValue(RpmProperty, value); }
    public string FanLabel { get => (string)GetValue(FanLabelProperty); set => SetValue(FanLabelProperty, value); }

    internal static readonly Color Cyan = Color.FromRgb(0x00, 0xAE, 0xC7), Blue = Color.FromRgb(0x3B, 0x6C, 0xFF);
    private static readonly Brush Line = Frozen(Color.FromRgb(0x2A, 0x35, 0x50)), Tick = Frozen(Color.FromRgb(0x3A, 0x45, 0x60)),
        Muted = Frozen(Color.FromRgb(0x8D, 0x97, 0xAB)), Off = Frozen(Color.FromRgb(0x16, 0x1D, 0x2C)), Track = Frozen(Color.FromRgb(0x15, 0x1C, 0x2B)),
        CyanB = Frozen(Cyan), White = Brushes.White;
    private static readonly Typeface Display = new(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface DisplayOblique = new(new FontFamily("Bahnschrift"), FontStyles.Oblique, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface Body = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    internal static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    protected override Size MeasureOverride(Size available)
    {
        double s = Math.Min(double.IsInfinity(available.Width) ? 360 : available.Width, double.IsInfinity(available.Height) ? 360 : available.Height);
        s = Math.Min(s, 400);
        return new Size(s, s);
    }

    private static Point Pt(double r, double deg)
    {
        double a = deg * Math.PI / 180;
        return new Point(180 + r * Math.Cos(a), 180 + r * Math.Sin(a));
    }

    internal static Color TempColor(double frac)
    {
        if (frac >= 0.9) return Color.FromRgb(0xFF, 0x6B, 0x6B);
        if (frac >= 0.8) return Color.FromRgb(0xF5, 0xA5, 0x24);
        double p = frac / 0.8;
        return Color.FromRgb((byte)(59 * p), (byte)(174 - 66 * p), (byte)(199 + 56 * p));
    }

    private static Geometry Arc(double r, double startDeg, double sweepDeg)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(Pt(r, startDeg), false, false);
            c.ArcTo(Pt(r, startDeg + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    private void Text(DrawingContext dc, string s, Typeface tf, double size, Brush b, double cx, double cy)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
    }

    protected override void OnRender(DrawingContext dc)
    {
        double scale = Math.Min(ActualWidth, ActualHeight) / 360.0;
        if (scale <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - 360 * scale) / 2, 0));
        dc.PushTransform(new ScaleTransform(scale, scale));

        // corner brackets
        var bracket = new Pen(Line, 2);
        foreach (var (a, b, c) in new[] { ((4.0, 48.0), (4.0, 4.0), (48.0, 4.0)), ((312.0, 4.0), (356.0, 4.0), (356.0, 48.0)), ((356.0, 312.0), (356.0, 356.0), (312.0, 356.0)), ((48.0, 356.0), (4.0, 356.0), (4.0, 312.0)) })
        {
            dc.DrawLine(bracket, new Point(a.Item1, a.Item2), new Point(b.Item1, b.Item2));
            dc.DrawLine(bracket, new Point(b.Item1, b.Item2), new Point(c.Item1, c.Item2));
        }
        var accent = new Pen(CyanB, 2);
        dc.DrawLine(accent, new Point(4, 18), new Point(18, 4));
        dc.DrawLine(accent, new Point(342, 356), new Point(356, 342));

        // octagon face
        var face = new LinearGradientBrush(Color.FromArgb(235, 0x1C, 0x25, 0x40), Color.FromArgb(235, 0x0B, 0x10, 0x19), 90);
        var oct = new StreamGeometry();
        using (var c = oct.Open())
        {
            c.BeginFigure(Pt(100, 22.5), true, true);
            c.PolyLineTo(Enumerable.Range(1, 7).Select(k => Pt(100, 22.5 + 45 * k)).ToList(), true, true);
        }
        dc.DrawGeometry(face, new Pen(Line, 1.5), oct);

        // ticks and labels
        for (int v = 0; v <= 100; v += 5)
        {
            double a = 135 + 270 * v / 100.0; bool big = v % 20 == 0;
            dc.DrawLine(new Pen(Tick, big ? 2.5 : 1), Pt(big ? 134 : 138, a), Pt(143, a));
        }
        foreach (int v in new[] { 0, 20, 40, 60, 80, 100 })
        {
            var p = Pt(163, 135 + 270 * v / 100.0);
            Text(dc, v.ToString(), Display, 12, Muted, p.X, p.Y);
        }

        // fan arc
        dc.DrawGeometry(null, new Pen(Track, 8), Arc(150, 135, 270));
        double rf = Math.Clamp((Rpm ?? 0) / 7000.0, 0, 1);
        if (rf > 0.001)
        {
            var grad = new LinearGradientBrush(Cyan, Blue, new Point(0, 1), new Point(1, 0));
            dc.DrawGeometry(null, new Pen(grad, 8), Arc(150, 135, 270 * rf));
        }

        // LED temperature ring
        double f = Math.Clamp((Temperature ?? 0) / 100.0, 0, 1);
        const int N = 40;
        for (int i = 0; i < N; i++)
        {
            double p = (i + 0.5) / N, a = 135 + 270 * p;
            var brush = p <= f && Temperature.HasValue ? Frozen(TempColor(p)) : Off;
            dc.DrawLine(new Pen(brush, 6), Pt(108, a), Pt(127, a));
        }

        // texts
        var titleBrush = new LinearGradientBrush(Cyan, Blue, 0);
        Text(dc, Title, DisplayOblique, 34, titleBrush, 180, 136);
        Text(dc, Temperature.HasValue ? ((int)Temperature.Value).ToString() : "--", Display, 64, White, 180, 190);
        string state = !Temperature.HasValue ? "NO DATA" : Temperature >= 90 ? "HOT" : Temperature >= 80 ? "WARM" : "NORMAL";
        Text(dc, "°C  //  " + state, Display, 13, Muted, 180, 236);
        dc.DrawLine(new Pen(Line, 1), new Point(120, 298), new Point(240, 298));
        Text(dc, Rpm.HasValue ? $"{Rpm.Value:N0} RPM" : "-- RPM", Display, 18, White, 180, 318);
        Text(dc, FanLabel, Body, 11, Muted, 180, 340);

        dc.Pop(); dc.Pop();
    }
}

/// <summary>Half gauge with a needle, for the system temperature.</summary>
public sealed class HalfGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double?), typeof(HalfGauge),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public double? Value { get => (double?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    protected override Size MeasureOverride(Size a) => new(260, 160);

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth / 260, ActualHeight / 160);
        dc.PushTransform(new ScaleTransform(s, s));
        Point C = new(130, 130);
        Geometry Arc(double frac)
        {
            var g = new StreamGeometry();
            using var c = g.Open();
            c.BeginFigure(new Point(30, 130), false, false);
            double a = Math.PI + Math.PI * frac;
            c.ArcTo(new Point(130 + 100 * Math.Cos(a), 130 + 100 * Math.Sin(a)), new Size(100, 100), 0, false, SweepDirection.Clockwise, true, false);
            return g;
        }
        dc.DrawGeometry(null, new Pen(Gauge.Frozen(Color.FromRgb(0x15, 0x1C, 0x2B)), 10), Arc(0.9999));
        double f = Math.Clamp((Value ?? 0) / 100.0, 0, 1);
        if (f > 0.001) dc.DrawGeometry(null, new Pen(new LinearGradientBrush(Gauge.Cyan, Gauge.Blue, 0), 10), Arc(f));
        var line = new Pen(Gauge.Frozen(Color.FromRgb(0x2A, 0x35, 0x50)), 2);
        dc.DrawLine(line, new Point(14, 130), new Point(30, 130));
        dc.DrawLine(line, new Point(230, 130), new Point(246, 130));
        double na = Math.PI + Math.PI * f;
        Point tip = new(130 + 82 * Math.Cos(na), 130 + 82 * Math.Sin(na));
        double px = -Math.Sin(na) * 4, py = Math.Cos(na) * 4;
        var needle = new StreamGeometry();
        using (var c = needle.Open())
        {
            c.BeginFigure(new Point(C.X + px, C.Y + py), true, true);
            c.PolyLineTo(new[] { tip, new Point(C.X - px, C.Y - py) }, true, true);
        }
        dc.DrawGeometry(Gauge.Frozen(Color.FromRgb(0x6F, 0xA0, 0xFF)), null, needle);
        dc.PushTransform(new RotateTransform(45, 130, 130));
        dc.DrawRectangle(Gauge.Frozen(Gauge.Blue), null, new Rect(124, 124, 12, 12));
        dc.Pop();
        var tf = new Typeface(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var muted = Gauge.Frozen(Color.FromRgb(0x8D, 0x97, 0xAB));
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        void Label(string s, double x, double y)
        {
            var ft = new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 11, muted, dip);
            dc.DrawText(ft, new Point(x - ft.Width / 2, y));
        }
        Label("0", 22, 140); Label("50", 130, 12); Label("100°", 236, 140);
        dc.Pop();
    }
}
