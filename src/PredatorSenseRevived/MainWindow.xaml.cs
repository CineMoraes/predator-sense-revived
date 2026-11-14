using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PredatorSenseRevived.Controls;
using PredatorSenseRevived.Firmware;

namespace PredatorSenseRevived;

public partial class MainWindow : Window
{
    private readonly AcerWmi _wmi = new();
    private readonly SensorLog _log = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly bool _demo;
    private readonly AppSettings _settings = AppSettings.Load();
    private FirmwareState? _state;
    private bool _busy, _snapshotDone, _zonesTouched, _syncingChips;
    private int _tick, _zone;
    private readonly Color[] _zones = Enumerable.Repeat(Color.FromRgb(0x00, 0xAE, 0xC7), 4).ToArray();

    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x00, 0xAE, 0xC7), Color.FromRgb(0x3B, 0x6C, 0xFF), Color.FromRgb(0x8A, 0x5C, 0xFF), Color.FromRgb(0xFF, 0x2E, 0x9A),
        Color.FromRgb(0xFF, 0x3B, 0x3B), Color.FromRgb(0xFF, 0x8A, 0x00), Color.FromRgb(0xFF, 0xD4, 0x00), Color.FromRgb(0x2B, 0xFF, 0x88), Colors.White,
    };

    public MainWindow(bool demo)
    {
        _demo = demo;
        InitializeComponent();
        ScalesLayer.Fill = ScaleTexture.Build();
        History.AddSeries("CPU", " °C", Color.FromRgb(0x00, 0xAE, 0xC7));
        History.AddSeries("GPU", " °C", Color.FromRgb(0x6F, 0xA0, 0xFF));
        History.AddSeries("CPU use", " %", Color.FromRgb(0xF5, 0xA5, 0x24));
        History.AddSeries("GPU use", " %", Color.FromRgb(0x3D, 0xDC, 0x97));
        Loaded += async (_, _) => { if (!_demo) StartWithWindows.IsChecked = await Task.Run(Startup.IsEnabled); };
        Keyboard.ZoneClicked += z => { _zone = z; RefreshLighting(); ShowToast($"Zone {z + 1} selected. Pick a color below.", true); };
        LogCsv.Checked += (_, _) => ShowToast("CSV logging ON: " + _log.FileName, true);
        LogCsv.Unchecked += (_, _) => ShowToast("CSV logging OFF", true);
        BuildLightingControls();
        SysInfo.Text = $"Version {Machine.AppVersion}   ·   {Machine.Model} (BIOS {Machine.BiosVersion})   ·   Data folder: %LOCALAPPDATA%\\PredatorSenseRevived";
        InitModelAndLayout();
        AppLog.Write($"--- start v{Machine.AppVersion} | {Machine.Maker} {Machine.Model} | BIOS {Machine.BiosVersion} | {Environment.OSVersion.VersionString} | admin={App.IsAdmin()} demo={_demo}");
        _timer.Tick += async (_, _) => await Poll();
        Loaded += async (_, _) => { await Start(); if (SelfTestPath != null) await RunSelfTest(SelfTestPath); };
        Closed += (_, _) => { _timer.Stop(); FailSafeOnClose(); _wmi.Dispose(); };
    }

    // ---------- start and polling ----------

    private async Task Start()
    {
        if (_demo) { Apply(FirmwareState.Demo()); SetStatus("Demo values (no firmware access)", false); return; }
        SetStatus("Connecting to firmware…", true);
        bool ok = await Task.Run(() => _wmi.Connect());
        if (!ok) { AppLog.Write("! connect failed: " + _wmi.LastError); SetStatus("No firmware access: " + _wmi.LastError, false); RefreshLighting(); return; }
        await Task.Run(() => _wmi.ConnectApge());
        await Poll(full: true);
        _timer.Start();
    }

    private async Task Poll(bool full = false)
    {
        if (_busy || !_wmi.IsConnected) return;
        _busy = true;
        try
        {
            full |= _tick++ % 15 == 0;
            var s = await Task.Run(() => FirmwareState.Read(_wmi, full));
            if (!full && _state != null)
                s = s with { BootAnimation = _state.BootAnimation, ProfileWord = _state.ProfileWord, ZoneColors = _state.ZoneColors, CoolBoost = _state.CoolBoost, UsbFloor = _state.UsbFloor, KbIdleSecs = _state.KbIdleSecs };
            Apply(s);
            double? gpuUse = await Task.Run(Controls.GpuUsage.Sample);
            History.Push(s.CpuTemp, s.GpuTemp, _cpuUse.Sample(), gpuUse);
            if (LogCsv.IsChecked == true) _log.Append(s);
            SetStatus($"Firmware OK · admin · {s.Time:HH:mm:ss}", true);
        }
        catch (Exception ex) { AppLog.Write("! read error: " + ex.Message); SetStatus("Read error: " + ex.Message, false); }
        finally { _busy = false; }
    }

    private void SetStatus(string text, bool ok)
    {
        StatusText.Text = text;
        StatusDot.Fill = (Brush)FindResource(ok ? "Green" : "Amber");
    }

    // ---------- show state ----------

    private void Apply(FirmwareState s)
    {
        _state = s;
        CpuGauge.Temperature = s.CpuTemp; CpuGauge.Rpm = s.CpuRpm;
        GpuGauge.Temperature = s.GpuTemp; GpuGauge.Rpm = s.GpuRpm;
        SysGauge.Value = s.SysTemp;
        SysText.Text = s.SysTemp is int t ? $"{t} °C" : "--";
        GpuOcText.Text = s.GpuOverclock switch { 0 => "NORMAL", 1 => "FASTER", 2 => "TURBO", _ => "--" };
        bool turbo = s.TurboFlag == 1;
        TurboState.Text = s.TurboFlag is null ? "--" : turbo ? "ON" : "OFF";
        TurboEdge.Fill = turbo ? (Brush)FindResource("Accent") : (Brush)FindResource("Line");
        TurboFace.Opacity = turbo ? 1 : 0.35;
        TurboGlow.Opacity = turbo ? 0.85 : 0;
        TurboSlot.Background = turbo ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromRgb(0x15, 0x1A, 0x24));
        PerfTurbo.IsChecked = turbo;
        PerfTurboDesc.Text = "Fans Max and GPU overclock Turbo, like the Turbo key. Read from the laptop: " + (s.TurboFlag is null ? "unknown." : turbo ? "ON now." : "OFF now.");
        OcNormal.IsChecked = s.GpuOverclock == 0; OcFaster.IsChecked = s.GpuOverclock == 1; OcTurbo.IsChecked = s.GpuOverclock == 2;
        SysBoot.IsChecked = s.BootAnimation == 1;
        SysWin.IsChecked = s.ProfileWord != 0 && s.WinKeyEnabled;
        ApplyExtra(s);

        string mode = turbo ? "turbo" : s.CpuFanMode switch { 1 => "auto", 2 => "max", 3 => "custom", _ => "" };
        SetMode(ModeAuto, ModeAutoT, mode == "auto"); SetMode(ModeMax, ModeMaxT, mode == "max");
        SetMode(ModeTurbo, ModeTurboT, mode == "turbo"); SetMode(ModeCustom, ModeCustomT, mode == "custom");
        FanCardAuto.Tag = s.CpuFanMode == 1 ? "on" : null;
        FanCardMax.Tag = s.CpuFanMode == 2 ? "on" : null;
        FanCardCustom.Tag = s.CpuFanMode == 3 ? "on" : null;

        if (!_zonesTouched && s.ZoneColors.Length == 4)
            for (int i = 0; i < 4; i++)
                if (s.ZoneColors[i] != "") _zones[i] = (Color)ColorConverter.ConvertFromString(s.ZoneColors[i]);
        RefreshLighting();
    }

    private static void SetMode(Button b, TextBlock title, bool on)
    {
        b.Tag = on ? "on" : null;
        title.FontSize = on ? 27 : 21;
        title.Foreground = on ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x7D, 0x87, 0x9A));
    }

    // ---------- lighting (preview only: writing is not tested yet) ----------

    private void BuildLightingControls()
    {
        for (int i = 0; i < 4; i++)
        {
            int z = i;
            var chip = new RadioButton { Style = (Style)FindResource("Seg"), GroupName = "zone", Content = $"ZONE {i + 1}", Tag = z };
            chip.Checked += (_, _) => { if (!_syncingChips) { _zone = z; RefreshLighting(); } };
            ZoneChips.Children.Add(chip);
        }
        foreach (var c in Palette)
        {
            var col = c;
            var plate = new Plate { Kind = PlateKind.Hex, Fill = new SolidColorBrush(col) };
            var b = new Button { Width = 40, Height = 34, Margin = new Thickness(0, 0, 10, 8), Cursor = Cursors.Hand, ToolTip = $"#{col.R:X2}{col.G:X2}{col.B:X2}", Tag = col };
            b.Template = HexTemplate();
            b.Content = plate;
            b.Click += async (_, _) => { _zones[_zone] = col; _zonesTouched = true; RefreshLighting(); await WriteZoneColor(_zone, col); };
            Swatches.Children.Add(b);
        }
    }

    private static ControlTemplate HexTemplate()
    {
        // Transparent background: the hexagon shape itself ignores the mouse, the button must catch the click.
        var root = new FrameworkElementFactory(typeof(Border));
        root.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        root.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        return new ControlTemplate(typeof(Button)) { VisualTree = root };
    }

    private void RefreshLighting()
    {
        if (Keyboard == null || Bar0 == null) return;
        Keyboard.Paint(_zones, _zone, (Brightness?.Value ?? 100) / 100.0);
        var bars = new[] { Bar0, Bar1, Bar2, Bar3 };
        for (int i = 0; i < 4; i++)
        {
            bars[i].Fill = new SolidColorBrush(_zones[i]);
            bars[i].Opacity = i == _zone ? 1 : 0.3;
            bars[i].Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = _zones[i], BlurRadius = 10, ShadowDepth = 0 };
        }
        ZoneTitle.Text = $"ZONE {_zone + 1} COLOR";
        _syncingChips = true;
        foreach (RadioButton chip in ZoneChips.Children) chip.IsChecked = (int)chip.Tag == _zone;
        _syncingChips = false;
        foreach (Button sw in Swatches.Children)
        {
            bool sel = (Color)sw.Tag == _zones[_zone];
            sw.Opacity = sel ? 1 : 0.55;
            sw.RenderTransformOrigin = new Point(0.5, 0.5);
            sw.RenderTransform = new ScaleTransform(sel ? 1.15 : 1, sel ? 1.15 : 1);
        }
    }

    private void BrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BrightText != null) BrightText.Text = $"{(int)e.NewValue} %";
        RefreshLighting();
    }

    // ---------- fan mode (verified writes) ----------

    private async void FanAuto_Click(object sender, RoutedEventArgs e) => await ChooseMode("auto");
    private async void FanMax_Click(object sender, RoutedEventArgs e) => await ChooseMode("max");
    private async void ModeAuto_Click(object sender, RoutedEventArgs e) => await ChooseMode("auto");
    private async void ModeMax_Click(object sender, RoutedEventArgs e) => await ChooseMode("max");
    private async void ModeTurbo_Click(object sender, RoutedEventArgs e) => await ChooseMode("turbo");
    private async void TurboToggle_Click(object sender, MouseButtonEventArgs e) => await SetTurboMode(_state?.TurboFlag != 1);
    private async void PerfTurbo_Click(object sender, RoutedEventArgs e)
    {
        bool want = _state?.TurboFlag != 1;
        PerfTurbo.IsChecked = !want; // show the real state until the firmware confirms
        await SetTurboMode(want);
    }

    /// <summary>Mode bar logic: Turbo is a mode on top of the fans. Auto/Max leave Turbo first.</summary>
    private async Task ChooseMode(string mode)
    {
        if (mode == "turbo") { await SetTurboMode(true); return; }
        if (_state?.TurboFlag == 1)
        {
            await SetTurboMode(false);          // Turbo OFF already sets fans Auto
            if (mode == "auto") return;
        }
        await SetFan(mode == "auto" ? 1 : 2);
    }

    /// <summary>VERIFIED 13-11 (--test-turbo): OFF and ON both read back as expected and the end state matched the start.</summary>
    private async Task SetTurboMode(bool on)
    {
        string name = on ? "ON" : "OFF";
        if (Blocked()) return;
        if (_demo) { ShowToast($"Demo mode: Turbo {name} not sent (no firmware access)", false); return; }
        if (!_wmi.IsConnected) { ShowToast("No firmware access: " + _wmi.LastError, false); return; }
        if (on && !TurboTest.OnAc()) { ShowToast("Turbo needs the charger connected (as PredatorSense).", false); return; }
        ShowToast($"Turbo {name}…", true);
        while (_busy) await Task.Delay(50);
        _busy = true;
        try
        {
            if (!_snapshotDone)
            {
                var before = await Task.Run(() => FirmwareState.Read(_wmi, true));
                Snapshot.Save(before, $"before Turbo {name}");
                _snapshotDone = true;
            }
            var steps = await Task.Run(() => _wmi.SetTurbo(on));
            var bad = steps.FirstOrDefault(s => !s.Skipped && s.Status != 0);
            if (bad != null) { ShowToast($"Firmware refused {bad.What} (status {bad.Status}). Stopped.", false); SetStatus($"Turbo {name}: refused at {bad.What}", false); return; }
            await Task.Delay(1500);
            var s = await Task.Run(() => FirmwareState.Read(_wmi, false));
            bool ok = s.TurboFlag == (on ? 1 : 0) && s.GpuOverclock == (on ? 2 : 0) && s.CpuFanMode == (on ? 2 : 1);
            string msg = ok ? $"Turbo {name} · read back OK" : $"Turbo {name} sent, firmware reads turbo={s.TurboFlag} gpuOc={s.GpuOverclock} fans={s.CpuFanMode}";
            SetStatus(msg, ok); ShowToast(msg, ok);
        }
        catch (Exception ex) { ShowToast("Turbo error: " + ex.Message, false); }
        finally { _busy = false; }
        await Poll(full: true);
    }

    private async Task SetFan(int mode)
    {
        string name = mode == 1 ? "Auto" : "Max";
        if (Blocked()) return;
        if (_demo) { SetStatus("Demo mode: no firmware writes", false); ShowToast($"Demo mode: Fans {name} not sent (no firmware access)", false); return; }
        if (!_wmi.IsConnected) { SetStatus("No firmware access: " + _wmi.LastError, false); ShowToast("No firmware access: " + _wmi.LastError, false); return; }
        ShowToast($"Setting fans to {name}…", true);
        // Note: fan mode works with Turbo ON too (tested 13-11 in PredatorControl v1: Auto dropped 6360 -> 5160 RPM while turbo flag = 1).
        while (_busy) await Task.Delay(50);
        _busy = true;
        try
        {
            if (!_snapshotDone)
            {
                var before = await Task.Run(() => FirmwareState.Read(_wmi, true));
                string file = Snapshot.Save(before, $"before fan mode {name}");
                _snapshotDone = true;
                SetStatus("Snapshot saved: " + Path.GetFileName(file), true);
            }
            int rc = await Task.Run(() => _wmi.SetFanMode(mode));
            if (rc != 0) { SetStatus($"Firmware refused fan mode {name} (status {rc})", false); return; }
            await Task.Delay(1500);
            var modes = await Task.Run(() => _wmi.FanModes());
            bool ok = modes.Cpu == mode && modes.Gpu == mode;
            string msg = ok ? $"Fans: {name} · read back OK" : $"Fans: sent {name}, but firmware reads CPU {modes.Cpu} / GPU {modes.Gpu}";
            SetStatus(msg, ok);
            ShowToast(msg, ok);
        }
        catch (Exception ex) { SetStatus("Write error: " + ex.Message, false); }
        finally { _busy = false; }
        await Poll(full: true);
    }

    // ---------- other actions ----------

    private async void SaveSnapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_demo || !_wmi.IsConnected) { SetStatus("No firmware access: nothing to save", false); ShowToast("No firmware access: nothing to save", false); return; }
        try
        {
            var s = await Task.Run(() => FirmwareState.Read(_wmi, true));
            string f = Path.GetFileName(Snapshot.Save(s, "manual"));
            SetStatus("Snapshot saved: " + f, true);
            ShowToast("Snapshot saved: " + f, true);
        }
        catch (Exception ex) { SetStatus("Snapshot error: " + ex.Message, false); ShowToast("Snapshot error: " + ex.Message, false); }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.Root);
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"\"{AppPaths.Root}\"") { UseShellExecute = false });
    }

    private void NavChanged(object sender, RoutedEventArgs e)
    {
        if (PageDash == null) return;
        var map = new (RadioButton Nav, FrameworkElement Page, string Sub)[]
        {
            (NavDash, PageDash, "Live temperatures, fans and modes"), (NavFans, PageFans, "Auto, Max or your own speed"),
            (NavPerf, PagePerf, "Turbo, GPU overclock and CoolBoost"), (NavLight, PageLight, "4-zone keyboard color and effects"),
            (NavSys, PageSys, "Boot, keys, charging and safety"),
        };
        foreach (var (nav, page, sub) in map)
        {
            page.Visibility = nav.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (nav.IsChecked == true) PageSub.Text = sub;
        }
    }

    // ---------- locked controls and click feedback ----------

    private readonly Controls.CpuUsage _cpuUse = new();

    /// <summary>
    /// PrtSc: Windows' "PrtSc opens Snipping Tool" hotkey does not work while an admin window has focus
    /// (a normal process cannot see keys sent to an admin window), so Windows falls back to a full-screen copy.
    /// We open the snipping overlay ourselves.
    /// </summary>
    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Snapshot || e.SystemKey == Key.Snapshot)
        {
            try { Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true }); } catch { }
            e.Handled = true;
        }
    }

    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4.5) };

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && Lock.Find(e.OriginalSource as DependencyObject) is string reason) { e.Handled = true; ShowToast("LOCKED · " + reason, false); }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Space || e.Key == Key.Enter) && Lock.Find(System.Windows.Input.Keyboard.FocusedElement as DependencyObject) is string reason)
        { e.Handled = true; ShowToast("LOCKED · " + reason, false); }
    }

    public void ShowToast(string text, bool ok)
    {
        AppLog.Write((ok ? "" : "! ") + text);
        ToastText.Text = text;
        var b = (Brush)FindResource(ok ? "LightBlue" : "Amber");
        ToastPlate.Stroke = b; ToastIcon.Fill = b;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Tick -= HideToast; _toastTimer.Tick += HideToast;
        _toastTimer.Start();
    }

    private void HideToast(object? sender, EventArgs e) { _toastTimer.Stop(); Toast.Visibility = Visibility.Collapsed; }

    private void GoSystem(object sender, RoutedEventArgs e) => NavSys.IsChecked = true;
    private void GoLighting(object sender, RoutedEventArgs e) => NavLight.IsChecked = true;
    private void Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseApp(object sender, RoutedEventArgs e) => Close();

    private void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            try { DragMove(); } catch (InvalidOperationException) { }
    }

    // ---------- render to PNG (for checking the UI without admin) ----------

    public void RenderToPng(string path, string page)
    {
        Apply(FirmwareState.Demo());
        for (int i = 0; i < Controls.LineChart.Capacity; i++) History.Push(70 + 8 * Math.Sin(i / 9.0), 50 + 3 * Math.Sin(i / 13.0), 35 + 30 * Math.Max(0, Math.Sin(i / 20.0)), 10 + 5 * Math.Sin(i / 7.0));
        SetStatus("Demo values (render mode)", false);
        (page switch { "fans" => NavFans, "perf" => NavPerf, "light" => NavLight, "sys" => NavSys, _ => NavDash }).IsChecked = true;
        var root = (FrameworkElement)Content;
        var size = new Size(Width, Height);
        root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) enc.Save(fs);
        Application.Current.Shutdown();
    }
}
