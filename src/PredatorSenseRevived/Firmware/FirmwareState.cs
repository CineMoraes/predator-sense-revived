using System.IO;
using System.Text.Json;

namespace PredatorSenseRevived.Firmware;

/// <summary>One full read of everything we know how to read. Used for the UI, snapshots and the CSV log.</summary>
public sealed record FirmwareState
{
    public DateTime Time { get; init; } = DateTime.Now;
    public int? CpuTemp { get; init; }
    public int? GpuTemp { get; init; }
    public int? SysTemp { get; init; }
    public int? CpuRpm { get; init; }
    public int? GpuRpm { get; init; }
    public int CpuFanMode { get; init; }
    public int GpuFanMode { get; init; }
    public int? TurboFlag { get; init; }
    public int? GpuOverclock { get; init; }
    public bool? TurboLed { get; init; }
    public bool? CoolBoost { get; init; }
    public int? UsbFloor { get; init; }
    public int? KbIdleSecs { get; init; }
    public int? BootAnimation { get; init; }
    public ulong ProfileWord { get; init; }
    public string[] ZoneColors { get; init; } = Array.Empty<string>();

    public bool WinKeyEnabled => ((ProfileWord >> 24) & 0xFF) == 1;

    public static FirmwareState Read(AcerWmi wmi, bool full)
    {
        var modes = wmi.FanModes();
        var s = new FirmwareState
        {
            CpuTemp = wmi.Sensor(1), CpuRpm = wmi.Sensor(2), SysTemp = wmi.Sensor(3), GpuRpm = wmi.Sensor(6), GpuTemp = wmi.Sensor(10),
            CpuFanMode = modes.Cpu, GpuFanMode = modes.Gpu,
            // Read every time: the TURBO key changes these without telling Windows.
            TurboFlag = wmi.Misc(1), GpuOverclock = wmi.Misc(5) is int oc ? oc & 0x0F : null, TurboLed = wmi.TurboLed(),
        };
        if (!full) return s;
        var zones = new[] { 1, 2, 4, 8 }.Select(b => wmi.ZoneColor(b)).Select(c => c is { } v ? $"#{v.R:X2}{v.G:X2}{v.B:X2}" : "").ToArray();
        bool? cb = null; int? usb = null, idle = null;
        if (wmi.HasApge) { try { cb = wmi.CoolBoost(); usb = wmi.UsbCharge(); idle = wmi.KbTimeout()?.Seconds; } catch { } }
        return s with { BootAnimation = wmi.Misc(6), ProfileWord = wmi.ProfileWord(), ZoneColors = zones, CoolBoost = cb, UsbFloor = usb, KbIdleSecs = idle };
    }

    /// <summary>Values from the 2026-11-13 probes. Used by --render and when firmware access fails in demo mode.</summary>
    public static FirmwareState Demo() => new()
    {
        CpuTemp = 77, GpuTemp = 52, SysTemp = 48, CpuRpm = 6360, GpuRpm = 6420, CpuFanMode = 2, GpuFanMode = 2,
        TurboFlag = 1, GpuOverclock = 2, TurboLed = true, BootAnimation = 1, ProfileWord = 0xFF000001000000,
        ZoneColors = new[] { "#00AEC7", "#00AEC7", "#00AEC7", "#00AEC7" },
    };
}

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PredatorSenseRevived");
    public static string Snapshots => Ensure(Path.Combine(Root, "snapshots"));
    public static string Logs => Ensure(Path.Combine(Root, "logs"));
    private static string Ensure(string p) { Directory.CreateDirectory(p); return p; }
}

/// <summary>Rule: before any firmware write, save every readable value.</summary>
public static class Snapshot
{
    public static string Save(FirmwareState state, string reason)
    {
        string file = Path.Combine(AppPaths.Snapshots, $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        var doc = new { reason, state };
        File.WriteAllText(file, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        return file;
    }
}

public sealed class SensorLog
{
    private readonly string _file = Path.Combine(AppPaths.Logs, $"sensors_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    private bool _header;
    public string FileName => Path.GetFileName(_file);

    public void Append(FirmwareState s)
    {
        if (!_header) { File.AppendAllText(_file, "time,cpu_c,gpu_c,sys_c,cpu_rpm,gpu_rpm,cpu_mode,gpu_mode\r\n"); _header = true; }
        File.AppendAllText(_file, $"{s.Time:HH:mm:ss},{s.CpuTemp},{s.GpuTemp},{s.SysTemp},{s.CpuRpm},{s.GpuRpm},{s.CpuFanMode},{s.GpuFanMode}\r\n");
    }
}
