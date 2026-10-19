param([Parameter(Mandatory=$true)][string]$Path, [int]$MinJump = 10)

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
Write-Host ("rows={0} span={1:F1}s  neutral=({2},{3})" -f $rows.Count, $rows[-1].el, $rows[0].nx, $rows[0].ny)
$n = 0; $leak = 0; $caught = 0
Write-Host ""
Write-Host ("=== raw jumps >= {0} units (Chebyshev) ===" -f $MinJump)
for ($i=1; $i -lt $rows.Count; $i++) {
    $dx = $rows[$i].x - $rows[$i-1].x; $dy = $rows[$i].y - $rows[$i-1].y
    $d = [Math]::Max([Math]::Abs($dx), [Math]::Abs($dy))
    if ($d -ge $MinJump) {
        $n++
        $tag = if ($rows[$i].a -eq 1) { 'LEAK(active)'; $leak++ } else { 'caught'; $caught++ }
        $dnPrev = [Math]::Max([Math]::Abs($rows[$i-1].x - $rows[0].nx), [Math]::Abs($rows[$i-1].y - $rows[0].ny))
        Write-Host ("  t={0,7:F2} ({1,3},{2,3})->({3,3},{4,3}) jump=({5,4},{6,4}) |prev dN|={7,4} on={8} {9}" -f `
            $rows[$i].el, $rows[$i-1].x, $rows[$i-1].y, $rows[$i].x, $rows[$i].y, $dx, $dy, $dnPrev, $rows[$i].on, $tag)
    }
}
Write-Host ""
Write-Host ("total big jumps={0}  caught={1}  LEAK={2}" -f $n, $caught, $leak)
Write-Host ""
Write-Host "=== active ratio ==="
$act = ($rows | Where-Object { $_.a -eq 1 }).Count
Write-Host ("  active rows={0}/{1}" -f $act, $rows.Count)
