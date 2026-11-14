# Read-only probe of the Acer gaming firmware interface (AcerGamingFunction).
# Calls ONLY Get* methods. Changes nothing. Needs admin.
$out = Join-Path $PSScriptRoot "probe_out.txt"
$log = New-Object System.Collections.Generic.List[string]
function Hex($v) { if ($null -eq $v) { "null" } elseif ($v -is [string]) { $v } else { "0x{0:X}" -f [uint64]$v } }
try {
    $i = Get-CimInstance -Namespace root/wmi -ClassName AcerGamingFunction -ErrorAction Stop | Select-Object -First 1
    $log.Add("Instance: $($i.InstanceName) Active=$($i.Active)")
} catch { $log.Add("INSTANCE ERROR: $($_.Exception.Message)"); $log | Set-Content $out; exit 1 }

function Call($method, $inp) {
    try {
        $r = Invoke-CimMethod -InputObject $i -MethodName $method -Arguments @{ gmInput = [uint32]$inp } -ErrorAction Stop
        return $r.gmOutput
    } catch { return "ERR " + $_.Exception.Message.Split("`n")[0] }
}

foreach ($sample in 1..2) {
    $log.Add("=== sample $sample $(Get-Date -Format HH:mm:ss)")
    foreach ($cmd in 0x01, 0x02) {
        foreach ($id in 0..15) {
            $inp = $cmd -bor ($id -shl 8)
            $o = Call "GetGamingSysInfo" $inp
            if ($o -is [string]) { $log.Add("SysInfo in=$(Hex $inp) -> $o"); continue }
            $st = [uint64]$o -band 0xFF; $val = ([uint64]$o -shr 8) -band 0xFFFF
            $log.Add("SysInfo in=$(Hex $inp) -> $(Hex $o) status=$st value=$val")
        }
    }
    if ($sample -eq 1) { Start-Sleep -Seconds 3 }
}
foreach ($inp in 0x0, 0x1, 0x8, 0x9) { $log.Add("FanBehavior in=$(Hex $inp) -> $(Hex (Call 'GetGamingFanBehavior' $inp))") }
foreach ($inp in 0x1, 0x4, 0x8) { $log.Add("FanSpeed in=$(Hex $inp) -> $(Hex (Call 'GetGamingFanSpeed' $inp))") }
foreach ($inp in 0x0, 0x1) { $log.Add("Profile in=$(Hex $inp) -> $(Hex (Call 'GetGamingProfile' $inp))") }
$log | Set-Content $out -Encoding utf8
