param([Parameter(Mandatory=$true)][string]$Path, [int]$TopN = 20)

$rows = New-Object System.Collections.ArrayList
foreach ($l in Get-Content $Path) {
    if ($l -like 'el,*') { continue }
    $p = $l.Split(',')
    if ($p.Count -lt 15) { continue }
    [void]$rows.Add([pscustomobject]@{
        el=[double]$p[0]; x=[int]$p[1]; y=[int]$p[2]; b6=[int]$p[3]
        on=[int]$p[4]; a=[int]$p[5]; sn=[int]$p[6]
        fx=[double]$p[7]; fy=[double]$p[8]
        sx=[int]$p[9]; sy=[int]$p[10]; cx=[int]$p[11]; cy=[int]$p[12]
        rx=[double]$p[13]; ry=[double]$p[14]
    })
}
$n = $rows.Count
if ($n -lt 2) { Write-Host "no rows (old format?)"; exit }
Write-Host ("rows={0} span={1:F1}s" -f $n, $rows[-1].el)

# ---------- snaps ----------
Write-Host ""
Write-Host "=== snapped frames (device jump discarded) ==="
$snaps = $rows | Where-Object { $_.sn -eq 1 }
$snapEnd = $rows | Where-Object { $_.sn -eq 1 -and $_.on -eq 1 }
Write-Host ("  total snaps            = {0}" -f $snaps.Count)
Write-Host ("  ...that also ended the touch session = {0}" -f $snapEnd.Count)
$snapMove = 0; $snapMoveN = 0
foreach ($s in $snaps) { $m = [Math]::Abs($s.sx) + [Math]::Abs($s.sy); if ($m -gt 0) { $snapMoveN++; $snapMove += $m } }
Write-Host ("  cursor px emitted on snap frames = {0}px over {1} frames (residual only)" -f $snapMove, $snapMoveN)

Write-Host ""
Write-Host "=== largest discarded jumps ==="
for ($i = 1; $i -lt $n; $i++) {
    if ($rows[$i].sn -ne 1) { continue }
    $rows[$i] | Add-Member -NotePropertyName jumpsz -NotePropertyValue ([Math]::Max([Math]::Abs($rows[$i].x - $rows[$i-1].x), [Math]::Abs($rows[$i].y - $rows[$i-1].y))) -Force
}
$rows | Where-Object { $_.sn -eq 1 } | Sort-Object jumpsz -Descending | Select-Object -First $TopN | ForEach-Object {
    Write-Host ("  t={0,7:F2} jump={1,3} -> raw=({2,3},{3,3}) cursorSent=({4,4},{5,4})" -f $_.el, $_.jumpsz, $_.x, $_.y, $_.sx, $_.sy)
}

# ---------- reversals ----------
$revX = 0; $revSumX = 0; $sameX = 0
$revY = 0; $revSumY = 0; $sameY = 0
$worst = New-Object System.Collections.ArrayList
for ($i = 1; $i -lt $n; $i++) {
    $a = $rows[$i]; $b = $rows[$i-1]
    if ($a.a -ne 1 -or $b.a -ne 1 -or $a.sn -eq 1) { continue }
    $drx = $a.x - $b.x; $dry = $a.y - $b.y
    if ($drx -ne 0 -and $a.sx -ne 0) {
        if (($drx -gt 0) -xor ($a.sx -gt 0)) { $revX++; $revSumX += [Math]::Abs($a.sx); [void]$worst.Add([pscustomobject]@{t=$a.el;axis='X';draw=$drx;sent=$a.sx}) } else { $sameX++ }
    }
    if ($dry -ne 0 -and $a.sy -ne 0) {
        if (($dry -lt 0) -xor ($a.sy -lt 0)) { $revY++; $revSumY += [Math]::Abs($a.sy); [void]$worst.Add([pscustomobject]@{t=$a.el;axis='Y';draw=$dry;sent=$a.sy}) } else { $sameY++ }
    }
}
Write-Host ""
Write-Host "=== cursor sent OPPOSITE to finger (active frames, Y accounts for InvertY) ==="
Write-Host ("  X: same={0,5}  reverse={1,4}  reverseTotal={2,6}px" -f $sameX, $revX, $revSumX)
Write-Host ("  Y: same={0,5}  reverse={1,4}  reverseTotal={2,6}px" -f $sameY, $revY, $revSumY)
Write-Host "  worst:"
$worst | Sort-Object { [Math]::Abs($_.sent) } -Descending | Select-Object -First 10 | ForEach-Object {
    Write-Host ("    t={0,7:F2} {1} finger={2,4} cursor={3,5}px" -f $_.t, $_.axis, $_.draw, $_.sent)
}

# ---------- slow monotone drag check: longest runs of sustained one-directional finger travel ----------
Write-Host ""
Write-Host "=== sustained one-way finger travel (>=25 units, no snap involved) ==="
$i = 1
$runs = New-Object System.Collections.ArrayList
while ($i -lt $n) {
    if ($rows[$i].a -ne 1 -or $rows[$i].sn -eq 1) { $i++; continue }
    $dirX = [Math]::Sign($rows[$i].x - $rows[$i-1].x)
    $dirY = [Math]::Sign($rows[$i].y - $rows[$i-1].y)
    $sx0 = $rows[$i].x; $sy0 = $rows[$i].y
    $j = $i; $rev = 0
    $cx0 = $rows[$i-1].cx; $cy0 = $rows[$i-1].cy
    while ($j + 1 -lt $n) {
        $k = $j + 1
        if ($rows[$k].a -ne 1 -or $rows[$k].sn -eq 1) { break }
        $dx = $rows[$k].x - $rows[$j].x; $dy = $rows[$k].y - $rows[$j].y
        if ($dirX -ne 0 -and $dx -ne 0 -and ([Math]::Sign($dx) -ne $dirX)) { break }
        if ($dirY -ne 0 -and $dy -ne 0 -and ([Math]::Sign($dy) -ne $dirY)) { break }
        $j = $k
    }
    $dist = [Math]::Max([Math]::Abs($rows[$j].x - $sx0), [Math]::Abs($rows[$j].y - $sy0))
    if ($dist -ge 25) {
        [void]$runs.Add([pscustomobject]@{ t0=$rows[$i].el; dur=$rows[$j].el-$rows[$i].el; dist=$dist
            x0=$sx0; y0=$sy0; x1=$rows[$j].x; y1=$rows[$j].y
            cur=(($rows[$j].cx - $cx0) + ($rows[$j].cy - $cy0)) })
    }
    $i = $j + 1
}
Write-Host ("  runs found = {0}" -f $runs.Count)
$runs | Select-Object -First $TopN | ForEach-Object {
    Write-Host ("  t={0,7:F2} dur={1,5:F2}s raw({2,3},{3,3})->({4,3},{5,3}) fingerDist={6,3} cursorDelta={7,6}px" -f $_.t0, $_.dur, $_.x0, $_.y0, $_.x1, $_.y1, $_.dist, $_.cur)
}

$totalAbs = 0
for ($i = 1; $i -lt $n; $i++) { $totalAbs += [Math]::Abs($rows[$i].sx) + [Math]::Abs($rows[$i].sy) }
Write-Host ""
Write-Host ("cursor total path = {0}px   net = ({1},{2})px   elapsed = {3:F1}s" -f $totalAbs, $rows[-1].cx, $rows[-1].cy, $rows[-1].el)
