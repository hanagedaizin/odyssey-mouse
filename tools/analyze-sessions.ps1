param([Parameter(Mandatory=$true)][string]$Path)

$rows = New-Object System.Collections.ArrayList
foreach ($l in Get-Content $Path) {
    if ($l -like 'el,*') { continue }
    $p = $l.Split(',')
    if ($p.Count -lt 11) { continue }
    [void]$rows.Add([pscustomobject]@{
        el=[double]$p[0]; x=[int]$p[1]; y=[int]$p[2]; b6=$p[3]; t=$p[4]; a=[int]$p[5]
        fx=[double]$p[6]; fy=[double]$p[7]; nx=[double]$p[8]; ny=[double]$p[9]; on=[int]$p[10]
    })
}
$n = $rows.Count
Write-Host ("rows={0} span={1:F1}s neutral=({2},{3})" -f $n, $rows[-1].el, $rows[0].nx, $rows[0].ny)

# session transitions
$starts = 0; $ends = 0
$sessDur = New-Object System.Collections.ArrayList
$sessStart = $null
for ($i=0; $i -lt $n; $i++) {
    if ($i -gt 0 -and $rows[$i].a -eq 1 -and $rows[$i-1].a -eq 0) { $starts++; $sessStart = $rows[$i].el }
    if ($i -gt 0 -and $rows[$i].a -eq 0 -and $rows[$i-1].a -eq 1) {
        $ends++
        if ($null -ne $sessStart) { [void]$sessDur.Add($rows[$i].el - $sessStart) }
    }
}
Write-Host ("sessions started={0}  ended={1}   ({2:F1} starts/sec)" -f $starts, $ends, ($starts / $rows[-1].el))
if ($sessDur.Count -gt 0) {
    $sd = $sessDur | Sort-Object
    $short = ($sd | Where-Object { $_ -lt 0.05 }).Count
    Write-Host ("session duration: median={0:F3}s  min={1:F3}  max={2:F3}   (<50ms: {3})" -f `
        $sd[[int]($sd.Count/2)], $sd[0], $sd[-1], $short)
}
$neutralRows = ($rows | Where-Object { $_.on -eq 1 }).Count
Write-Host ("time on neutral: {0}/{1} rows ({2:F1}%)" -f $neutralRows, $n, (100.0*$neutralRows/$n))

# real leaks: big jump applied while ALREADY in a session (both rows active)
Write-Host ""
Write-Host "=== REAL leaks: jump >= 12 with both prev and cur active ==="
$cnt = 0
for ($i=1; $i -lt $n; $i++) {
    if ($rows[$i].a -eq 1 -and $rows[$i-1].a -eq 1) {
        $dx = $rows[$i].x - $rows[$i-1].x; $dy = $rows[$i].y - $rows[$i-1].y
        $d = [Math]::Max([Math]::Abs($dx), [Math]::Abs($dy))
        if ($d -ge 12) {
            $cnt++
            Write-Host ("  t={0,7:F2} ({1,3},{2,3})->({3,3},{4,3}) jump=({5,4},{6,4})" -f $rows[$i].el, $rows[$i-1].x, $rows[$i-1].y, $rows[$i].x, $rows[$i].y, $dx, $dy)
        }
    }
}
Write-Host ("  count={0}" -f $cnt)

# snaps INTO neutral:
Write-Host ""
Write-Host "=== snap into neutral from >=10 away (undo events) ==="
$s = 0
for ($i=1; $i -lt $n; $i++) {
    if ($rows[$i].on -eq 1 -and $rows[$i-1].on -eq 0) {
        $dp = [Math]::Max([Math]::Abs($rows[$i-1].x - $rows[0].nx), [Math]::Abs($rows[$i-1].y - $rows[0].ny))
        if ($dp -ge 10) { $s++; if ($s -le 12) { Write-Host ("  t={0,7:F2} ({1,3},{2,3})->({3,3},{4,3})  dNprev={5}" -f $rows[$i].el, $rows[$i-1].x, $rows[$i-1].y, $rows[$i].x, $rows[$i].y, $dp) } }
    }
}
Write-Host ("  count={0}" -f $s)
