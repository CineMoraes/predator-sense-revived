using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using PredatorSenseRevived.Firmware;

namespace PredatorSenseRevived;

/// <summary>Which laptop is this? The firmware codes were verified only on the Predator Helios 300 PH315-52.</summary>
public static class Machine
{
    private static string Bios(string name)
    {
        try { return Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", name, "")?.ToString()?.Trim() ?? ""; }
        catch { return ""; }
    }

    /// <summary>PSR_FAKE_MODEL lets developers test the "untested model" path on a tested laptop.</summary>
    public static string Model { get; } = Environment.GetEnvironmentVariable("PSR_FAKE_MODEL") is { Length: > 0 } fake ? fake : Bios("SystemProductName");
    public static string Maker { get; } = Bios("SystemManufacturer");
    public static string BiosVersion { get; } = Bios("BIOSVersion");
    public static bool IsTested => Model.Contains("PH315-52", StringComparison.OrdinalIgnoreCase);
    public static string AppVersion => typeof(Machine).Assembly.GetName().Version?.ToString(3) ?? "?";
}

public sealed class AppSettings
{
    public string KeyboardLayout { get; set; } = "";
    public bool UnlockUntestedModel { get; set; }

    private static string File_ => Path.Combine(AppPaths.Root, "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(File_)) ?? new AppSettings(); }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        try { Directory.CreateDirectory(AppPaths.Root); File.WriteAllText(File_, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }
}
