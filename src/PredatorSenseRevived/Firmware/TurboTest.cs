using System.IO;
using System.Runtime.InteropServices;

namespace PredatorSenseRevived.Firmware;

/// <summary>
/// --test-turbo report.txt (admin): supervised write test for Turbo on/off.
/// Snapshot -> switch Turbo to the opposite state -> wait -> read back -> switch back -> wait -> read back -> compare with start.
/// Stops before any write when the laptop is on battery.
/// </summary>
public static class TurboTest
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus { public byte AcLine, Battery, Percent, Saver; public int LifeTime, FullLifeTime; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus s);

    public static bool OnAc() => GetSystemPowerStatus(out var ps) && ps.AcLine == 1;

    public static void Run(string path)
    {
        var log = new List<string> { $"TURBO TEST {DateTime.Now:yyyy-MM-dd HH:mm:ss}  admin={App.IsAdmin()}" };
        try { Body(log); }
        catch (Exception ex) { log.Add("EXCEPTION " + ex); }
        File.WriteAllLines(path, log);
    }

    private static string Line(FirmwareState s) =>
        $"turbo={s.TurboFlag} led={s.TurboLed} gpuOc={s.GpuOverclock} fans={s.CpuFanMode}/{s.GpuFanMode} rpm={s.CpuRpm}/{s.GpuRpm} cpu={s.CpuTemp}C gpu={s.GpuTemp}C";

    private static bool Expect(FirmwareState s, bool on) =>
        s.TurboFlag == (on ? 1 : 0) && (s.TurboLed == null || s.TurboLed == on) && s.GpuOverclock == (on ? 2 : 0) && s.CpuFanMode == (on ? 2 : 1) && s.GpuFanMode == (on ? 2 : 1);

    private static void Body(List<string> log)
    {
        using var wmi = new AcerWmi();
        if (!wmi.Connect()) { log.Add("connect FAIL: " + wmi.LastError); return; }
        GetSystemPowerStatus(out var ps);
        log.Add($"power: AC={(ps.AcLine == 1 ? "yes" : "NO")} battery={ps.Percent}%");
        if (ps.AcLine != 1) { log.Add("STOP: on battery. No writes made."); return; }

        var start = FirmwareState.Read(wmi, true);
        log.Add("snapshot: " + Path.GetFileName(Snapshot.Save(start, "before Turbo test")));
        log.Add("START   " + Line(start));
        bool startOn = start.TurboFlag == 1;

        foreach (bool target in new[] { !startOn, startOn })
        {
            log.Add("");
            log.Add($"SET TURBO {(target ? "ON" : "OFF")}");
            foreach (var st in wmi.SetTurbo(target))
                log.Add($"  {st.What,-24} input={st.Input,-9} {(st.Skipped ? "skipped (already right)" : st.Status == 0 ? "OK" : "REFUSED status " + st.Status)}");
            for (int t = 2; t <= 8; t += 2)
            {
                Thread.Sleep(2000);
                log.Add($"  +{t}s  " + Line(FirmwareState.Read(wmi, false)));
            }
            var after = FirmwareState.Read(wmi, true);
            log.Add($"  RESULT {(Expect(after, target) ? "MATCHES" : "DIFFERS FROM")} expected Turbo {(target ? "ON" : "OFF")}");
        }

        var end = FirmwareState.Read(wmi, true);
        bool same = end.TurboFlag == start.TurboFlag && end.TurboLed == start.TurboLed && end.GpuOverclock == start.GpuOverclock && end.CpuFanMode == start.CpuFanMode && end.GpuFanMode == start.GpuFanMode;
        log.Add("");
        log.Add("END     " + Line(end));
        log.Add(same ? "RESTORED: end state = start state" : "WARNING: end state differs from start");
    }
}
