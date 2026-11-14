using System.IO;

namespace PredatorSenseRevived.Firmware;

/// <summary>--test-kbtimeout report.txt: find the write layout for the keyboard light timeout and put it back to 30 s.</summary>
public static class KbTimeoutTest
{
    public static void Run(string path)
    {
        var log = new List<string> { $"KB TIMEOUT TEST {DateTime.Now:yyyy-MM-dd HH:mm:ss} admin={App.IsAdmin()}" };
        try
        {
            using var wmi = new AcerWmi();
            if (!wmi.Connect() || !wmi.ConnectApge()) { log.Add("connect FAIL " + wmi.LastError); File.WriteAllLines(path, log); return; }
            string Raw() { ulong o = wmi.Apge("GetFunction", 0x88401); return $"0x{o:X} bytes[{string.Join(" ", Enumerable.Range(0, 8).Select(i => ((o >> (8 * i)) & 0xFF).ToString("X2")))}]"; }
            log.Add("now: " + Raw());
            if (path.Contains("steps"))
            {
                foreach (var (n, inp) in new[] { ("NEVER via brightness 0 (0, 0)", 0x88402UL), ("60 s (100, 60)", 0x88402UL | (100UL << 32) | (60UL << 40)), ("120 s (100, 120)", 0x88402UL | (100UL << 32) | (120UL << 40)), ("back to 30 s (100, 30)", 0x88402UL | (100UL << 32) | (30UL << 40)) })
                {
                    ulong st = wmi.Apge("SetFunction", inp); Thread.Sleep(800);
                    log.Add($"{n}: input 0x{inp:X} status 0x{st & 0xFF:X} -> {Raw()}");
                }
                File.WriteAllLines(path, log); return;
            }
            if (path.Contains("never"))
            {
                foreach (var (n, inp) in new[] { ("never (100, 0)", 0x88402UL | (100UL << 32)), ("never, b3 flag (100, 0, b3=1)", 0x88402UL | (1UL << 24) | (100UL << 32)), ("never (100, 255)", 0x88402UL | (100UL << 32) | (255UL << 40)) })
                {
                    ulong st = wmi.Apge("SetFunction", inp); Thread.Sleep(800);
                    log.Add($"{n}: input 0x{inp:X} status 0x{st & 0xFF:X} -> {Raw()}");
                }
                ulong r = wmi.Apge("SetFunction", 0x88402UL | (100UL << 32) | (30UL << 40)); Thread.Sleep(800);
                log.Add($"restore 30 s: status 0x{r & 0xFF:X} -> {Raw()}");
                File.WriteAllLines(path, log); return;
            }
            (string Name, ulong Input)[] tries =
            {
                ("A: bright 100 @b4, secs 30 @b5", 0x88402UL | (100UL << 32) | (30UL << 40)),
                ("B: bright 100 @b3, secs 30 @b4", 0x88402UL | (100UL << 24) | (30UL << 32)),
                ("C: secs 30 @b5 only",            0x88402UL | (30UL << 40)),
            };
            foreach (var (name, input) in tries)
            {
                ulong st = wmi.Apge("SetFunction", input);
                Thread.Sleep(800);
                ulong o = wmi.Apge("GetFunction", 0x88401);
                int secs = (int)((o >> 40) & 0xFF);
                log.Add($"{name}: input 0x{input:X} status 0x{st & 0xFF:X} -> {Raw()} secs={secs}");
                if (secs == 30) { log.Add("RESULT: layout " + name[0] + " works, timeout is 30 s again"); break; }
            }
        }
        catch (Exception ex) { log.Add("ERROR " + ex.Message); }
        File.WriteAllLines(path, log);
    }
}
