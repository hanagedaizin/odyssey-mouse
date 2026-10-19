param([Parameter(Mandatory=$true)][string]$Path, [int]$TopN = 15)

$rows = New-Object System.Collections.ArrayList
foreach ($l in Get-Content $Path) {
    if ($l -like 'el,*') { continue }
    $p = $l.Split(',')
    if ($p.Count -lt 20) { continue }
    $pd = 0
    if ($p.Count -ge 21) { $pd = [int]$p[20] }
    [void]$rows.Add([pscustomobject]@{
        el=[double]$p[0]; x=[int]$p[1]; y=[int]$p[2]; nt=[int]$p[4]; a=[int]$p[5]
        dn=[int]$p[6]; up=[int]$p[7]; jump=[int]$p[8]; tr=[double]$p[9]
        rx=[double]$p[10]; ry=[double]$p[11]
        fx=[double]$p[12]; fy=[double]$p[13]
        sx=[int]$p[14]; sy=[int]$p[15]; cx=[int]$p[16]; cy=[int]$p[17]; pd=$pd
    })
}
$n = $rows.Count
if ($n -lt 2) { Write-Host "no rows"; exit }
Write-Host ("rows={0} span={1:F1}s" -f $n, $rows[-1].el)

$downs = @($rows | Where-Object { $_.dn -eq 1 })
$ups   = @($rows | Where-Object { $_.up -eq 1 })
Write-Host ("touch-downs={0}  releases={1}  (255 frames={2})" -f $downs.Count, $ups.Count, (@($rows|Where-Object{$_.x -eq 255}).Count))
Write-Host ("pending (lift-candidate) frames={0}" -f (@($rows | Where-Object { $_.pd -eq 1 }).Count))

# リフト判定が正しかったか: UP 直後の 50ms で生値が動いていれば「休止値を通過しただけの
# 本物の移動」を捨てたことになる（＝ゴム感の元）
Write-Host ""
Write-Host "=== release 判定の検証 (UP 直後 50ms の生値) ==="
$falseUp = 0; $falseUpUnits = 0.0; $trueUp = 0
for ($i = 0; $i -lt $n; $i++) {
    $r = $rows[$i]
    if ($r.up -ne 1) { continue }
    $changed = $false; $j = $i + 1
    while ($j -lt $n -and ($rows[$j].el - $r.el) -le 0.05) {
        if ($rows[$j].x -ne $r.x -or $rows[$j].y -ne $r.y) { $changed = $true; break }
        $j++
    }
    if ($changed) {
        $falseUp++
        if ($i -gt 0) { $falseUpUnits += [Math]::Abs($rows[$i].x - $rows[$i-1].x) + [Math]::Abs($rows[$i].y - $rows[$i-1].y) }
    } else { $trueUp++ }
}
Write-Host ("  本物のリフト={0}  偽リフト(移動を捨てた)={1}" -f $trueUp, $falseUp)

# cursor movement emitted on discarded frames
$mv = 0; $mvN = 0
foreach ($r in $rows) { if ($r.dn -eq 1 -or $r.up -eq 1 -or $r.nt -eq 1) { $m=[Math]::Abs($r.sx)+[Math]::Abs($r.sy); if ($m -gt 0) { $mvN++; $mv += $m } } }
Write-Host ("cursor px emitted on discarded frames = {0}px over {1} frames (残量の後追いのみ)" -f $mv, $mvN)

# deltas that were discarded: how big were they
Write-Host ""
Write-Host "=== discarded jumps (top) ==="
$downs | Sort-Object jump -Descending | Select-Object -First $TopN | ForEach-Object {
    Write-Host ("  DOWN t={0,7:F2} jump={1,3} -> raw=({2,3},{3,3}) toRest={4,4:F0} sent=({5,4},{6,4})" -f $_.el, $_.jump, $_.x, $_.y, $_.tr, $_.sx, $_.sy)
}
$ups | Sort-Object jump -Descending | Select-Object -First $TopN | ForEach-Object {
    Write-Host ("  UP   t={0,7:F2} jump={1,3} -> raw=({2,3},{3,3}) toRest={4,4:F0} sent=({5,4},{6,4})" -f $_.el, $_.jump, $_.x, $_.y, $_.tr, $_.sx, $_.sy)
}

# reversals
$revX=0;$revY=0;$revSumX=0;$revSumY=0;$worst=New-Object System.Collections.ArrayList
for ($i=1; $i -lt $n; $i++) {
    $a=$rows[$i]; $b=$rows[$i-1]
    if ($a.dn -eq 1 -or $a.up -eq 1 -or $a.nt -eq 1) { continue }
    if ($b.x -eq 255 -or $a.x -eq 255) { continue }
    $drx = $a.x - $b.x; $dry = $a.y - $b.y
    if ($drx -ne 0 -and $a.sx -ne 0) { if (($drx -gt 0) -xor ($a.sx -gt 0)) { $revX++; $revSumX += [Math]::Abs($a.sx); [void]$worst.Add([pscustomobject]@{t=$a.el;ax='X';d=$drx;s=$a.sx}) } }
    if ($dry -ne 0 -and $a.sy -ne 0) { if (($dry -lt 0) -eq ($a.sy -lt 0)) { $revY++; $revSumY += [Math]::Abs($a.sy); [void]$worst.Add([pscustomobject]@{t=$a.el;ax='Y';d=$dry;s=$a.sy}) } }
}
Write-Host ""
Write-Host "=== cursor sent OPPOSITE to finger (real movement frames only) ==="
Write-Host ("  X: reverse={0,4}  total={1,6}px" -f $revX, $revSumX)
Write-Host ("  Y: reverse={0,4}  total={1,6}px" -f $revY, $revSumY)
$worst | Sort-Object { [Math]::Abs($_.s) } -Descending | Select-Object -First 8 | ForEach-Object {
    Write-Host ("    t={0,7:F2} {1} finger={2,4} cursor={3,5}px" -f $_.t,$_.ax,$_.d,$_.s)
}

# tap candidates: down -> up with small travel
Write-Host ""
Write-Host "=== touch sessions (down -> up) ==="
$sess = New-Object System.Collections.ArrayList
$cur = $null
foreach ($r in $rows) {
    if ($r.dn -eq 1) { if ($null -ne $cur) { [void]$sess.Add($cur) }; $cur = [pscustomobject]@{ t0=$r.el; x0=$r.x; y0=$r.y; tEnd=$r.el; travel=0; n=0 } }
    elseif ($null -ne $cur) {
        if ($r.x -ne 255 -and $r.y -ne 255) {
            $d = [Math]::Max([Math]::Abs($r.x - $cur.x0), [Math]::Abs($r.y - $cur.y0))
            if ($d -gt $cur.travel) { $cur.travel = $d }
        }
        $cur.tEnd = $r.el; $cur.n++
        if ($r.up -eq 1 -or $r.nt -eq 1) { [void]$sess.Add($cur); $cur = $null }
    }
}
if ($null -ne $cur) { [void]$sess.Add($cur) }
Write-Host ("  sessions={0}" -f $sess.Count)
$cand = @($sess | Where-Object { ($_.tEnd - $_.t0) -ge 0.04 -and ($_.tEnd - $_.t0) -lt 0.25 -and $_.travel -le 12 })
Write-Host ("  short+small sessions (tap candidates) = {0}" -f $cand.Count)
$cand | Select-Object -First 25 | ForEach-Object {
    Write-Host ("    t={0,7:F2} hold={1,5:F3}s travel={2,3} at ({3},{4})" -f $_.t0, ($_.tEnd-$_.t0), $_.travel, $_.x0, $_.y0)
}
Write-Host ""
Write-Host "=== longest / largest sessions ==="
$sess | Sort-Object travel -Descending | Select-Object -First 8 | ForEach-Object {
    Write-Host ("    t={0,7:F2} hold={1,6:F3}s travel={2,3} from ({3},{4})" -f $_.t0, ($_.tEnd-$_.t0), $_.travel, $_.x0, $_.y0)
}

$totalAbs=0; for ($i=1;$i -lt $n;$i++){ $totalAbs += [Math]::Abs($rows[$i].sx)+[Math]::Abs($rows[$i].sy) }
Write-Host ""
Write-Host ("cursor total path = {0}px  net=({1},{2})px  elapsed={3:F1}s" -f $totalAbs, $rows[-1].cx, $rows[-1].cy, $rows[-1].el)
