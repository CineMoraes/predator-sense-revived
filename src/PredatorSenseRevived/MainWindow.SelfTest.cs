using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PredatorSenseRevived;

/// <summary>
/// --selftest report.txt [--live]: presses every control on every page, like a user, and records what changed.
/// Demo mode (default): no firmware. --live (admin): real firmware; only fan Auto/Max are written, ending on the mode found at start.
/// </summary>
public partial class MainWindow
{
    public string? SelfTestPath { get; set; }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    private static string NameOf(ButtonBase b)
    {
        string? auto = System.Windows.Automation.AutomationProperties.GetName(b);
        if (!string.IsNullOrEmpty(auto)) return auto;
        if (!string.IsNullOrEmpty(b.Name)) return b.Name;
        string text = string.Join(" ", Descendants(b).OfType<TextBlock>().Select(t => t.Text).Where(t => t != "").Take(2));
        if (text != "") return text;
        if (b.Content is string s) return s;
        return b.ToolTip?.ToString() ?? b.GetType().Name;
    }

    private string Observe() =>
        $"page={(new FrameworkElement[] { PageDash, PageFans, PagePerf, PageLight, PageSys }.First(p => p.Visibility == Visibility.Visible).Name)}" +
        $"|status={StatusText.Text}|toast={(Toast.Visibility == Visibility.Visible ? ToastText.Text : "")}" +
        $"|zone={_zone}|zones={string.Join(",", _zones)}|bright={Brightness.Value:0}|log={LogCsv.IsChecked}" +
        $"|fan={_state?.CpuFanMode}/{_state?.GpuFanMode}|turbo={_state?.TurboFlag}|oc={_state?.GpuOverclock}|rpm={_state?.CpuRpm}/{_state?.GpuRpm}";

    private static string Diff(string before, string after)
    {
        var a = before.Split('|'); var b = after.Split('|');
        var parts = new List<string>();
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) parts.Add(b[i]);
        return string.Join(" ; ", parts);
    }

    private async Task Settle(int ms)
    {
        await Task.Delay(ms);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    /// <summary>Can a real mouse reach this control? Hit-test its centre in the window.</summary>
    private bool Clickable(FrameworkElement b)
    {
        if (!b.IsVisible || b.ActualWidth < 1) return true; // hidden page controls: not checked here
        var p = b.TranslatePoint(new Point(b.ActualWidth / 2, b.ActualHeight / 2), this);
        var hit = InputHitTest(p) as DependencyObject;
        for (var d = hit; d != null; d = VisualTreeHelper.GetParent(d)) if (d == b) return true;
        return false;
    }

    private void UserClick(ButtonBase b)
    {
        // Same path as a real click: the window's lock check first.
        var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = PreviewMouseDownEvent, Source = b };
        b.RaiseEvent(down);
        if (down.Handled) return;
        // ButtonBase.OnClick is what a real mouse-up runs: toggles IsChecked AND raises Click.
        typeof(ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(b, null);
    }

    private void SavePng(string file)
    {
        var root = (FrameworkElement)Content;
        var bmp = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    public async Task RunSelfTest(string path)
    {
        var lines = new List<string> { $"SELF-TEST {DateTime.Now:yyyy-MM-dd HH:mm:ss}  mode={(_demo ? "demo" : "LIVE firmware")}  admin={App.IsAdmin()}", "" };
        int ok = 0, locked = 0, dead = 0;
        void Log(string res, string page, string ctl, string detail)
        {
            lines.Add($"{res,-8} | {page,-6} | {ctl,-34} | {detail}");
            if (res == "OK") ok++; else if (res == "LOCKED") locked++; else dead++;
        }
        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        int? startFan = _state?.CpuFanMode, startTurbo = _state?.TurboFlag;
        await Settle(800);

        var pages = new (RadioButton Nav, FrameworkElement Page, string Tag)[]
            { (NavDash, PageDash, "dash"), (NavFans, PageFans, "fans"), (NavPerf, PagePerf, "perf"), (NavLight, PageLight, "light"), (NavSys, PageSys, "sys") };
        var skip = new HashSet<string> { "Minimize", "Close", "OPEN DATA FOLDER" };

        // Header toggle (on every page)
        {
            string before = Observe(); UserClick(LogCsv); await Settle(300);
            string d = Diff(before, Observe()); Log(d != "" ? "OK" : "NO-REACT", "header", "LOG CSV toggle", d);
            UserClick(LogCsv); await Settle(200);
        }

        foreach (var (nav, page, tag) in pages)
        {
            UserClick(nav); await Settle(400);
            Log(page.Visibility == Visibility.Visible ? "OK" : "NO-REACT", "menu", nav.Name, "page visible: " + (page.Visibility == Visibility.Visible));

            var buttons = Descendants(page).OfType<ButtonBase>().Where(b => b.IsVisible).ToList();
            foreach (var b in buttons)
            {
                string name = NameOf(b);
                if (skip.Contains(name)) { lines.Add($"SKIPPED  | {tag,-6} | {name,-34} | opens another window or closes the app"); continue; }
                if (!b.IsEnabled) { Log("DISABLED", tag, name, "control is disabled (no feedback)"); continue; }
                bool isKey = b.Parent is Controls.KeyboardView;
                bool isFan = name.Contains("Auto") || name.Contains("Max") || name.Contains("AUTO") || name.Contains("MAX") || name.Contains("Turbo");
                if (!Clickable(b)) { Log("NO-HIT", tag, name, "a real mouse click does not reach this control"); continue; }
                ToastText.Text = ""; Toast.Visibility = Visibility.Collapsed;
                string before = Observe();
                UserClick(b);
                await Settle(isKey ? 60 : isFan && !_demo && Lock.Find(b) == null ? 5000 : 350);
                string after = Observe();
                string d = Diff(before, after);
                string? reason = Lock.Find(b);
                if (reason != null) Log(after.Contains("LOCKED") ? "LOCKED" : "NO-REACT", tag, name, after.Contains("LOCKED") ? "shows: " + reason : "locked but no message!");
                else Log(d != "" ? "OK" : "NO-REACT", tag, name, d == "" ? "nothing changed" : d);
            }
            UserClick(nav); await Settle(300);
            SavePng(Path.Combine(dir, $"selftest_{tag}.png"));
        }

        // Turbo hexagon and the TURBO slot on the keyboard deck (mouse-up handlers, not buttons)
        foreach (var (el, nav, label) in new (FrameworkElement, RadioButton, string)[] { (TurboHex, NavDash, "TURBO hexagon"), (TurboSlot, NavLight, "TURBO slot (keyboard)") })
        {
            UserClick(nav); await Settle(300);
            ToastText.Text = ""; Toast.Visibility = Visibility.Collapsed;
            string before = Observe();
            el.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent, Source = el });
            await Settle(_demo ? 350 : 6000);
            string d = Diff(before, Observe());
            Log(d != "" ? "OK" : "NO-REACT", "turbo", label, d == "" ? "nothing changed" : d);
        }

        // Brightness slider
        {
            NavLight.IsChecked = true; await Settle(200);
            string before = Observe(); Brightness.Value = 40; await Settle(200);
            string d = Diff(before, Observe()); Log(d != "" ? "OK" : "NO-REACT", "light", "Brightness slider", d);
            Brightness.Value = 100;
        }

        // Live: leave Turbo as it was at start.
        if (!_demo && startTurbo is 0 or 1 && _state?.TurboFlag != startTurbo)
        {
            await SetTurboMode(startTurbo == 1); await Settle(500);
            lines.Add($"RESTORE  | turbo  | back to {startTurbo}                     | now {_state?.TurboFlag}");
        }
        // Live: leave the fans as they were at start.
        if (!_demo && startFan is 1 or 2 && _state?.CpuFanMode != startFan)
        {
            await SetFan(startFan.Value); await Settle(500);
            lines.Add($"RESTORE  | fans   | back to mode {startFan}                | now {_state?.CpuFanMode}/{_state?.GpuFanMode}");
        }

        lines.Insert(1, $"TOTAL: {ok} OK, {locked} LOCKED (explain on click), {dead} problems (NO-REACT/DISABLED)");
        File.WriteAllLines(path, lines);
        Application.Current.Shutdown();
    }
}
