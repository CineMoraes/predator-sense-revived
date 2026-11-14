using System.Text;

namespace PredatorSenseRevived.Firmware;

/// <summary>
/// Read-only dump of everything the firmware answers (Get* calls only, nothing is changed).
/// Owners of other models attach it to a GitHub "model report" so their laptop can be supported.
/// </summary>
public static class ModelReport
{
    public static string Build(AcerWmi wmi)
    {
        var sb = new StringBuilder();
        void L(string s) => sb.AppendLine(s);
        string Gm(string method, ulong input)
        {
            try { return $"0x{wmi.Call(method, input):X}"; } catch (Exception e) { return "ERR " + e.Message.Split('\n')[0].Trim(); }
        }
        string Ap(ulong input)
        {
            try { return $"0x{wmi.Apge("GetFunction", input):X}"; } catch (Exception e) { return "ERR " + e.Message.Split('\n')[0].Trim(); }
        }

        L($"Predator Sense Revived {Machine.AppVersion} - model report (read only)");
        L($"Maker: {Machine.Maker}");
        L($"Model: {Machine.Model}");
        L($"BIOS:  {Machine.BiosVersion}");
        L($"OS:    {Environment.OSVersion.VersionString}");
        L("");
        L("[GetGamingSysInfo] input (id << 8) | 1");
        for (int id = 0; id <= 17; id++) L($"  id {id,2}: {Gm("GetGamingSysInfo", ((ulong)id << 8) | 1)}");
        L($"  input 0x0: {Gm("GetGamingSysInfo", 0)}");
        L($"  input 0x2: {Gm("GetGamingSysInfo", 2)}");
        L("[GetGamingFanBehavior]");
        foreach (ulong i in new ulong[] { 0x1, 0x2, 0x4, 0x8, 0x10, 0x9, 0x19 }) L($"  0x{i:X}: {Gm("GetGamingFanBehavior", i)}");
        L("[GetGamingFanSpeed]");
        foreach (ulong i in new ulong[] { 1, 2, 3, 4, 5 }) L($"  fan {i}: {Gm("GetGamingFanSpeed", i)}");
        L("[GetGamingFanTable]");
        L($"  0x1: {Gm("GetGamingFanTable", 1)}");
        L("[GetGamingMiscSetting]");
        for (int i = 0; i <= 0x10; i++) L($"  0x{i:X}: {Gm("GetGamingMiscSetting", (ulong)i)}");
        L("[GetGamingProfile]");
        L($"  0x0: {Gm("GetGamingProfile", 0)}");
        L("[GetGamingLED]");
        L($"  0x1: {Gm("GetGamingLED", 1)}");
        L("[GetGamingRgbKb] zone bits");
        foreach (ulong z in new ulong[] { 1, 2, 4, 8 }) L($"  zone {z}: {Gm("GetGamingRgbKb", z)}");
        L("[APGeAction GetFunction]");
        if (!wmi.HasApge && !wmi.ConnectApge()) L("  class not found");
        else foreach (ulong i in new ulong[] { 0x7, 0x207, 0x204, 0x88401 }) L($"  0x{i:X}: {Ap(i)}");
        return sb.ToString();
    }
}
