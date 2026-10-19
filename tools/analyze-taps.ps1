param([Parameter(Mandatory=$true)][string]$Path)

$rows = New-Object System.Collections.ArrayList
foreach ($l in Get-Content $Path) {
    if ($l -like 'el,*') { continue }
    $p = $l.Split(',')
    if ($p.Count -lt 15) { continue }
    [void]$rows.Add([pscustomobject]@{
        el=[double]$p[0]; x=[int]$p[1]; y=[int]$p[2]; on=[int]$p[4]; a=[int]$p[5]; sn=[int]$p[6]; sx=[int]$p[9]; sy=[int]$p[10]
    })
}
$n = $rows.Count
Write-Host ("rows={0} span={1:F1}s" -f $n, $rows[-1].el)

# ---- 255 statistics ----
$n255 = ($rows | Where-Object { $_.x -eq 255 }).Count
Write-Host ("frames with raw=255 : {0}" -f $n255)

# ---- snap density over time ----
Write-Host ""
Write-Host "=== snaps per 10s bucket ==="
$b = @{}
foreach ($r in $rows) { if ($r.sn -eq 1) { $k = [int]($r.el / 10); if (-not $b.ContainsKey($k)) { $b[$k]=0 }; $b[$k]++ } }
$b.Keys | Sort-Object | ForEach-Object { Write-Host ("  {0,3}-{1,3}s : {2,4}" -f ($_*10), ($_*10+10), $b[$_]) }

# ---- active-run / session-end analysis ----
Write-Host ""
Write-Host "=== touch sessions ended by 255 or by snap (first 40) ==="
$ends = New-Object System.Collections.ArrayList
$runStart = $null; $runStartVal = $null; $snapInRun = 0
for ($i = 0; $i -lt $n; $i++) {
    $r = $rows[$i]
    if ($r.a -eq 1) {
        if ($null -eq $runStart) { $runStart = $r.el; $runStartVal = "($($r.x),$($r.y))"; $snapInRun = 0 }
        if ($r.sn -eq 1) { $snapInRun++ }
    } else {
        if ($null -ne $runStart) {
            [void]$ends.Add([pscustomobject]@{ t=$r.el; dur=($r.el - $runStart); start=$runStartVal; end=($r.x); snaps=$snapInRun; cause=$(if ($r.x -eq 255) {'255'} else {'snapEnd'}) })
            $runStart = $null
        }
    }
}
Write-Host ("total session ends = {0}   (255-based = {1}, snap-based = {2})" -f $ends.Count, ($ends|Where-Object{$_.cause -eq '255'}).Count, ($ends|Where-Object{$_.cause -eq 'snapEnd'}).Count)
$ends | Select-Object -First 40 | ForEach-Object {
    Write-Host ("  t={0,7:F2} dur={1,6:F3}s start={2} snapsInRun={3} cause={4}" -f $_.t, $_.dur, $_.start, $_.snaps, $_.cause)
}

# ---- would a TapMaxSec=0.25 / TapMaxMove=12 filter have fired? approximate ----
Write-Host ""
Write-Host "=== candidate taps (session end with short dur, cause shows) ==="
$short = $ends | Where-Object { $_.dur -lt 0.25 }
Write-Host ("  duration<0.25s session ends = {0}" -f $short.Count)
$short | Select-Object -First 30 | ForEach-Object {
    Write-Host ("  t={0,7:F2} dur={1,6:F3}s start={2} cause={3}" -f $_.t, $_.dur, $_.start, $_.cause)
}
