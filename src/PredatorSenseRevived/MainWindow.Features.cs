using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PredatorSenseRevived.Firmware;

namespace PredatorSenseRevived;

/// <summary>Features unlocked after --test-all PASS on 2026-11-13 (report in HANDOFF.md).</summary>
public partial class MainWindow
{
    /// <summary>One firmware change: snapshot once per run, write, read back, toast, refresh.</summary>
    private async Task<bool> Write(string what, Func<int> write, Func<bool> verify)
    {
        if (Blocked()) return false;
        if (_demo) { ShowToast($"Demo mode: {what} not sent (no firmware access)", false); return false; }
        if (!_wmi.IsConnected) { ShowToast("No firmware access: " + _wmi.LastError, false); return false; }
        while (_busy) await Task.Delay(50);
        _busy = true;
        bool ok = false;
        try
        {
            if (!_snapshotDone)
            {
                var before = await Task.Run(() => FirmwareState.Read(_wmi, true));
                Snapshot.Save(before, "before " + what);
                _snapshotDone = true;
            }
            int st = await Task.Run(write);
            if (st != 0) { ShowToast($"Firmware refused {what} (status {st})", false); return false; }
            await Task.Delay(700);
            ok = await Task.Run(verify);
            string msg = ok ? $"{what} · read back OK" : $"{what} sent, but the read-back differs";
            SetStatus(msg, ok); ShowToast(msg, ok);
        }
        catch (Exception ex) { ShowToast($"{what} error: {ex.Message}", false); }
        finally { _busy = false; }
        await Poll(full: true);
        return ok;
    }

    // ---------- laptop model and keyboard layout ----------

    /// <summary>On a model that was never tested, every firmware change is refused until the owner unlocks it.</summary>
    private bool Blocked()
    {
        if (Machine.IsTested || _settings.UnlockUntestedModel) return false;
        ShowToast($"Read-only: this model ({Machine.Model}) is not tested. Unlock in System > Safety, at your own risk.", false);
        return true;
    }

    private void InitModelAndLayout()
    {
        if (Machine.IsTested) ModelText.Text = "PREDATOR HELIOS 300 · PH315-52";
        else
        {
            ModelText.Text = (Machine.Model == "" ? "UNKNOWN MODEL" : Machine.Model.ToUpperInvariant()) + " · NOT TESTED";
            UntestedRow.Visibility = Visibility.Visible;
            UntestedText.Text = $"This app was tested only on the Predator Helios 300 PH315-52. On your {Machine.Model}, a firmware code can mean something else. Save a support file first and check the project page.";
            UnlockModel.IsChecked = _settings.UnlockUntestedModel;
        }
        string layout = _settings.KeyboardLayout;
        if (layout == "")
        {
            bool br = false;
            try { foreach (System.Globalization.CultureInfo ci in System.Windows.Input.InputLanguageManager.Current.AvailableInputLanguages!) br |= ci.Name == "pt-BR"; } catch { }
            layout = br ? "abnt2" : "us";
        }
        SetKeyboardLayout(layout, save: false);
    }

    public void SetKeyboardLayout(string id, bool save)
    {
        Keyboard.SetLayout(id);
        LayoutAbnt2.IsChecked = Keyboard.LayoutId == "abnt2";
        LayoutUs.IsChecked = Keyboard.LayoutId == "us";
        RefreshLighting();
        if (save && !_demo) { _settings.KeyboardLayout = Keyboard.LayoutId; _settings.Save(); }
    }

    private void Layout_Click(object sender, RoutedEventArgs e)
    {
        SetKeyboardLayout((string)((FrameworkElement)sender).Tag, save: true);
        ShowToast($"Keyboard layout: {Keyboard.LayoutId.ToUpperInvariant()}", true);
    }

    private void UnlockModel_Click(object sender, RoutedEventArgs e)
    {
        _settings.UnlockUntestedModel = UnlockModel.IsChecked == true;
        if (!_demo) _settings.Save();
        ShowToast(_settings.UnlockUntestedModel ? $"Controls unlocked on {Machine.Model}. A snapshot is saved before the first change." : "Controls are read-only again.", !_settings.UnlockUntestedModel);
    }

    /// <summary>One text file for support: app log + model report + last snapshot + last sensor lines. No user name, no PC name.</summary>
    private async void Support_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            while (_busy) await Task.Delay(50);
            _busy = true;
            bool live = !_demo && _wmi.IsConnected;
            string report = live ? await Task.Run(() => ModelReport.Build(_wmi)) : "(no firmware access: model report not included)";
            string name = SupportFile.Save(report);
            ShowToast($"Support file saved: {name}. Press OPEN DATA FOLDER to find it.", true);
        }
        catch (Exception ex) { ShowToast("Support file error: " + ex.Message, false); }
        finally { _busy = false; }
    }

    // ---------- custom fan speed (firmware %, min 20) ----------

    private void FanSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CpuPctText != null && CpuSlider != null) CpuPctText.Text = $"{(int)CpuSlider.Value} %";
        if (GpuPctText != null && GpuSlider != null) GpuPctText.Text = $"{(int)GpuSlider.Value} %";
    }

    private async void ModeCustom_Click(object sender, RoutedEventArgs e) { NavFans.IsChecked = true; await ApplyCustom(); }
    private async void ApplyCustom_Click(object sender, RoutedEventArgs e) => await ApplyCustom();

    private async Task ApplyCustom()
    {
        if (_state?.TurboFlag == 1) await SetTurboMode(false);
        int cpu = (int)CpuSlider.Value, gpu = (int)GpuSlider.Value;
        await Write($"Custom fans CPU {cpu} % / GPU {gpu} %", () =>
        {
            int st = _wmi.SetFanCustomMode(); if (st != 0) return st;
            st = _wmi.SetFanSpeed(1, cpu); if (st != 0) return st;
            return _wmi.SetFanSpeed(4, gpu);
        }, () => _wmi.FanModes() is var m && m.Cpu == 3 && m.Gpu == 3);
    }

    /// <summary>Fail-safe: never leave the fans on Custom when the app closes.</summary>
    private void FailSafeOnClose()
    {
        try { if (!_demo && _wmi.IsConnected && _wmi.FanModes().Cpu == 3) _wmi.SetFanMode(1); } catch { }
    }

    // ---------- GPU overclock ----------

    private async void Oc_Click(object sender, RoutedEventArgs e)
    {
        int level = int.Parse((string)((FrameworkElement)sender).Tag);
        if (level == 2) { await SetTurboMode(true); return; }          // GPU Turbo level = Turbo mode
        if (_state?.TurboFlag == 1) await SetTurboMode(false);          // leaves GPU OC on Normal
        if (level == 0 && _state?.GpuOverclock == 0) { ShowToast("GPU overclock: Normal", true); return; }
        await Write($"GPU overclock {(level == 1 ? "Faster" : "Normal")}", () => _wmi.SetMisc(5, level), () => (_wmi.Misc(5) & 0x0F) == level);
    }

    // ---------- toggles ----------

    private async void CoolBoost_Click(object sender, RoutedEventArgs e)
    {
        bool want = CoolBoost.IsChecked == true; CoolBoost.IsChecked = !want;
        if (!_wmi.HasApge && !_demo) { ShowToast("CoolBoost: APGeAction class not found", false); return; }
        await Write($"CoolBoost {(want ? "ON" : "OFF")}", () => _wmi.SetCoolBoost(want), () => _wmi.CoolBoost() == want);
    }

    private async void SysBoot_Click(object sender, RoutedEventArgs e)
    {
        bool want = SysBoot.IsChecked == true; SysBoot.IsChecked = !want;
        await Write($"Boot animation {(want ? "ON" : "OFF")}", () => _wmi.Misc(6) == (want ? 1 : 0) ? 0 : _wmi.SetMisc(6, want ? 1 : 0), () => _wmi.Misc(6) == (want ? 1 : 0));
    }

    private async void SysWin_Click(object sender, RoutedEventArgs e)
    {
        bool want = SysWin.IsChecked == true; SysWin.IsChecked = !want;
        await Write($"Windows key {(want ? "ON" : "LOCKED")}", () => _wmi.SetWinKey(want), () => _wmi.WinKey() == want);
    }

    private async void Usb_Click(object sender, RoutedEventArgs e)
    {
        int floor = int.Parse((string)((FrameworkElement)sender).Tag);
        if (!_wmi.HasApge && !_demo) { ShowToast("USB charging: APGeAction class not found", false); return; }
        await Write(floor == 0 ? "USB charging when off: OFF" : $"USB charging when off: until {floor} %", () => _wmi.SetUsbCharge(floor), () => _wmi.UsbCharge() == floor);
    }

    private async void Idle_Click(object sender, RoutedEventArgs e)
    {
        int secs = int.Parse((string)((FrameworkElement)sender).Tag);
        if (!_wmi.HasApge && !_demo) { ShowToast("Light timeout: APGeAction class not found", false); return; }
        await Write(secs == 0 ? "Keyboard light: never off" : "Keyboard light off after 30 s", () => _wmi.SetKbIdleOff(secs != 0), () => _wmi.KbTimeout()?.Seconds == (secs == 0 ? 0 : 30));
    }

    private async void StartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        bool want = StartWithWindows.IsChecked == true;
        if (_demo || !App.IsAdmin()) { StartWithWindows.IsChecked = !want; ShowToast("Start with Windows needs the app running as admin", false); return; }
        var r = await Task.Run(() => want ? Startup.Enable() : Startup.Disable());
        StartWithWindows.IsChecked = await Task.Run(Startup.IsEnabled);
        bool safe = Startup.ExeInProtectedFolder();
        ShowToast(!r.Ok ? "Start with Windows failed: " + r.Message : !want ? "Start with Windows: OFF"
            : safe ? "Start with Windows: ON (minimized, no UAC)" : "Start with Windows: ON. For safety, keep the exe in a Program Files folder (see README, Security).", r.Ok && (safe || !want));
    }

    // ---------- keyboard ----------

    private int _effect;

    private async Task WriteZoneColor(int zone, Color c)
    {
        if (_effect != 0) { ShowToast("Zone colors show with the STATIC effect.", false); }
        await Write($"Zone {zone + 1} color #{c.R:X2}{c.G:X2}{c.B:X2}", () => _wmi.SetZoneColor(1 << zone, c.R, c.G, c.B),
            () => _wmi.ZoneColor(1 << zone) == (c.R, c.G, c.B));
    }

    private async void Effect_Click(object sender, RoutedEventArgs e)
    {
        _effect = int.Parse((string)((FrameworkElement)sender).Tag);
        await ApplyEffect();
    }

    private async void Brightness_DragCompleted(object sender, DragCompletedEventArgs e) => await ApplyEffect();

    private async Task ApplyEffect()
    {
        string[] names = { "Static", "Breathing", "Neon", "Wave", "Shifting", "Zoom" };
        int bright = (int)Brightness.Value;
        var c = _zones[0];
        var zones = _zones.ToArray();
        // The firmware cannot read the effect back; "OK" here means it accepted every call.
        await Write($"Keyboard {names[_effect]} at {bright} %", () =>
        {
            int st = _wmi.SetKeyboardEffect(_effect, 5, bright, 1, c.R, c.G, c.B);
            if (st != 0 || _effect != 0) return st;
            for (int i = 0; i < 4 && st == 0; i++) st = _wmi.SetZoneColor(1 << i, zones[i].R, zones[i].G, zones[i].B);
            return st;
        }, () => true);
    }

    private void ApplyExtra(FirmwareState s)
    {
        if (s.CoolBoost is bool cb) CoolBoost.IsChecked = cb;
        if (s.KbIdleSecs is int k)
            foreach (RadioButton r in IdleSeg.Children) r.IsChecked = (string)r.Tag == k.ToString();
        if (s.UsbFloor is int u)
            foreach (RadioButton r in UsbSeg.Children) r.IsChecked = (string)r.Tag == u.ToString();
    }
}
