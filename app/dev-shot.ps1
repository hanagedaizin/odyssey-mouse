param(
    [string]$Exe = '',
    [string]$Out = '',
    [int]$WaitMs = 2600,
    [switch]$Keep,
    [string]$ExtraArgs = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Fg {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
if ($Exe -eq '') { $Exe = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'dist\OdysseyMouse.exe' }
if ($Out -eq '') { $Out = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'obj\shot.png' }

Get-Process OdysseyMouse -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); Start-Sleep -Milliseconds 300 }
if ([string]::IsNullOrWhiteSpace($ExtraArgs)) { $p = Start-Process -FilePath $Exe -PassThru }
else { $p = Start-Process -FilePath $Exe -ArgumentList $ExtraArgs -PassThru }
Start-Sleep -Milliseconds $WaitMs

# ウィンドウハンドルができるまで最大 15 秒待つ
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 75; $i++) {
    $p.Refresh()
    $h = $p.MainWindowHandle
    if ($h -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 200
}
if ($h -eq [IntPtr]::Zero) {
    Write-Host "no main window handle"
}
else {
    [void][Fg]::ShowWindow($h, 5)
    [void][Fg]::SetForegroundWindow($h)
    Start-Sleep -Milliseconds 900
}

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap($screen.Width, $screen.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host ("screen {0}x{1} -> {2}" -f $screen.Width, $screen.Height, $Out)

if (-not $Keep) { Start-Sleep -Milliseconds 200; Get-Process OdysseyMouse -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill() } }
