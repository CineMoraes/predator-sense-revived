using System.Runtime.InteropServices;

namespace PredatorSenseRevived.Firmware;

/// <summary>
/// Talks to the laptop firmware through WMI class root\WMI AcerGamingFunction (needs admin).
/// Uses the Windows WMI scripting COM objects, so no NuGet package is needed.
/// Codes: docs/firmware-notes.md. Status byte 0 of every output: 0 OK, 1 unsupported, 2 invalid.
/// </summary>
public sealed class AcerWmi : IDisposable
{
    private dynamic? _instance;
    private readonly object _lock = new();

    public bool IsConnected => _instance != null;
    public string LastError { get; private set; } = "";

    public bool Connect()
    {
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator")
                ?? throw new InvalidOperationException("WMI scripting is not available.");
            dynamic locator = Activator.CreateInstance(locatorType)!;
            dynamic service = locator.ConnectServer(".", "root\\WMI");
            dynamic set = service.ExecQuery("SELECT * FROM AcerGamingFunction");
            if ((int)set.Count == 0) throw new InvalidOperationException("AcerGamingFunction not found.");
            _instance = set.ItemIndex(0);
            // A first read proves we have access (non-admin gets "access denied" here).
            Call("GetGamingFanBehavior", 0x9);
            return true;
        }
        catch (Exception ex)
        {
            _instance = null;
            LastError = ex is COMException ce && (uint)ce.HResult == 0x80041003
                ? "Access denied. Run as administrator."
                : ex.Message;
            return false;
        }
    }

    /// <summary>Calls a method. Getters take uint32; most setters take uint64 (sent as string, as WMI scripting expects).</summary>
    public ulong Call(string method, ulong input, bool input64 = false)
    {
        lock (_lock)
        {
            if (_instance == null) throw new InvalidOperationException("Not connected.");
            dynamic inParams = _instance.Methods_.Item(method).InParameters.SpawnInstance_();
            inParams.Properties_.Item("gmInput").Value = input64 ? input.ToString() : (object)(int)input;
            dynamic outParams = _instance.ExecMethod_(method, inParams);
            object value = outParams.Properties_.Item("gmOutput").Value;
            return Convert.ToUInt64(value);
        }
    }

    public static int Status(ulong output) => (int)(output & 0xFF);
    public static ulong Payload(ulong output) => output >> 8;

    // ---- Reads (all verified on PH315-52, 2026-11-13) ----

    /// <summary>GetGamingSysInfo((id &lt;&lt; 8) | 1): value in bits 8..23. Null when unsupported.</summary>
    public int? Sensor(int id)
    {
        ulong o = Call("GetGamingSysInfo", ((ulong)id << 8) | 1);
        return Status(o) == 0 ? (int)((o >> 8) & 0xFFFF) : null;
    }

    /// <summary>Fan modes: 1 Auto, 2 Max, 3 Custom. CPU in bits 8-9, GPU in bits 14-15.</summary>
    public (int Cpu, int Gpu) FanModes()
    {
        ulong o = Call("GetGamingFanBehavior", 0x9);
        return ((int)((o >> 8) & 3), (int)((o >> 14) & 3));
    }

    public int? Misc(int index)
    {
        ulong o = Call("GetGamingMiscSetting", (ulong)index);
        return Status(o) == 0 ? (int)((o >> 8) & 0xFF) : null;
    }

    /// <summary>Zone bit 1, 2, 4 or 8. Returns (R, G, B) or null.</summary>
    public (byte R, byte G, byte B)? ZoneColor(int zoneBit)
    {
        ulong o = Call("GetGamingRgbKb", (ulong)zoneBit);
        if (Status(o) != 0) return null;
        return ((byte)(o >> 8), (byte)(o >> 16), (byte)(o >> 24));
    }

    public ulong ProfileWord() => Call("GetGamingProfile", 0);

    // ---- Writes ----

    /// <summary>VERIFIED: 1 = Auto (0x410009), 2 = Max (0x820009). CPU slot 0 and GPU slot 3.</summary>
    public int SetFanMode(int mode)
    {
        if (mode != 1 && mode != 2) throw new ArgumentOutOfRangeException(nameof(mode), "Only Auto (1) and Max (2) are tested.");
        ulong input = 0x9UL | ((ulong)mode << 16) | ((ulong)mode << 22);
        return Status(Call("SetGamingFanBehavior", input, input64: true));
    }

    /// <summary>GetGamingLED(1): Turbo LED, payload bits 0-1, 1 = on. Null when unsupported.</summary>
    public bool? TurboLed()
    {
        ulong o = Call("GetGamingLED", 1);
        return Status(o) == 0 ? ((o >> 8) & 3) == 1 : null;
    }

    public sealed record Step(string What, string Input, int Status, bool Skipped);

    /// <summary>
    /// NOT YET TESTED ON PH315-52. Turbo on/off, same sequence as rusty-predator (MIT) decoded from PredatorSense 3.0.3136:
    /// ON  = LED 0x10001, misc 1 = 1 (0x101), fans Max, misc 5 = 2 (0x205).
    /// OFF = LED 0x1,     misc 1 = 0 (0x001), fans Auto, misc 5 = 0 (0x005).
    /// LED and misc values are read first and written only when they differ (a same-value write can act like a Turbo key press).
    /// Stops at the first refused step.
    /// </summary>
    public List<Step> SetTurbo(bool on)
    {
        var steps = new List<Step>();
        bool Run(string what, string method, ulong input, bool needed)
        {
            if (!needed) { steps.Add(new Step(what, $"0x{input:X}", 0, true)); return true; }
            int st = Status(Call(method, input, input64: true));
            steps.Add(new Step(what, $"0x{input:X}", st, false));
            return st == 0;
        }
        bool? led = TurboLed();
        if (led != null && !Run("Turbo LED", "SetGamingLED", on ? 0x10001UL : 0x1UL, led != on)) return steps;
        int want = on ? 1 : 0;
        if (Misc(1) is int flag && !Run("Turbo flag (misc 1)", "SetGamingMiscSetting", 1UL | ((ulong)want << 8), flag != want)) return steps;
        int fan = SetFanMode(on ? 2 : 1);
        steps.Add(new Step("Fans " + (on ? "Max" : "Auto"), on ? "0x820009" : "0x410009", fan, false));
        if (fan != 0) return steps;
        int level = on ? 2 : 0;
        if (Misc(5) is int oc && !Run("GPU overclock (misc 5)", "SetGamingMiscSetting", 5UL | ((ulong)level << 8), (oc & 0x0F) != level)) return steps;
        return steps;
    }

    // ---- More features (encodings: rusty-predator, MIT). Each one is tested by --test-all before the UI uses it. ----

    private dynamic? _apge;
    public bool HasApge => _apge != null;

    /// <summary>Second class: APGeAction (CoolBoost, USB charging when off, keyboard light timeout). Absent on some models.</summary>
    public bool ConnectApge()
    {
        try
        {
            dynamic locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!)!;
            dynamic set = locator.ConnectServer(".", "root\\WMI").ExecQuery("SELECT * FROM APGeAction");
            if ((int)set.Count == 0) return false;
            _apge = set.ItemIndex(0);
            return true;
        }
        catch { _apge = null; return false; }
    }

    /// <summary>APGeAction GetFunction (uint32 in) / SetFunction (uint64 in, as string). Status byte 0: 0 OK, 0xE1/0xE2 = error.</summary>
    public ulong Apge(string method, ulong input)
    {
        lock (_lock)
        {
            if (_apge == null) throw new InvalidOperationException("APGeAction not connected.");
            dynamic inParams = _apge.Methods_.Item(method).InParameters.SpawnInstance_();
            inParams.Properties_.Item("uiInput").Value = method == "SetFunction" ? input.ToString() : (object)unchecked((int)input);
            dynamic outParams = _apge.ExecMethod_(method, inParams);
            return Convert.ToUInt64(outParams.Properties_.Item("uiOutput").Value);
        }
    }

    /// <summary>Call with a byte array input (SetGamingKBBacklight takes uint8[16]).</summary>
    public ulong CallBytes(string method, byte[] input)
    {
        lock (_lock)
        {
            if (_instance == null) throw new InvalidOperationException("Not connected.");
            dynamic inParams = _instance.Methods_.Item(method).InParameters.SpawnInstance_();
            inParams.Properties_.Item("gmInput").Value = input;
            dynamic outParams = _instance.ExecMethod_(method, inParams);
            return Convert.ToUInt64(outParams.Properties_.Item("gmOutput").Value);
        }
    }

    // Keyboard
    public int SetZoneColor(int zoneMask, byte r, byte g, byte b) =>
        Status(Call("SetGamingRgbKb", (ulong)zoneMask | ((ulong)r << 8) | ((ulong)g << 16) | ((ulong)b << 24)));

    /// <summary>effect 0 Static, 1 Breathing, 2 Neon, 3 Wave, 4 Shifting, 5 Zoom. Payload [effect, speed, brightness, pattern, direction, R, G, B] twice.</summary>
    public int SetKeyboardEffect(int effect, int speed, int brightness, int direction, byte r, byte g, byte b)
    {
        bool color = effect is 1 or 2 or 5, dir = effect is 3 or 5;
        var p = new byte[16];
        p[0] = (byte)effect; p[1] = (byte)(effect == 0 ? 0 : Math.Clamp(speed, 1, 9)); p[2] = (byte)Math.Clamp(brightness, 0, 100);
        p[3] = (byte)(effect is 3 or 4 ? 0x08 : 0); p[4] = (byte)(dir ? direction : 0);
        if (color) { p[5] = r; p[6] = g; p[7] = b; }
        Array.Copy(p, 0, p, 8, 8);
        return Status(CallBytes("SetGamingKBBacklight", p));
    }

    // Misc settings: write only when different (rule).
    public int SetMisc(int index, int value) => Status(Call("SetGamingMiscSetting", (ulong)index | ((ulong)value << 8), input64: true));

    // Profile word: Win key (byte 3).
    public bool? WinKey() { ulong o = ProfileWord(); return Status(o) == 0 ? ((o >> 24) & 0xFF) == 1 : null; }
    public int SetWinKey(bool on) => Status(Call("SetGamingProfile", 0x2UL | ((on ? 1UL : 0UL) << 24), input64: true));

    // Custom fan speed: firmware % (not duty %). Fan id 1 = CPU, 4 = GPU (slot 3). Custom mode = 0xC30009.
    public int SetFanCustomMode() => Status(Call("SetGamingFanBehavior", 0x9UL | (3UL << 16) | (3UL << 22), input64: true));
    public int SetFanSpeed(int fanId, int firmwarePct) => Status(Call("SetGamingFanSpeed", (ulong)fanId | ((ulong)Math.Clamp(firmwarePct, 20, 100) << 8), input64: true));

    // APGeAction features
    public bool? CoolBoost() { ulong o = Apge("GetFunction", 0x207); return (o & 0xFF) == 0 ? ((o >> 8) & 0xFF) == 1 : null; }
    public int SetCoolBoost(bool on) => (int)(Apge("SetFunction", 7UL | ((on ? 1UL : 0UL) << 16)) & 0xFF);

    /// <summary>USB charging when off: 0 = off, else battery floor %.</summary>
    public int? UsbCharge() { ulong o = Apge("GetFunction", 0x204); if ((o & 0xFF) != 0) return null; return ((o >> 8) & 0xFF) == 0x0F ? (int)((o >> 16) & 0xFF) : 0; }
    public int SetUsbCharge(int floor) => (int)(Apge("SetFunction", floor == 0 ? 0x04UL | (0x1FUL << 8) | (10UL << 16) : 0x04UL | (0x0FUL << 8) | ((ulong)floor << 16)) & 0xFF);

    /// <summary>Keyboard light: (brightness, idle seconds before off; 0 = never).</summary>
    public (int Brightness, int Seconds)? KbTimeout() { ulong o = Apge("GetFunction", 0x88401); if ((o & 0xFF) != 0) return null; return ((int)((o >> 32) & 0xFF), (int)((o >> 40) & 0xFF)); }
    /// <summary>PH315-52: brightness must be non-zero (0 makes the firmware reset to 100 / never). Read-back brightness byte is not reliable.</summary>
    /// <summary>VERIFIED 14-11: PH315-52 has only ON (fixed 30 s) or NEVER. ON = 0x88402|100&lt;&lt;32|30&lt;&lt;40, NEVER = 0x88402 (brightness byte 0). Other seconds are ignored.</summary>
    public int SetKbIdleOff(bool on) => (int)(Apge("SetFunction", on ? 0x88402UL | (100UL << 32) | (30UL << 40) : 0x88402UL) & 0xFF);
    public int SetKbTimeout(int brightness, int seconds) => brightness <= 0 ? throw new ArgumentOutOfRangeException(nameof(brightness)) : (int)(Apge("SetFunction", 0x88402UL | ((ulong)brightness << 32) | ((ulong)seconds << 40)) & 0xFF);

    public void Dispose()
    {
        if (_instance != null) Marshal.FinalReleaseComObject((object)_instance);
        _instance = null;
    }
}
