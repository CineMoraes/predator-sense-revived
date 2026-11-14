# Read-only probe #2: which extra firmware features exist (profiles, misc settings, keyboard RGB).
# Calls ONLY Get* methods. Changes nothing. Needs admin.
$out = Join-Path $PSScriptRoot "probe2_out.txt"
$log = New-Object System.Collections.Generic.List[string]
$i = Get-CimInstance -Namespace root/wmi -ClassName AcerGamingFunction | Select-Object -First 1
function Call($m, $inp) {
    try {
        $o = (Invoke-CimMethod -InputObject $i -MethodName $m -Arguments @{ gmInput = [uint32]$inp } -ErrorAction Stop).gmOutput
        if ($o -is [array]) { return "bytes[" + (($o | ForEach-Object { "{0:X2}" -f $_ }) -join " ") + "]" }
        if ($null -eq $o) { return "null" }
        return ("0x{0:X}" -f [uint64]$o)
    } catch { return "ERR " + $_.Exception.Message.Split("`n")[0] }
}
foreach ($k in 0..0x20) { $log.Add(("MiscSetting in=0x{0:X} -> {1}" -f $k, (Call "GetGamingMiscSetting" $k))) }
foreach ($k in 0..4) { $log.Add(("Profile in=0x{0:X} -> {1}" -f $k, (Call "GetGamingProfile" $k))) }
foreach ($k in 0..4) { $log.Add(("ProfileSetting in=0x{0:X} -> {1}" -f $k, (Call "GetGamingProfileSetting" $k))) }
foreach ($k in 0..4) { $log.Add(("KBBacklight in=0x{0:X} -> {1}" -f $k, (Call "GetGamingKBBacklight" $k))) }
foreach ($k in 0..4) { $log.Add(("RgbKb in=0x{0:X} -> {1}" -f $k, (Call "GetGamingRgbKb" $k))) }
foreach ($m in "GetGamingLED", "GetGamingLEDBehavior", "GetGamingLEDColor") { foreach ($k in 0..2) { $log.Add(("$m in=0x{0:X} -> {1}" -f $k, (Call $m $k))) } }
foreach ($k in 0x1, 0x4, 0x9) { $log.Add(("FanTable in=0x{0:X} -> {1}" -f $k, (Call "GetGamingFanTable" $k))) }
$log | Set-Content $out -Encoding utf8
