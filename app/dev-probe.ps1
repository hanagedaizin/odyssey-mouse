param(
    [int]$Tab = -1,
    [int]$Scroll = 0,
    [string]$Out = '',
    [string]$Lang = '',
    [int]$WaitMs = 2500,
    [string]$Exe = '',
    [string]$ProcName = 'OdysseyMouse',
    [string]$InvokeButton = '',
    [int]$SettleMs = 600,
    [string]$ExtraArgs = '',
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Ux {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = if ($Exe -ne '') { $Exe } else { Join-Path $dir 'dist\OdysseyMouse.exe' }
if ($Out -eq '') {
    $tag = if ($InvokeButton -ne '') { '_inv' } elseif ($Tab -ge 0) { "t$Tab" } else { 'x' }
    if ($Scroll -ne 0) { $tag += "_s$Scroll" }
    $Out = Join-Path $dir "obj\shot$tag.png"
}
if ($Lang -ne '') {
    $d = Join-Path $env:APPDATA 'OdysseyMouse'
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null }
    $ini = Join-Path $d 'settings.ini'
    if (Test-Path $ini) {
        $t = [IO.File]::ReadAllText($ini)
        if ($t -match '(?m)^lang=') { $t = [Text.RegularExpressions.Regex]::Replace($t, '(?m)^lang=.*$', "lang=$Lang") }
        else { $t = "lang=$Lang`r`n" + $t }
        [IO.File]::WriteAllText($ini, $t, (New-Object Text.UTF8Encoding($false)))
    }
    else { [IO.File]::WriteAllText($ini, "lang=$Lang`r`n", (New-Object Text.UTF8Encoding($false))) }
}

Get-Process $ProcName -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); Start-Sleep -Milliseconds 400 }
$arg = if ($Tab -ge 0) { "/tab=$Tab" } elseif ($ExtraArgs.Trim().Length -gt 0) { $ExtraArgs.Trim() } else { '' }
Write-Host ("launch args = [{0}]" -f $arg)
if ($arg -eq '') { $p = Start-Process -FilePath $exe -PassThru } else { $p = Start-Process -FilePath $exe -ArgumentList $arg -PassThru }
Start-Sleep -Milliseconds $WaitMs

$h = [IntPtr]::Zero
for ($i = 0; $i -lt 75; $i++) {
    $p.Refresh()
    $h = $p.MainWindowHandle
    if ($h -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 200
}
if ($h -eq [IntPtr]::Zero) { throw 'no main window' }
[void][Ux]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 600

$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$r = New-Object Ux+RECT
[void][Ux]::GetWindowRect($h, [ref]$r)
Write-Host ("rect L={0} T={1} R={2} B={3}" -f $r.L, $r.T, $r.R, $r.B)

if ($InvokeButton -ne '') {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $btns = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    $found = $null
    for ($i = 0; $i -lt $btns.Count; $i++) {
        $n = $btns[$i].Current.Name
        Write-Host ("  button[{0}] = '{1}'" -f $i, $n)
        if ($n -and ($n -ieq $InvokeButton -or $n -like ($InvokeButton + '*'))) { $found = $btns[$i] }
    }
    if (-not $found) { throw "button '$InvokeButton' not found" }
    Write-Host ("  invoking '{0}'" -f $found.Current.Name)
    $ip = $null
    try { $ip = $found.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern) } catch { $ip = $null }
    if ($ip -eq $null) { throw 'button has no Invoke pattern' }
    ([System.Windows.Automation.InvokePattern]$ip).Invoke()
    Start-Sleep -Milliseconds $SettleMs
}

if ($Scroll -ne 0) {
    [void][Ux]::SetCursorPos([int](($r.L + $r.R) / 2), [int](($r.T + $r.B) / 2))
    Start-Sleep -Milliseconds 200
    for ($i = 0; $i -lt [Math]::Abs($Scroll); $i++) {
        [Ux]::mouse_event(0x0800, 0, 0, [uint32]$(if ($Scroll -gt 0) { 120 } else { -120 }), [IntPtr]::Zero)
        Start-Sleep -Milliseconds 60
    }
    Start-Sleep -Milliseconds 400
}

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap($screen.Width, $screen.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host ("-> " + $Out)

if (-not $Keep) { Start-Sleep -Milliseconds 200; Get-Process $ProcName -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() } }
