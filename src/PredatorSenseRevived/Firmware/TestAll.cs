using System.IO;

namespace PredatorSenseRevived.Firmware;

/// <summary>
/// --test-all report.txt (admin, charger): supervised write test of every locked feature, one after another.
/// For each: read -> change -> read back -> restore -> read back. A refused feature is marked and the run goes on.
/// At the end the whole state is compared with the start snapshot and restored.
/// </summary>
public static class TestAll
{
    private static readonly List<string> Log = new();
    private static readonly Dictionary<string, string> Result = new();
    private static void L(string s) => Log.Add(s);
    private static void Wait(double s) => Thread.Sleep((int)(s * 1000));

    private static void Feature(string name, Func<string> body)
    {
        L(""); L($"=== {name}");
        try { string r = body(); Result[name] = r; L($"--> {r}"); }
        catch (Exception ex) { Result[name] = "ERROR"; L($"--> ERROR {ex.GetType().Name}: {ex.Message}"); }
    }

    private static string Hex(ulong v) => $"0x{v:X}";

    public static void Run(string path)
    {
        L($"TEST ALL {DateTime.Now:yyyy-MM-dd HH:mm:ss}  admin={App.IsAdmin()}");
        try { Body(); } catch (Exception ex) { L("FATAL " + ex); }
        L(""); L("SUMMARY");
        foreach (var (k, v) in Result) L($"  {k,-28} {v}");
        File.WriteAllLines(path, Log);
    }

    private static void Body()
    {
        using var wmi = new AcerWmi();
        if (!wmi.Connect()) { L("connect FAIL: " + wmi.LastError); return; }
        if (!TurboTest.OnAc()) { L("STOP: on battery. No writes made."); return; }
        bool apge = wmi.ConnectApge();
        L($"APGeAction class: {(apge ? "found" : "NOT found")}");

        var start = FirmwareState.Read(wmi, true);
        L("snapshot: " + Path.GetFileName(Snapshot.Save(start, "before test-all")));
        L($"start: turbo={start.TurboFlag} led={start.TurboLed} oc={start.GpuOverclock} fans={start.CpuFanMode}/{start.GpuFanMode} boot={start.BootAnimation} zones={string.Join(",", start.ZoneColors)} profile={Hex(start.ProfileWord)}");

        // ---------- read-only probe of the new parts ----------
        L(""); L("=== PROBE (read only)");
        L($"duty sensors: cpu(12)={wmi.Sensor(12)} gpu(16)={wmi.Sensor(16)} fan3(17)={wmi.Sensor(17)}");
        (int B, int S)? kb0 = null; bool? cb0 = null; int? usb0 = null;
        if (apge)
        {
            try { cb0 = wmi.CoolBoost(); L($"CoolBoost get 0x207 -> {Hex(wmi.Apge("GetFunction", 0x207))} state={cb0}"); } catch (Exception e) { L("CoolBoost get error " + e.Message); }
            try { usb0 = wmi.UsbCharge(); L($"USB charge get 0x204 -> {Hex(wmi.Apge("GetFunction", 0x204))} floor={usb0}"); } catch (Exception e) { L("USB get error " + e.Message); }
            try { kb0 = wmi.KbTimeout(); L($"KB timeout get 0x88401 -> {Hex(wmi.Apge("GetFunction", 0x88401))} value={kb0}"); } catch (Exception e) { L("KB timeout get error " + e.Message); }
        }
        int brightness = kb0?.B is int bb && bb > 0 ? bb : 100;

        // ---------- 1. keyboard zone color ----------
        Feature("Keyboard zone color", () =>
        {
            var orig = new[] { 1, 2, 4, 8 }.Select(z => wmi.ZoneColor(z)).ToArray();
            if (orig.Any(c => c == null)) return "UNSUPPORTED (zone read failed)";
            (byte R, byte G, byte B)[] test = { (255, 40, 0), (0, 255, 80), (140, 60, 255), (255, 200, 0) };
            for (int i = 0; i < 4; i++) L($"set zone {1 << i} -> status {wmi.SetZoneColor(1 << i, test[i].R, test[i].G, test[i].B)}");
            Wait(3);
            bool ok = true;
            for (int i = 0; i < 4; i++) { var c = wmi.ZoneColor(1 << i); L($"read zone {1 << i} = {c}"); ok &= c == test[i]; }
            for (int i = 0; i < 4; i++) { var o = orig[i]!.Value; wmi.SetZoneColor(1 << i, o.R, o.G, o.B); }
            Wait(1);
            bool back = new[] { 1, 2, 4, 8 }.Select((z, i) => wmi.ZoneColor(z) == orig[i]).All(x => x);
            L($"restored: {back}");
            return ok && back ? "PASS" : ok ? "PASS (restore check failed)" : "FAIL (read back differs)";
        });

        // ---------- 2. keyboard effects (cannot be read back) ----------
        Feature("Keyboard effects", () =>
        {
            var names = new[] { "Static", "Breathing", "Neon", "Wave", "Shifting", "Zoom" };
            var statuses = new List<int>();
            foreach (int fx in new[] { 1, 2, 3, 4, 5 })
            {
                int st = wmi.SetKeyboardEffect(fx, 5, brightness, 1, 0x00, 0xAE, 0xC7);
                statuses.Add(st);
                L($"effect {names[fx]} -> status {st}");
                Wait(4);
            }
            int back = wmi.SetKeyboardEffect(0, 0, brightness, 0, 0, 0, 0);
            L($"back to Static (brightness {brightness}) -> status {back}");
            var zones = start.ZoneColors;
            for (int i = 0; i < 4; i++)
                if (zones.Length == 4 && zones[i].Length == 7)
                {
                    byte H(int k) => Convert.ToByte(zones[i].Substring(k, 2), 16);
                    wmi.SetZoneColor(1 << i, H(1), H(3), H(5));
                }
            return statuses.All(s => s == 0) && back == 0 ? "PASS (firmware accepted; check the keyboard by eye)" : $"FAIL (statuses {string.Join(",", statuses)}, static {back})";
        });

        // ---------- 3. boot animation ----------
        Feature("Boot animation", () =>
        {
            int? orig = wmi.Misc(6);
            if (orig is not (0 or 1)) return $"UNSUPPORTED (misc 6 = {orig})";
            int want = 1 - orig.Value;
            L($"set misc 6 = {want} -> status {wmi.SetMisc(6, want)}");
            Wait(1); int? got = wmi.Misc(6); L($"read misc 6 = {got}");
            L($"restore misc 6 = {orig} -> status {wmi.SetMisc(6, orig.Value)}");
            Wait(1); int? back = wmi.Misc(6); L($"read misc 6 = {back}");
            return got == want && back == orig ? "PASS (shows at next power on)" : "FAIL";
        });

        // ---------- 4. Windows key ----------
        Feature("Windows key lock", () =>
        {
            bool? orig = wmi.WinKey();
            if (orig == null) return "UNSUPPORTED";
            L($"profile before {Hex(wmi.ProfileWord())}");
            L($"set Win key {(!orig.Value ? "ON" : "OFF")} -> status {wmi.SetWinKey(!orig.Value)}");
            Wait(1); bool? got = wmi.WinKey(); L($"profile after {Hex(wmi.ProfileWord())} winKey={got}");
            L($"restore -> status {wmi.SetWinKey(orig.Value)}");
            Wait(1); bool? back = wmi.WinKey(); L($"profile restored {Hex(wmi.ProfileWord())} winKey={back}");
            return got == !orig && back == orig ? "PASS" : "FAIL";
        });

        // ---------- 5. CoolBoost ----------
        Feature("CoolBoost", () =>
        {
            if (!apge || cb0 == null) return "UNSUPPORTED (no APGeAction or read refused)";
            L($"set CoolBoost {!cb0} -> status {Hex((ulong)wmi.SetCoolBoost(!cb0.Value))}");
            Wait(1); bool? got = wmi.CoolBoost(); L($"read = {got}");
            L($"restore -> status {Hex((ulong)wmi.SetCoolBoost(cb0.Value))}");
            Wait(1); bool? back = wmi.CoolBoost(); L($"read = {back}");
            return got == !cb0 && back == cb0 ? "PASS" : "FAIL";
        });

        // ---------- 6. USB charging when off ----------
        Feature("USB charging when off", () =>
        {
            if (!apge || usb0 == null) return "UNSUPPORTED (no APGeAction or read refused)";
            int want = usb0 == 20 ? 30 : 20;
            L($"set floor {want} -> status {Hex((ulong)wmi.SetUsbCharge(want))}");
            Wait(1); int? got = wmi.UsbCharge(); L($"read = {got}");
            L($"restore {usb0} -> status {Hex((ulong)wmi.SetUsbCharge(usb0.Value))}");
            Wait(1); int? back = wmi.UsbCharge(); L($"read = {back}");
            return got == want && back == usb0 ? "PASS" : "FAIL";
        });

        // ---------- 7. keyboard light timeout ----------
        Feature("Keyboard light timeout", () =>
        {
            if (!apge || kb0 == null) return "UNSUPPORTED (no APGeAction or read refused)";
            int want = kb0.Value.S == 30 ? 60 : 30;
            L($"set {want} s (brightness {kb0.Value.B}) -> status {Hex((ulong)wmi.SetKbTimeout(kb0.Value.B, want))}");
            Wait(1); var got = wmi.KbTimeout(); L($"read = {got}");
            L($"restore {kb0.Value.S} s -> status {Hex((ulong)wmi.SetKbTimeout(kb0.Value.B, kb0.Value.S))}");
            Wait(1); var back = wmi.KbTimeout(); L($"read = {back}");
            return got?.Seconds == want && back?.Seconds == kb0.Value.S ? "PASS" : "FAIL";
        });

        // ---------- 8. GPU overclock (Turbo OFF first; only Normal/Faster, Turbo level = Turbo mode) ----------
        Feature("GPU overclock", () =>
        {
            foreach (var s in wmi.SetTurbo(false)) L($"turbo off: {s.What} {(s.Skipped ? "skipped" : "status " + s.Status)}");
            Wait(2);
            L($"oc now {wmi.Misc(5)}");
            L($"set misc 5 = 1 (Faster) -> status {wmi.SetMisc(5, 1)}");
            Wait(2); int? f = wmi.Misc(5) is int a ? a & 0x0F : null; L($"read oc = {f} turbo={wmi.Misc(1)}");
            L($"set misc 5 = 0 (Normal) -> status {wmi.SetMisc(5, 0)}");
            Wait(2); int? n = wmi.Misc(5) is int b ? b & 0x0F : null; L($"read oc = {n} turbo={wmi.Misc(1)}");
            return f == 1 && n == 0 ? "PASS" : "FAIL";
        });

        // ---------- 9. custom fan speed (Turbo is OFF here) ----------
        Feature("Custom fan speed", () =>
        {
            L($"custom mode -> status {wmi.SetFanCustomMode()}");
            var res = new List<bool>();
            foreach (int pct in new[] { 60, 80, 40 })
            {
                L($"set CPU {pct}% -> {wmi.SetFanSpeed(1, pct)}, GPU {pct}% -> {wmi.SetFanSpeed(4, pct)}");
                Wait(7);
                var m = wmi.FanModes();
                L($"  mode {m.Cpu}/{m.Gpu}  rpm {wmi.Sensor(2)}/{wmi.Sensor(6)}  duty {wmi.Sensor(12)}/{wmi.Sensor(16)}  speed get {Hex(wmi.Call("GetGamingFanSpeed", 1))}/{Hex(wmi.Call("GetGamingFanSpeed", 4))}");
                res.Add(m.Cpu == 3 && m.Gpu == 3);
            }
            L($"back to Auto -> {wmi.SetFanMode(1)}");
            Wait(2);
            return res.All(x => x) ? "PASS (see RPM/duty per step)" : "FAIL (mode not Custom)";
        });

        // ---------- restore whole state ----------
        L(""); L("=== RESTORE");
        foreach (var s in wmi.SetTurbo(start.TurboFlag == 1)) L($"turbo {(start.TurboFlag == 1 ? "on" : "off")}: {s.What} {(s.Skipped ? "skipped" : "status " + s.Status)}");
        if (start.TurboFlag != 1 && start.CpuFanMode is 1 or 2) wmi.SetFanMode(start.CpuFanMode);
        if (start.GpuOverclock is int oc && ((wmi.Misc(5) ?? 0) & 0x0F) != oc && start.TurboFlag != 1) wmi.SetMisc(5, oc);
        Wait(3);
        var end = FirmwareState.Read(wmi, true);
        L($"end:   turbo={end.TurboFlag} led={end.TurboLed} oc={end.GpuOverclock} fans={end.CpuFanMode}/{end.GpuFanMode} boot={end.BootAnimation} zones={string.Join(",", end.ZoneColors)} profile={Hex(end.ProfileWord)}");
        bool same = end.TurboFlag == start.TurboFlag && end.TurboLed == start.TurboLed && end.GpuOverclock == start.GpuOverclock
            && end.CpuFanMode == start.CpuFanMode && end.GpuFanMode == start.GpuFanMode && end.BootAnimation == start.BootAnimation
            && end.ProfileWord == start.ProfileWord && end.ZoneColors.SequenceEqual(start.ZoneColors);
        L(same ? "RESTORED: end state = start state" : "WARNING: end state differs from start");
        Result["Final state = start"] = same ? "YES" : "NO";
    }
}
