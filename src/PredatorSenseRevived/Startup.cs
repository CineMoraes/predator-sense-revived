using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace PredatorSenseRevived;

/// <summary>
/// Start with Windows: a Task Scheduler task at logon with highest privileges, so the app starts as admin without a UAC prompt.
/// Runs on battery too. Starts the app minimized.
/// </summary>
public static class Startup
{
    public const string TaskName = "Predator Sense Revived";

    private static (int Code, string Output) Schtasks(string args)
    {
        var p = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
        string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, o.Trim());
    }

    /// <summary>
    /// The task runs the exe as admin at logon. If the exe is in a folder that normal programs can write to,
    /// any program could replace it and get admin rights. Program Files is protected by Windows.
    /// </summary>
    public static bool ExeInProtectedFolder()
    {
        string exe = Environment.ProcessPath ?? "";
        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath).Any(pf => pf != "" && exe.StartsWith(pf + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsEnabled() => Schtasks($"/Query /TN \"{TaskName}\"").Code == 0;

    public static (bool Ok, string Message) Enable()
    {
        string exe = Environment.ProcessPath!;
        string user = WindowsIdentity.GetCurrent().Name;
        string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Starts Predator Sense Revived at logon (admin, minimized).</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{System.Security.SecurityElement.Escape(user)}</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>{System.Security.SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author""><Exec><Command>{System.Security.SecurityElement.Escape(exe)}</Command><Arguments>--minimized --no-elevate</Arguments></Exec></Actions>
</Task>";
        string tmp = Path.Combine(Path.GetTempPath(), $"psr_task_{Guid.NewGuid():N}.xml");
        File.WriteAllText(tmp, xml, System.Text.Encoding.Unicode);
        var r = Schtasks($"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F");
        File.Delete(tmp);
        return (r.Code == 0 && IsEnabled(), r.Output);
    }

    public static (bool Ok, string Message) Disable()
    {
        var r = Schtasks($"/Delete /TN \"{TaskName}\" /F");
        return (r.Code == 0 && !IsEnabled(), r.Output);
    }
}
