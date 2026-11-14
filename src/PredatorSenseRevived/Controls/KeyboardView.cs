using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace PredatorSenseRevived.Controls;

/// <summary>
/// The Helios 300 keyboard. Layouts: "abnt2" (drawn from a photo of a PH315-52, docs/keyboard-ph315-52-abnt2.jpg)
/// and "us" (ANSI; derived from the same deck, not yet checked against a photo).
/// Grid of 150 columns of 1/8 key: main block 120 + gap 2 + numpad 28. Zones: main split 0-40-80-120, numpad = zone 4.
/// To add a layout: add a case in <see cref="Layout"/> and an entry in <see cref="Layouts"/>.
/// </summary>
public sealed class KeyboardView : Grid
{
    private sealed record KeyDef(string Label, string Sub, int X, int W, int Row, int H);
    private readonly List<(KeyDef Def, Button Btn, int Zone)> _keys = new();
    public event Action<int>? ZoneClicked;

    public static readonly (string Id, string Name)[] Layouts = { ("abnt2", "ABNT2"), ("us", "US") };
    public string LayoutId { get; private set; } = "";

    private static readonly HashSet<string> Rim = new() { "W", "A", "S", "D", "▲", "◀", "▼", "▶", "LOGO" };

    public KeyboardView()
    {
        for (int i = 0; i < 150; i++) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        for (int i = 0; i < 5; i++) RowDefinitions.Add(new RowDefinition { Height = new GridLength(54) });
        SetLayout("abnt2");
    }

    public void SetLayout(string id)
    {
        if (!Layouts.Any(l => l.Id == id)) id = "abnt2";
        if (id == LayoutId) return;
        LayoutId = id;
        Children.Clear(); _keys.Clear();
        foreach (var k in Layout(id))
        {
            double cx = k.X + k.W / 2.0;
            int zone = cx < 40 ? 0 : cx < 80 ? 1 : cx < 120 ? 2 : 3;
            var b = new Button { Margin = new Thickness(3), Focusable = true, Cursor = System.Windows.Input.Cursors.Hand, Tag = zone };
            System.Windows.Automation.AutomationProperties.SetName(b, $"Zone {zone + 1}, key {(k.Label == "" ? "Space" : k.Label == "LOGO" ? "PredatorSense" : k.Label)}");
            b.Click += (_, _) => ZoneClicked?.Invoke(zone);
            SetColumn(b, k.X); SetColumnSpan(b, k.W); SetRow(b, k.Row - 1); SetRowSpan(b, k.H);
            Children.Add(b);
            _keys.Add((k, b, zone));
        }
    }

    private static IEnumerable<KeyDef> Layout(string id)
    {
        var list = new List<KeyDef>();
        void Seq(int row, params (string L, string S, int W)[] keys) { int x = 0; foreach (var k in keys) { list.Add(new KeyDef(k.L, k.S, x, k.W, row, 1)); x += k.W; } }

        // Function row and numpad top: the same on both layouts.
        (string, string, int)[] f = { ("Esc", "", 0), ("F1", "", 10), ("F2", "", 17), ("F3", "", 24), ("F4", "", 31), ("F5", "", 40), ("F6", "", 47), ("F7", "", 54), ("F8", "", 61),
            ("F9", "", 70), ("F10", "", 77), ("F11", "", 84), ("F12", "Scr Lk", 91), ("PrtSc", "SysRq", 99), ("Del", "Ins", 106), ("Home", "", 113),
            ("Pg Up", "", 122), ("Pg Dn", "", 129), ("End", "", 136), ("⏻", "", 143) };
        list.AddRange(f.Select(k => new KeyDef(k.Item1, k.Item2, k.Item3, 7, 1, 1)));

        string numDot;
        if (id == "us")
        {
            Seq(2, ("`", "~", 6), ("1", "!", 8), ("2", "@", 8), ("3", "#", 8), ("4", "$", 8), ("5", "%", 8), ("6", "^", 8), ("7", "&", 8), ("8", "*", 8), ("9", "(", 8), ("0", ")", 8), ("-", "_", 8), ("=", "+", 8), ("⟵", "", 18));
            Seq(3, ("⇥", "", 10), ("Q", "", 8), ("W", "", 8), ("E", "", 8), ("R", "", 8), ("T", "", 8), ("Y", "", 8), ("U", "", 8), ("I", "", 8), ("O", "", 8), ("P", "", 8), ("[", "{", 8), ("]", "}", 8), ("\\", "|", 14));
            Seq(4, ("Caps", "", 12), ("A", "", 8), ("S", "", 8), ("D", "", 8), ("F", "", 8), ("G", "", 8), ("H", "", 8), ("J", "", 8), ("K", "", 8), ("L", "", 8), (";", ":", 8), ("'", "\"", 8), ("Enter", "", 20));
            Seq(5, ("⇧", "", 16), ("Z", "", 8), ("X", "", 8), ("C", "", 8), ("V", "", 8), ("B", "", 8), ("N", "", 8), ("M", "", 8), (",", "<", 8), (".", ">", 8), ("/", "?", 8), ("⇧", "", 16), ("▲", "", 8));
            Seq(6, ("Ctrl", "", 7), ("Fn", "", 7), ("WIN", "", 8), ("Alt", "", 8), ("", "", 58), ("Alt", "", 8), ("Ctrl", "", 8), ("◀", "", 8), ("▼", "", 8));
            numDot = ".";
        }
        else // abnt2
        {
            Seq(2, ("'", "\"", 6), ("1", "!", 8), ("2", "@", 8), ("3", "#", 8), ("4", "$", 8), ("5", "%", 8), ("6", "¨", 8), ("7", "&", 8), ("8", "*", 8), ("9", "(", 8), ("0", ")", 8), ("-", "_", 8), ("=", "+", 8), ("⟵", "", 18));
            Seq(3, ("⇥", "", 10), ("Q", "", 8), ("W", "", 8), ("E", "", 8), ("R", "", 8), ("T", "", 8), ("Y", "", 8), ("U", "", 8), ("I", "", 8), ("O", "", 8), ("P", "", 8), ("´", "`", 8), ("[", "{", 8));
            list.Add(new KeyDef("↵", "", 106, 14, 3, 2));
            Seq(4, ("Fixa", "", 10), ("A", "", 8), ("S", "", 8), ("D", "", 8), ("F", "", 8), ("G", "", 8), ("H", "", 8), ("J", "", 8), ("K", "", 8), ("L", "", 8), ("Ç", "", 8), ("~", "^", 8), ("]", "}", 8));
            Seq(5, ("⇧", "", 7), ("\\", "|", 8), ("Z", "", 8), ("X", "", 8), ("C", "", 8), ("V", "", 8), ("B", "", 8), ("N", "", 8), ("M", "", 8), (",", "<", 8), (".", ">", 8), (";", ":", 8), ("⇧", "", 17), ("▲", "", 8));
            Seq(6, ("Ctrl", "", 7), ("Fn", "", 7), ("WIN", "", 8), ("Alt", "", 8), ("", "", 42), ("Alt Gr", "", 8), ("☰", "", 8), ("/", "?", 16), ("◀", "", 8), ("▼", "", 8));
            numDot = ",";
        }

        (string, string, int, int)[] n = { ("LOGO", "", 122, 2), ("NumLk", "", 129, 2), ("/", "", 136, 2), ("*", "", 143, 2),
            ("7", "Home", 122, 3), ("8", "▲", 129, 3), ("9", "PgUp", 136, 3), ("-", "", 143, 3),
            ("4", "◁", 122, 4), ("5", "", 129, 4), ("6", "▷", 136, 4), ("+", "", 143, 4),
            ("1", "End", 122, 5), ("2", "▽", 129, 5), ("3", "PgDn", 136, 5),
            ("▶", "", 122, 6), ("0", "Ins", 129, 6), (numDot, "Del", 136, 6) };
        list.AddRange(n.Select(k => new KeyDef(k.Item1, k.Item2, k.Item3, 7, k.Item4, 1)));
        list.Add(new KeyDef("Enter", "", 143, 7, 5, 2));
        return list;
    }

    /// <summary>Repaints every key with its zone color. brightness 0..1.</summary>
    public void Paint(IReadOnlyList<Color> zones, int selectedZone, double brightness)
    {
        foreach (var (k, b, zone) in _keys)
        {
            var c = zones[zone];
            bool rim = Rim.Contains(k.Label), sel = zone == selectedZone;
            var brush = Gauge.Frozen(c);
            UIElement content;
            if (k.Label == "LOGO") content = Logo(brush);
            else if (k.Label == "WIN") content = WinLogo(brush);
            else
            {
                bool word = k.Label.Length > 2;
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                if (k.Sub != "") sp.Children.Add(new TextBlock { Text = k.Sub, FontSize = 9, Foreground = brush, Opacity = 0.85, HorizontalAlignment = HorizontalAlignment.Center });
                sp.Children.Add(new TextBlock
                {
                    Text = k.Label, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold,
                    FontSize = k.Row == 1 ? 10 : word ? 11 : 17, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI Symbol, Segoe UI"),
                });
                content = sp;
            }
            content.Effect = new DropShadowEffect { Color = c, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 };
            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new LinearGradientBrush(Color.FromRgb(0x1B, 0x20, 0x2A), Color.FromRgb(0x0E, 0x11, 0x18), 90),
                BorderBrush = rim ? brush : Gauge.Frozen(sel ? Color.FromRgb(0x3B, 0x4A, 0x6B) : Color.FromRgb(0x23, 0x2A, 0x37)),
                BorderThickness = new Thickness(rim ? 1.5 : 1),
                Child = content,
                Effect = new DropShadowEffect { Color = c, BlurRadius = rim ? 14 : 9, ShadowDepth = 0, Opacity = rim ? 0.55 : 0.22 },
            };
            b.Template = KeyTemplate();
            b.Content = border;
            b.Opacity = 0.32 + 0.68 * brightness;
        }
    }

    private static ControlTemplate? _tpl;
    private static ControlTemplate KeyTemplate()
    {
        if (_tpl != null) return _tpl;
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        _tpl = new ControlTemplate(typeof(Button)) { VisualTree = cp };
        _tpl.Seal();
        return _tpl;
    }

    private static UIElement Logo(Brush b) => new Path
    {
        Data = Geometry.Parse("M2 2 L7 18 L10 9 L13 18 L16 9 L19 18 L24 2 M13 2 V10"), Stroke = b, StrokeThickness = 1.8,
        Width = 26, Height = 20, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    private static UIElement WinLogo(Brush b) => new Path
    {
        Data = Geometry.Parse("M1 2.5 L7 1.6 V7.4 H1 Z M8.2 1.4 L15 0.4 V7.4 H8.2 Z M1 8.6 H7 V14.4 L1 13.5 Z M8.2 8.6 H15 V15.6 L8.2 14.6 Z"), Fill = b,
        Width = 16, Height = 16, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
}
