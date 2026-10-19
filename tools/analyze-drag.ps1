param([Parameter(Mandatory=$true)][string]$Path, [int]$ActiveOnly = 1)

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
if ($n -lt 2) { Write-Host "no rows"; exit }
Write-Host ("rows={0} span={1:F1}s" -f $n, $rows[-1].el)

# --- 1. reverse movement: finger moves one way, cursor is sent the opposite way ---
$revX = 0; $revY = 0; $sameX = 0; $sameY = 0
$revSumX = 0; $revSumY = 0
$worst = @()
for ($i = 1; $i -lt $n; $i++) {
    if ($ActiveOnly -eq 1 -and ($rows[$i].a -ne 1 -or $rows[$i-1].a -ne 1)) { continue }
    $drx = $rows[$i].x - $rows[$i-1].x
    $dry = $rows[$i].y - $rows[$i-1].y
    $sx = $rows[$i].sx
    $sy = $rows[$i].sy
    if ($drx -ne 0 -and $sx -ne 0) {
        if (($drx -gt 0) -xor ($sx -gt 0)) { $revX++; $revSumX += [Math]::Abs($sx); $worst += [pscustomobject]@{t=$rows[$i].el; axis='X'; draw=$drx; sent=$sx} } else { $sameX++ }
    }
    if ($dry -ne 0 -and $sy -ne 0) {
        if (($dry -gt 0) -xor ($sy -gt 0)) { $revY++; $revSumY += [Math]::Abs($sy); $worst += [pscustomobject]@{t=$rows[$i].el; axis='Y'; draw=$dry; sent=$sy} } else { $sameY++ }
    }
}
Write-Host ""
Write-Host "=== cursor sent OPPOSITE to finger movement (while active) ==="
Write-Host ("  X: same={0}  reverse={1}  (reverse total={2}px)" -f $sameX, $revX, $revSumX)
Write-Host ("  Y: same={0}  reverse={1}  (reverse total={2}px)" -f $sameY, $revY, $revSumY)

Write-Host ""
Write-Host "=== worst reversals ==="
$worst | Sort-Object { [Math]::Abs($_.sent) } -Descending | Select-Object -First 15 | ForEach-Object {
    Write-Host ("  t={0,7:F2} {1} finger={2,4} cursor={3,5}px" -f $_.t, $_.axis, $_.draw, $_.sent)
}

# --- 2. movement emitted at moments the touch ended (lift) - must be zero ---
Write-Host ""
Write-Host "=== movement sent on the frame the touch session ENDED (lift) ==="
$liftCnt = 0; $liftMove = 0
for ($i = 1; $i -lt $n; $i++) {
    if ($rows[$i].a -eq 0 -and $rows[$i-1].a -eq 1) {
        $liftCnt++
        $m = [Math]::Abs($rows[$i].sx) + [Math]::Abs($rows[$i].sy)
        if ($m -gt 0) { $liftMove++; Write-Host ("  t={0,7:F2} raw=({1},{2}) sent=({3},{4})" -f $rows[$i].el, $rows[$i].x, $rows[$i].y, $rows[$i].sx, $rows[$i].sy) }
    }
}
Write-Host ("  lift events={0}  with movement={1}" -f $liftCnt, $liftMove)

# --- 3. time between finger raw change and cursor catch-up (lag) ---
Write-Host ""
Write-Host "=== summary of cumulative cursor vs raw range ==="
Write-Host ("  raw X range {0}..{1}   raw Y range {2}..{3}" -f ($rows.x|Measure-Object -Min).Minimum, ($rows.x|Measure-Object -Max).Maximum, ($rows.y|Measure-Object -Min).Minimum, ($rows.y|Measure-Object -Max).Maximum)
Write-Host ("  cursor total moved X={0}px  Y={1}px (net)" -f $rows[-1].cx, $rows[-1].cy)
$totalAbs = 0; for ($i=1; $i -lt $n; $i++) { $totalAbs += [Math]::Abs($rows[$i].sx) + [Math]::Abs($rows[$i].sy) }
Write-Host ("  cursor total path length = {0}px" -f $totalAbs)
