using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace PredatorSenseRevived.Controls;

/// <summary>Live history chart, 0-100 scale (°C or %), last <see cref="Capacity"/> samples (2 s each = 5 min).</summary>
public sealed class LineChart : FrameworkElement
{
    public const int Capacity = 150;
    private sealed record Series(string Name, string Unit, Color Color, List<double?> Values);
    private readonly List<Series> _series = new();

    public void AddSeries(string name, string unit, Color color) => _series.Add(new Series(name, unit, color, new List<double?>()));

    public void Push(params double?[] values)
    {
        for (int i = 0; i < _series.Count && i < values.Length; i++)
        {
            var v = _series[i].Values;
            v.Add(values[i]);
            if (v.Count > Capacity) v.RemoveAt(0);
        }
        InvalidateVisual();
    }

    private static readonly Typeface Tf = new(new FontFamily("Bahnschrift"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 50 || h < 50) return;
        double left = 34, top = 30, right = w - 8, bottom = h - 18;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var muted = Gauge.Frozen(Color.FromRgb(0x8D, 0x97, 0xAB));
        var grid = new Pen(Gauge.Frozen(Color.FromArgb(0x30, 0x6F, 0xA0, 0xFF)), 1) { DashStyle = new DashStyle(new double[] { 3, 5 }, 0) };
        foreach (int v in new[] { 0, 25, 50, 75, 100 })
        {
            double y = bottom - (bottom - top) * v / 100.0;
            dc.DrawLine(grid, new Point(left, y), new Point(right, y));
            var ft = new FormattedText(v.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Tf, 10, muted, dip);
            dc.DrawText(ft, new Point(left - 6 - ft.Width, y - ft.Height / 2));
        }
        var axis = new FormattedText("5 min ago", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Tf, 10, muted, dip);
        dc.DrawText(axis, new Point(left, bottom + 3));
        var now = new FormattedText("now", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Tf, 10, muted, dip);
        dc.DrawText(now, new Point(right - now.Width, bottom + 3));

        double step = (right - left) / (Capacity - 1);
        double lx = left;
        foreach (var s in _series)
        {
            var brush = Gauge.Frozen(s.Color);
            int n = s.Values.Count;
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                bool started = false;
                for (int i = 0; i < n; i++)
                {
                    if (s.Values[i] is not double v) { started = false; continue; }
                    var p = new Point(right - (n - 1 - i) * step, bottom - (bottom - top) * Math.Clamp(v, 0, 100) / 100.0);
                    if (!started) { c.BeginFigure(p, false, false); started = true; } else c.LineTo(p, true, true);
                }
            }
            geo.Freeze();
            var glow = new Pen(Gauge.Frozen(Color.FromArgb(0x40, s.Color.R, s.Color.G, s.Color.B)), 6) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, glow, geo);
            dc.DrawGeometry(null, new Pen(brush, 2) { LineJoin = PenLineJoin.Round }, geo);

            // legend: swatch, name, current value
            string last = n > 0 && s.Values[n - 1] is double lv ? $"{lv:0}{s.Unit}" : "--";
            dc.DrawRectangle(brush, null, new Rect(lx, 9, 14, 4));
            var name = new FormattedText($"{s.Name}  {last}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Tf, 12, Gauge.Frozen(Color.FromRgb(0xE9, 0xED, 0xF5)), dip);
            dc.DrawText(name, new Point(lx + 20, 3));
            lx += 20 + name.Width + 26;
        }
    }
}

/// <summary>CPU use % from GetSystemTimes (no extra package).</summary>
public sealed class CpuUsage
{
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    private long _idle, _total;

    public double? Sample()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user)) return null;
        long total = kernel + user;
        double? r = null;
        if (_total != 0 && total > _total) r = 100.0 * (1 - (double)(idle - _idle) / (total - _total));
        _idle = idle; _total = total;
        return r is double v ? Math.Clamp(v, 0, 100) : null;
    }
}

/// <summary>GPU use % from nvidia-smi (comes with the NVIDIA driver). Null when not available.</summary>
public static class GpuUsage
{
    private static bool _missing;

    public static double? Sample()
    {
        if (_missing) return null;
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"), "--query-gpu=utilization.gpu --format=csv,noheader,nounits")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!;
            string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(1500);
            return double.TryParse(o.Trim().Split('\n')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
        }
        catch { _missing = true; return null; }
    }
}
