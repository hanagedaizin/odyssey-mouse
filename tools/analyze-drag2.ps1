param([Parameter(Mandatory=$true)][string]$Path, [int]$TopN = 20)

$rows = New-Object System.Collections.ArrayList
foreach ($l in Get-Content $Path) {
    if ($l -like 'el,*') { continue }
    $p = $l.Split(',')
    if ($p.Count -lt 14) { continue }
    [void]$rows.Add([pscustomobject]@{
        el=[double]$p[0]; x=[int]$p[1]; y=[int]$p[2]; b6=[int]$p[3]; on=[int]$p[4]; a=[int]$p[5]
        fx=[double]$p[6]; fy=[double]$p[7]
        sx=[int]$p[8]; sy=[int]$p[9]; cx=[int]$p[10]; cy=[int]$p[11]
        rx=[double]$p[12]; ry=[double]$p[13]
    })
}
$n = $rows.Count
Write-Host ("rows={0} span={1:F1}s" -f $n, $rows[-1].el)

# --- X reverse only (Y is inverted on purpose) ---
$revX = 0; $revSumX = 0
$revY = 0; $revSumY = 0
for ($i = 1; $i -lt $n; $i++) {
    if ($rows[$i].a -ne 1 -or $rows[$i-1].a -ne 1) { continue }
    $drx = $rows[$i].x - $rows[$i-1].x
    $dry = $rows[$i].y - $rows[$i-1].y
    # X: same sign ; Y: inverted (raw up -> cursor up which is negative sy)
    if ($drx -ne 0 -and $rows[$i].sx -ne 0 -and (($drx -gt 0) -xor ($rows[$i].sx -gt 0))) { $revX++; $revSumX += [Math]::Abs($rows[$i].sx) }
    if ($dry -ne 0 -and $rows[$i].sy -ne 0 -and (($dry -lt 0) -xor ($rows[$i].sy -lt 0))) { $revY++; $revSumY += [Math]::Abs($rows[$i].sy) }
}
Write-Host ""
Write-Host "=== reversals (Y accounted for InvertY) ==="
Write-Host ("  X reverse: {0} events, {1}px total" -f $revX, $revSumX)
Write-Host ("  Y reverse: {0} events, {1}px total" -f $revY, $revSumY)

# --- segments: split by gaps > 0.3s of silence (no raw change and no movement) ---
Write-Host ""
Write-Host "=== active sessions ==="
$sess = New-Object System.Collections.ArrayList
$cur = $null
for ($i = 0; $i -lt $n; $i++) {
    $r = $rows[$i]
    $new = $false
    if ($null -eq $cur) { $new = $true }
    elseif ($r.a -eq 1 -and $cur.lastActive -eq 0) { $new = $true }
    elseif (($r.el - $cur.tEnd) -gt 0.5) { $new = $true }
    if ($new) {
        if ($null -ne $cur) { [void]$sess.Add($cur) }
        $cur = [pscustomobject]@{ t0=$r.el; tEnd=$r.el; lastActive=0
            x0=$r.x; y0=$r.y; x1=$r.x; y1=$r.y
            sentX=0; sentY=0; revX=0; revY=0; nframes=0; onSnaps=0 }
    }
    $cur.tEnd = $r.el
    if ($r.a -eq 1) { $cur.x1 = $r.x; $cur.y1 = $r.y; $cur.nframes++ }
    $cur.sentX += $r.sx; $cur.sentY += $r.sy
    if ($i -gt 0 -and $r.a -eq 1 -and $rows[$i-1].a -eq 1) {
        $drx = $r.x - $rows[$i-1].x; $dry = $r.y - $rows[$i-1].y
        if ($drx -ne 0 -and $r.sx -ne 0 -and (($drx -gt 0) -xor ($r.sx -gt 0))) { $cur.revX += [Math]::Abs($r.sx) }
        if ($dry -ne 0 -and $r.sy -ne 0 -and (($dry -lt 0) -xor ($r.sy -lt 0))) { $cur.revY += [Math]::Abs($r.sy) }
    }
    $cur.lastActive = $r.a
}
if ($null -ne $cur) { [void]$sess.Add($cur) }

$sess | Where-Object { $_.nframes -gt 10 } | ForEach-Object {
    Write-Host ("  t={0,7:F2}s dur={1,5:F2}s raw({2,3},{3,3})->({4,3},{5,3}) dx={6,4} dy={7,4} | cursor sent=({8,5},{9,5}) revX={10,4} revY={11,4} frames={12}" -f `
        $_.t0, ($_.tEnd - $_.t0), $_.x0, $_.y0, $_.x1, $_.y1, ($_.x1-$_.x0), ($_.y1-$_.y0), $_.sentX, $_.sentY, $_.revX, $_.revY, $_.nframes)
}

# --- biggest single-frame raw jumps while active ---
Write-Host ""
Write-Host "=== biggest raw jumps while BOTH frames active (X and Y separated) ==="
$list = New-Object System.Collections.ArrayList
for ($i = 1; $i -lt $n; $i++) {
    if ($rows[$i].a -ne 1 -or $rows[$i-1].a -ne 1) { continue }
    $d = [Math]::Max([Math]::Abs($rows[$i].x - $rows[$i-1].x), [Math]::Abs($rows[$i].y - $rows[$i-1].y))
    if ($d -ge 8) { [void]$list.Add([pscustomobject]@{ t=$rows[$i].el; px=$rows[$i-1].x; py=$rows[$i-1].y; x=$rows[$i].x; y=$rows[$i].y; sx=$rows[$i].sx; sy=$rows[$i].sy; d=$d }) }
}
Write-Host ("  count={0}" -f $list.Count)
$list | Sort-Object d -Descending | Select-Object -First $TopN | ForEach-Object {
    Write-Host ("  t={0,7:F2} ({1,3},{2,3})->({3,3},{4,3}) raw=({5,4},{6,4}) cursor=({7,5},{8,5})" -f $_.t, $_.px, $_.py, $_.x, $_.y, ($_.x-$_.px), ($_.y-$_.py), $_.sx, $_.sy)
}
