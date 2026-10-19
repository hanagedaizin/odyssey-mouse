# build.ps1 - builds OdysseyMouse.exe and OdysseyMouseSetup.exe
# Requires only the .NET Framework 4.x and a C# compiler (Roslyn csc from the
# Visual Studio Build Tools, or the framework csc.exe as a fallback).
param(
    [switch]$SkipInstaller,
    [switch]$SkipIcon
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root 'dist'
$obj = Join-Path $root 'obj'

function Write-Step($t) { Write-Host ""; Write-Host ("=== " + $t + " ===") -ForegroundColor Cyan }

# ---------------------------------------------------------------------------
Write-Step "compiler"
$fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path $fx)) { $fx = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$wpf = Join-Path $fx 'WPF'

$csc = $null
$cands = @()
$vsRoot = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio'
if (Test-Path $vsRoot) {
    $cands += (Get-ChildItem $vsRoot -Filter csc.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'Roslyn' } | Select-Object -ExpandProperty FullName)
}
$cands += (Join-Path $fx 'csc.exe')
foreach ($c in $cands) { if ($c -and (Test-Path $c)) { $csc = $c; break } }
if (-not $csc) { throw "C# compiler (csc.exe) not found." }
Write-Host ("csc : " + $csc)
Write-Host ("fx  : " + $fx)
& $csc /nologo -version 2>&1 | Select-Object -First 1 | ForEach-Object { Write-Host ("ver : " + $_) }

$refs = @(
    (Join-Path $fx 'mscorlib.dll'),
    (Join-Path $fx 'System.dll'),
    (Join-Path $fx 'System.Core.dll'),
    (Join-Path $fx 'System.Drawing.dll'),
    (Join-Path $fx 'System.Windows.Forms.dll'),
    (Join-Path $fx 'System.Xaml.dll'),
    (Join-Path $fx 'System.Xml.dll'),
    (Join-Path $wpf 'PresentationFramework.dll'),
    (Join-Path $wpf 'PresentationCore.dll'),
    (Join-Path $wpf 'WindowsBase.dll')
)
foreach ($r in $refs) { if (-not (Test-Path $r)) { throw "missing reference: $r" } }

# ---------------------------------------------------------------------------
Write-Step "version"
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
if (-not (Test-Path $obj)) { New-Item -ItemType Directory -Path $obj | Out-Null }
$ver = '1.0.0'
$verFile = Join-Path $root 'version.txt'
if (Test-Path $verFile) { $ver = (Get-Content $verFile -Raw).Trim() }
$ver4 = $ver
while (($ver4 -split '\.').Count -lt 4) { $ver4 += '.0' }
Write-Host ("version: " + $ver)
$ai = @"
using System.Reflection;
[assembly: AssemblyTitle("Odyssey Mouse")]
[assembly: AssemblyProduct("Odyssey Mouse")]
[assembly: AssemblyDescription("Use a Samsung Odyssey (Windows Mixed Reality) motion controller as a mouse")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyCopyright("")]
[assembly: AssemblyVersion("$ver4")]
[assembly: AssemblyFileVersion("$ver4")]
"@
$aiPath = Join-Path $obj 'AssemblyInfo.cs'
[System.IO.File]::WriteAllText($aiPath, $ai, (New-Object System.Text.UTF8Encoding($false)))

# ---------------------------------------------------------------------------
Write-Step "icon"
$ico = Join-Path $obj 'app.ico'
if ($SkipIcon -and (Test-Path $ico)) { Write-Host "skipped (existing)" }
else { & (Join-Path $root 'make-icon.ps1') -Out $ico }

# ---------------------------------------------------------------------------
Write-Step "OdysseyMouse.exe"
$srcs = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
if ($srcs.Count -eq 0) { throw "no source files in src\" }
$appExe = Join-Path $dist 'OdysseyMouse.exe'
$cscArgs = New-Object System.Collections.ArrayList
foreach ($a in @('/nologo', '/target:winexe', '/platform:anycpu', '/langversion:latest', '/optimize+', '/codepage:65001', '/warn:2',
                 ('/win32icon:' + $ico), ('/out:' + $appExe), ('/resource:' + $ico + ',OdysseyMouse.app.ico'), $aiPath)) {
    [void]$cscArgs.Add($a)
}
foreach ($r in $refs) { [void]$cscArgs.Add('/r:' + $r) }
foreach ($s in $srcs) { [void]$cscArgs.Add($s) }
& $csc $cscArgs.ToArray()
if ($LASTEXITCODE -ne 0) { throw "app build failed (exit $LASTEXITCODE)" }
Write-Host ("ok: " + $appExe + "  " + ("{0:N0}" -f (Get-Item $appExe).Length) + " bytes")

# ---------------------------------------------------------------------------
if (-not $SkipInstaller) {
    Write-Step "OdysseyMouseSetup.exe"
    $setupSrc = Join-Path $root 'installer\Setup.cs'
    if (-not (Test-Path $setupSrc)) { Write-Host "installer\Setup.cs not found - skipped"; return }
    $setupExe = Join-Path $dist 'OdysseyMouseSetup.exe'
    $args2 = New-Object System.Collections.ArrayList
    foreach ($a in @('/nologo', '/target:winexe', '/platform:anycpu', '/langversion:latest', '/optimize+', '/codepage:65001', '/warn:2',
                     ('/win32icon:' + $ico), ('/out:' + $setupExe), ('/resource:' + $ico + ',Setup.app.ico'),
                     ('/resource:' + $appExe + ',Payload.OdysseyMouse.exe'), $aiPath)) {
        [void]$args2.Add($a)
    }
    foreach ($r in $refs) { [void]$args2.Add('/r:' + $r) }
    [void]$args2.Add($setupSrc)
    & $csc $args2.ToArray()
    if ($LASTEXITCODE -ne 0) { throw "installer build failed (exit $LASTEXITCODE)" }
    Write-Host ("ok: " + $setupExe + "  " + ("{0:N0}" -f (Get-Item $setupExe).Length) + " bytes")
}

Write-Step "done"
Get-ChildItem $dist | ForEach-Object { Write-Host ("  {0,-28} {1,10:N0} bytes" -f $_.Name, $_.Length) }
