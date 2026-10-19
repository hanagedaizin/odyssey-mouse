param([Parameter(Mandatory=$true)][string]$Path)

$re = [regex]'\[\s*([0-9.]+)s\] PAD raw=\(\s*(-?\d+),\s*(-?\d+)\) f=\(\s*(-?[\d.]+),\s*(-?[\d.]+)\) d=\(\s*(-?[\d.]+),\s*(-?[\d.]+)\) spd=\s*(-?[\d.]+) x([\d.]+) rem=\(\s*(-?[\d.]+),\s*(-?[\d.]+)\)'

$rows = New-Object System.Collections.ArrayList
foreach ($l in Get-Content $Path) {
    $m = $re.Match($l)
    if ($m.Success) {
        [void]$rows.Add([pscustomobject]@{
            t    = [double]$m.Groups[1].Value
            rx   = [int]$m.Groups[2].Value
            ry   = [int]$m.Groups[3].Value
            fx   = [double]$m.Groups[4].Value
            fy   = [double]$m.Groups[5].Value
            dx   = [double]$m.Groups[6].Value
            dy   = [double]$m.Groups[7].Value
            spd  = [double]$m.Groups[8].Value
            sc   = [double]$m.Groups[9].Value
            remx = [double]$m.Groups[10].Value
            remy = [double]$m.Groups[11].Value
        })
    }
}

Write-Host ("rows=" + $rows.Count)

# --- 1. large raw jumps between consecutive printed lines ---
Write-Host ""
Write-Host "=== large raw jumps (|drx|+|dry| >= 15) ==="
for ($i = 1; $i -lt $rows.Count; $i++) {
    $jx = $rows[$i].rx - $rows[$i-1].rx
    $jy = $rows[$i].ry - $rows[$i-1].ry
    if (([Math]::Abs($jx) + [Math]::Abs($jy)) -ge 15) {
        Write-Host ("  t={0,7:F2} raw ({1,3},{2,3}) -> ({3,3},{4,3})  jump=({5,4},{6,4}) dt={7:F3}" -f `
            $rows[$i].t, $rows[$i-1].rx, $rows[$i-1].ry, $rows[$i].rx, $rows[$i].ry, $jx, $jy, ($rows[$i].t - $rows[$i-1].t))
    }
}

# --- 2. residual magnitude distribution ---
Write-Host ""
Write-Host "=== residual (pending px) stats ==="
$rm = $rows | ForEach-Object { [Math]::Abs($_.remx) + [Math]::Abs($_.remy) }
$sorted = $rm | Sort-Object
if ($sorted.Count -gt 0) {
    Write-Host ("  max={0:F1} p99={1:F1} p95={2:F1} median={3:F1}" -f `
        $sorted[-1], $sorted[[int]($sorted.Count*0.99)], $sorted[[int]($sorted.Count*0.95)], $sorted[[int]($sorted.Count*0.5)])
}

# --- 3. how often does applied direction oppose raw direction? ---
Write-Host ""
Write-Host "=== direction reversals (raw moves one way, filtered delta opposite) ==="
$rev = 0; $tot = 0
for ($i = 1; $i -lt $rows.Count; $i++) {
    $drx = $rows[$i].rx - $rows[$i-1].rx
    $dry = $rows[$i].ry - $rows[$i-1].ry
    if ($drx -ne 0 -or $dry -ne 0) {
        $tot++
        if (($drx * $rows[$i].dx + $dry * $rows[$i].dy) -lt 0) { $rev++ }
    }
}
Write-Host ("  reversals={0} / movements={1}" -f $rev, $tot)

# --- 4. sample a window around the biggest jump ---
Write-Host ""
Write-Host "=== biggest 3 jumps with context ==="
$jumps = @()
for ($i = 1; $i -lt $rows.Count; $i++) {
    $jx = $rows[$i].rx - $rows[$i-1].rx
    $jy = $rows[$i].ry - $rows[$i-1].ry
    $jumps += [pscustomobject]@{ i=$i; mag=([Math]::Abs($jx)+[Math]::Abs($jy)) }
}
$top = $jumps | Sort-Object mag -Descending | Select-Object -First 3
foreach ($j in $top) {
    Write-Host ("--- jump mag={0} at index {1} ---" -f $j.mag, $j.i)
    $a = [Math]::Max(0, $j.i - 4); $b = [Math]::Min($rows.Count-1, $j.i + 4)
    for ($k = $a; $k -le $b; $k++) {
        $r = $rows[$k]
        Write-Host ("    t={0,7:F2} raw=({1,3},{2,3}) f=({3,6:F1},{4,6:F1}) d=({5,6:F2},{6,6:F2}) rem=({7,7:F1},{8,7:F1})" -f `
            $r.t, $r.rx, $r.ry, $r.fx, $r.fy, $r.dx, $r.dy, $r.remx, $r.remy)
    }
}
