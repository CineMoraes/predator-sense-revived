using System.IO;
using System.Text;
using PredatorSenseRevived.Firmware;

namespace PredatorSenseRevived;

/// <summary>Small text log of what the app did (every message it showed). Kept under 300 KB.</summary>
public static class AppLog
{
    private static readonly object Gate = new();
    public static string FilePath => Path.Combine(AppPaths.Logs, "app.log");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 300_000) File.WriteAllLines(FilePath, File.ReadAllLines(FilePath).TakeLast(800));
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}\r\n");
            }
        }
        catch { }
    }
}

public static class SupportFile
{
    /// <summary>Removes the Windows user name and the PC name, in case a message or path carried them.</summary>
    public static string Scrub(string text)
    {
        foreach (var (secret, mask) in new[] { (Environment.UserName, "<user>"), (Environment.MachineName, "<pc>") })
            if (secret.Length >= 3) text = text.Replace(secret, mask, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    private static string Tail(string file, int lines)
    {
        try { return File.Exists(file) ? string.Join("\r\n", File.ReadAllLines(file).TakeLast(lines)) : "(none)"; }
        catch (Exception e) { return "(cannot read: " + e.Message + ")"; }
    }

    public static string Save(string modelReport)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Predator Sense Revived {Machine.AppVersion} - support file, {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine("Contains: firmware report (read only), app log, last firmware snapshot, last sensor readings.");
        sb.AppendLine("Does not contain: user name, PC name, serial number, files or network data.");
        sb.AppendLine().AppendLine("===== MODEL REPORT =====").AppendLine(modelReport);
        sb.AppendLine("===== APP LOG (last 300 lines) =====").AppendLine(Tail(AppLog.FilePath, 300));
        string? snap = Directory.Exists(AppPaths.Snapshots) ? Directory.GetFiles(AppPaths.Snapshots, "*.json").OrderBy(f => f).LastOrDefault() : null;
        sb.AppendLine().AppendLine("===== LAST SNAPSHOT =====").AppendLine(snap == null ? "(none)" : Tail(snap, 60));
        string? csv = Directory.GetFiles(AppPaths.Logs, "sensors_*.csv").OrderBy(f => f).LastOrDefault();
        sb.AppendLine().AppendLine("===== SENSORS (last 60 lines) =====").AppendLine(csv == null ? "(none)" : Tail(csv, 60));
        string name = $"support_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        File.WriteAllText(Path.Combine(AppPaths.Root, name), Scrub(sb.ToString()));
        return name;
    }
}
