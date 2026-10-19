param([Parameter(Mandatory=$true)][string]$Path)

$rows = New-Object System.Collections.ArrayList
foreach ($l in Get-Content $Path) {
    if ($l -like 'el,*') { continue }
    $p = $l.Split(',')
    if ($p.Count -lt 14) { continue }
    [void]$rows.Add([pscustomobject]@{ el=[double]$p[0]; x=[int]$p[1]; y=[int]$p[2]; on=[int]$p[4]; a=[int]$p[5]; sx=[int]$p[8]; sy=[int]$p[9] })
}
Write-Host ("rows={0} span={1:F1}s" -f $rows.Count, $rows[-1].el)

# dwell time for every exact raw value
$dwell = @{}
$i = 0
while ($i -lt $rows.Count) {
    $x = $rows[$i].x; $y = $rows[$i].y
    $j = $i
    while ($j + 1 -lt $rows.Count -and $rows[$j+1].x -eq $x -and $rows[$j+1].y -eq $y) { $j++ }
    $dur = $rows[$j].el - $rows[$i].el
    $key = "$x,$y"
    if (-not $dwell.ContainsKey($key)) { $dwell[$key] = [pscustomobject]@{ key=$key; total=0.0; runs=0; max=0.0 } }
    $dwell[$key].total += $dur
    $dwell[$key].runs++
    if ($dur -gt $dwell[$key].max) { $dwell[$key].max = $dur }
    $i = $j + 1
}
Write-Host ""
Write-Host "=== raw values by TOTAL dwell time (top 20) ==="
$dwell.Values | Sort-Object total -Descending | Select-Object -First 20 | ForEach-Object {
    Write-Host ("  ({0,7})  total={1,7:F1}s  runs={2,5}  longest={3,6:F2}s" -f $_.key, $_.total, $_.runs, $_.max)
}

# Find every "snap": a frame where raw changed by >=8 units and the NEW value dwells >=0.3s
Write-Host ""
Write-Host "=== snaps to a long-dwell value (new value dwells >=0.3s) ==="
$lastVal = ''
$snaps = New-Object System.Collections.ArrayList
for ($k = 1; $k -lt $rows.Count; $k++) {
    $pr = $rows[$k-1]; $r = $rows[$k]
    $d = [Math]::Max([Math]::Abs($r.x - $pr.x), [Math]::Abs($r.y - $pr.y))
    if ($d -lt 8) { continue }
    $key = "$($r.x),$($r.y)"
    if ($dwell.ContainsKey($key) -and $dwell[$key].max -ge 0.3) {
        [void]$snaps.Add([pscustomobject]@{ t=$r.el; px=$pr.x; py=$pr.y; x=$r.x; y=$r.y; d=$d; dwell=$dwell[$key].max; sx=$r.sx; sy=$r.sy; a=$r.a; pa=$pr.a })
    }
}
Write-Host ("  count={0}" -f $snaps.Count)
$snaps | Sort-Object d -Descending | Select-Object -First 40 | ForEach-Object {
    Write-Host ("  t={0,7:F2} ({1,3},{2,3})->({3,3},{4,3}) d={5,3} dwell={6,5:F2}s cursor=({7,4},{8,4}) active {9}->{10}" -f $_.t, $_.px, $_.py, $_.x, $_.y, $_.d, $_.dwell, $_.sx, $_.sy, $_.pa, $_.a)
}
