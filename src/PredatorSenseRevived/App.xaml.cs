using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace PredatorSenseRevived;

/// <summary>
/// Start modes:
///   (none)                      normal start; asks for admin (UAC) if needed, else runs read-only.
///   --render out.png [--page X] draws the window with demo values to a PNG and exits (no admin, no firmware).
///   --demo                      opens the window with demo values (no firmware).
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        string? icon = ArgValue(args, "--make-icon");
        if (icon != null) { IconMaker.Make(icon); Shutdown(); return; }
        string? startupOut = ArgValue(args, "--enable-startup");
        if (startupOut != null) { var r = PredatorSenseRevived.Startup.Enable(); System.IO.File.WriteAllText(startupOut, $"ok={r.Ok} enabled={PredatorSenseRevived.Startup.IsEnabled()} {r.Message}"); Shutdown(); return; }
        string? probe = ArgValue(args, "--probe");
        if (probe != null) { RunProbe(probe); Shutdown(); return; }
        string? kbt = ArgValue(args, "--test-kbtimeout");
        if (kbt != null) { Firmware.KbTimeoutTest.Run(kbt); Shutdown(); return; }
        string? testAll = ArgValue(args, "--test-all");
        if (testAll != null) { Firmware.TestAll.Run(testAll); Shutdown(); return; }
        string? turboTest = ArgValue(args, "--test-turbo");
        if (turboTest != null) { Firmware.TurboTest.Run(turboTest); Shutdown(); return; }
        string? selftest = ArgValue(args, "--selftest");
        if (selftest != null)
        {
            var t = new MainWindow(demo: !args.Contains("--live")) { SelfTestPath = selftest };
            MainWindow = t; t.Show(); return;
        }
        string? render = ArgValue(args, "--render");
        bool demo = render != null || args.Contains("--demo");

        if (!demo && !IsAdmin() && !args.Contains("--no-elevate"))
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--no-elevate") { UseShellExecute = true, Verb = "runas" });
                Shutdown();
                return;
            }
            catch { /* UAC cancelled: continue without admin, the window shows "no firmware access". */ }
        }

        var win = new MainWindow(demo);
        MainWindow = win;
        if (render != null) { if (ArgValue(args, "--layout") is string lay) win.SetKeyboardLayout(lay, save: false); win.RenderToPng(render, ArgValue(args, "--page") ?? "dash"); }
        else { if (args.Contains("--minimized")) win.WindowState = WindowState.Minimized; win.Show(); }
    }

    /// <summary>--probe out.txt: connect, read everything once (no writes), write the result as text.</summary>
    private static void RunProbe(string path)
    {
        using var wmi = new Firmware.AcerWmi();
        string text;
        if (!wmi.Connect()) text = $"admin={IsAdmin()} connect=FAIL error={wmi.LastError}";
        else
        {
            try { text = $"admin={IsAdmin()} connect=OK\n" + System.Text.Json.JsonSerializer.Serialize(Firmware.FirmwareState.Read(wmi, true), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }); }
            catch (Exception ex) { text = $"admin={IsAdmin()} connect=OK read=FAIL {ex}"; }
        }
        System.IO.File.WriteAllText(path, text);
    }

    public static bool IsAdmin() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
