# Predator Control - replacement for PredatorSense fan control on Acer PH315-52.
# Talks to the laptop firmware (WMI class AcerGamingFunction). Needs admin.
# Only uses: temperature/fan reads, and fan mode Auto / Max. No custom curves, no BIOS, no overclock.

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSCommandPath`""
    exit
}

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

try {
    $wmi = Get-CimInstance -Namespace root/wmi -ClassName AcerGamingFunction -ErrorAction Stop | Select-Object -First 1
} catch {
    [System.Windows.Forms.MessageBox]::Show("Cannot reach the Acer firmware interface.`n$($_.Exception.Message)", "Predator Control") | Out-Null
    exit 1
}

# Sensor ids for GetGamingSysInfo (command 0x01). Verified on PH315-52 on 2026-11-13.
$S_CPU_TEMP = 0x01; $S_CPU_FAN = 0x02; $S_SYS_TEMP = 0x03; $S_GPU_FAN = 0x06; $S_GPU_TEMP = 0x0A
$MODE_NAME = @{ 0 = "?"; 1 = "Auto"; 2 = "Max"; 3 = "Custom" }

function Get-Sensor([int]$sensor) {
    $r = Invoke-CimMethod -InputObject $wmi -MethodName GetGamingSysInfo -Arguments @{ gmInput = [uint32](0x01 -bor ($sensor -shl 8)) }
    $o = [uint64]$r.gmOutput
    if (($o -band 0xFF) -ne 0) { return $null }
    return [int](($o -shr 8) -band 0xFFFF)
}
function Get-FanModes {
    $o = [uint64](Invoke-CimMethod -InputObject $wmi -MethodName GetGamingFanBehavior -Arguments @{ gmInput = [uint32]0x9 }).gmOutput
    return @{ CPU = [int](($o -shr 8) -band 3); GPU = [int](($o -shr 14) -band 3) }
}
function Set-FanMode([int]$mode) {
    # Fans: CPU = bit 0, GPU = bit 3. Mode: CPU at bits 16-17, GPU at bits 22-23. 1 = Auto, 2 = Max.
    $inp = [uint64](0x9 -bor ($mode -shl 16) -bor ($mode -shl 22))
    $r = Invoke-CimMethod -InputObject $wmi -MethodName SetGamingFanBehavior -Arguments @{ gmInput = $inp }
    return [uint32]$r.gmOutput
}

# Log file
$logDir = Join-Path $PSScriptRoot "logs"
New-Item -ItemType Directory -Force $logDir | Out-Null
$logFile = Join-Path $logDir ("sensors_{0}.csv" -f (Get-Date -Format "yyyyMMdd_HHmmss"))
[IO.File]::WriteAllText($logFile, "time,cpu_c,gpu_c,sys_c,cpu_rpm,gpu_rpm,cpu_mode,gpu_mode`r`n")

# Window
$bg = [Drawing.Color]::FromArgb(24, 24, 28); $fg = [Drawing.Color]::FromArgb(235, 235, 240)
$form = New-Object Windows.Forms.Form
$form.Text = "Predator Control"; $form.Size = New-Object Drawing.Size(380, 400)
$form.FormBorderStyle = "FixedSingle"; $form.MaximizeBox = $false; $form.StartPosition = "CenterScreen"
$form.BackColor = $bg; $form.ForeColor = $fg; $form.Font = New-Object Drawing.Font("Segoe UI", 11)

$rows = [ordered]@{ cpu = "CPU temp"; gpu = "GPU temp"; sys = "System temp"; cpufan = "CPU fan"; gpufan = "GPU fan"; mode = "Fan mode" }
$val = @{}; $y = 15
foreach ($k in $rows.Keys) {
    $l = New-Object Windows.Forms.Label; $l.Text = $rows[$k]; $l.Location = New-Object Drawing.Point(20, $y); $l.AutoSize = $true
    $v = New-Object Windows.Forms.Label; $v.Text = "..."; $v.Location = New-Object Drawing.Point(190, $y); $v.AutoSize = $true
    $v.Font = New-Object Drawing.Font("Segoe UI", 11, [Drawing.FontStyle]::Bold)
    $form.Controls.AddRange(@($l, $v)); $val[$k] = $v; $y += 32
}

function New-Btn($text, $x) {
    $b = New-Object Windows.Forms.Button; $b.Text = $text; $b.Size = New-Object Drawing.Size(150, 42)
    $b.Location = New-Object Drawing.Point($x, ($y + 8)); $b.FlatStyle = "Flat"; $b.BackColor = [Drawing.Color]::FromArgb(45, 45, 55)
    $form.Controls.Add($b); return $b
}
$btnAuto = New-Btn "Fans: Auto" 20
$btnMax = New-Btn "Fans: Max" 190

$chk = New-Object Windows.Forms.CheckBox; $chk.Text = "Log to CSV (every 2 s)"; $chk.Checked = $true; $chk.AutoSize = $true
$chk.Location = New-Object Drawing.Point(20, ($y + 62)); $form.Controls.Add($chk)
$status = New-Object Windows.Forms.Label; $status.AutoSize = $true; $status.Location = New-Object Drawing.Point(20, ($y + 92))
$status.ForeColor = [Drawing.Color]::FromArgb(160, 160, 170); $status.Font = New-Object Drawing.Font("Segoe UI", 9)
$status.Text = "Log: logs\" + (Split-Path $logFile -Leaf); $form.Controls.Add($status)

function TempColor($t) {
    if ($null -eq $t) { return $fg }
    if ($t -ge 90) { return [Drawing.Color]::FromArgb(255, 90, 90) }
    if ($t -ge 80) { return [Drawing.Color]::FromArgb(255, 170, 60) }
    return [Drawing.Color]::FromArgb(120, 220, 140)
}

function Update-View {
    try {
        $c = Get-Sensor $S_CPU_TEMP; $g = Get-Sensor $S_GPU_TEMP; $s = Get-Sensor $S_SYS_TEMP
        $cf = Get-Sensor $S_CPU_FAN; $gf = Get-Sensor $S_GPU_FAN; $m = Get-FanModes
        $val.cpu.Text = "$c $([char]0xB0)C"; $val.cpu.ForeColor = TempColor $c
        $val.gpu.Text = "$g $([char]0xB0)C"; $val.gpu.ForeColor = TempColor $g
        $val.sys.Text = "$s $([char]0xB0)C"; $val.sys.ForeColor = TempColor $s
        $val.cpufan.Text = "$cf RPM"; $val.gpufan.Text = "$gf RPM"
        $val.mode.Text = "CPU $($MODE_NAME[$m.CPU]) / GPU $($MODE_NAME[$m.GPU])"
        if ($chk.Checked) {
            [IO.File]::AppendAllText($logFile, "$(Get-Date -Format 'HH:mm:ss'),$c,$g,$s,$cf,$gf,$($m.CPU),$($m.GPU)`r`n")
        }
    } catch { $status.Text = "Read error: " + $_.Exception.Message.Split("`n")[0] }
}

function Apply-Mode([int]$mode) {
    try {
        $rc = Set-FanMode $mode
        if ($rc -eq 0) { $status.Text = "Fan mode set: $($MODE_NAME[$mode])" }
        else { $status.Text = "Firmware refused the change (code $rc)" }
    } catch { $status.Text = "Set error: " + $_.Exception.Message.Split("`n")[0] }
    Update-View
}
$btnAuto.Add_Click({ Apply-Mode 1 })
$btnMax.Add_Click({ Apply-Mode 2 })

$timer = New-Object Windows.Forms.Timer; $timer.Interval = 2000
$timer.Add_Tick({ Update-View })
$form.Add_Shown({ Update-View; $timer.Start() })
$form.Add_FormClosing({ $timer.Stop() })
[void]$form.ShowDialog()
