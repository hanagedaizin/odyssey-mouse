# build.ps1 - SensorViewer.exe (IMU / コンパスのビジュアライザ) をビルドする。
# .NET Framework 4.x と C# コンパイラ (Roslyn csc、無ければフレームワーク内蔵 csc) のみ必要。
param(
    [string]$Out = ''
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

$refs = @(
    (Join-Path $fx 'mscorlib.dll'),
    (Join-Path $fx 'System.dll'),
    (Join-Path $fx 'System.Core.dll'),
    (Join-Path $fx 'System.Drawing.dll'),
    (Join-Path $fx 'System.Windows.Forms.dll'),
    (Join-Path $fx 'System.Web.Extensions.dll')   # glTF(.glb) の JSON 解析に JavaScriptSerializer で使う
)
foreach ($r in $refs) { if (-not (Test-Path $r)) { throw "missing reference: $r" } }

# ---------------------------------------------------------------------------
Write-Step "SensorViewer.exe"
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }
if (-not (Test-Path $obj)) { New-Item -ItemType Directory -Path $obj | Out-Null }
$outExe = if ($Out) { $Out } else { Join-Path $dist 'SensorViewer.exe' }

$src = Join-Path $root 'SensorViewer.cs'
if (-not (Test-Path $src)) { throw "missing source: $src" }

# csc の引数は $args (自動変数) と衝突するので ArrayList 経由で渡す (app/build.ps1 と同じ)
$cscArgs = New-Object System.Collections.ArrayList
foreach ($a in @('/nologo', '/target:winexe', '/platform:anycpu', '/langversion:latest', '/optimize+',
                 '/codepage:65001', '/warn:2', ('/out:' + $outExe), $src)) {
    [void]$cscArgs.Add($a)
}
foreach ($r in $refs) { [void]$cscArgs.Add('/r:' + $r) }
& $csc $cscArgs.ToArray()
if ($LASTEXITCODE -ne 0) { throw "build failed (exit $LASTEXITCODE)" }
Write-Host ("ok: " + $outExe + "  " + ("{0:N0}" -f (Get-Item $outExe).Length) + " bytes")

# ---------------------------------------------------------------------------
# exe.config : GDI+ 等のネイティブ例外 (AccessViolation) でプロセスを落とさず
# ログに残して続行できるようにする（ビジュアライザ用の保険）。
Write-Step "SensorViewer.exe.config"
$cfg = '<?xml version="1.0" encoding="utf-8"?>' + "`r`n" +
       '<configuration>' + "`r`n" +
       '  <runtime>' + "`r`n" +
       '    <legacyCorruptedStateExceptionsPolicy enabled="true"/>' + "`r`n" +
       '  </runtime>' + "`r`n" +
       '</configuration>' + "`r`n"
$cfgPath = $outExe + '.config'
[System.IO.File]::WriteAllText($cfgPath, $cfg, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("ok: " + $cfgPath)

Write-Step "done"
