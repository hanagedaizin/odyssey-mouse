param([Parameter(Mandatory=$true)][string]$Path)

$rows = New-Object System.Collections.ArrayList
foreach ($l in Get-Content $Path) {
    if ($l -like 'el,*') { continue }
    $p = $l.Split(',')
    if ($p.Count -lt 15) { continue }
    [void]$rows.Add([pscustomobject]@{ el=[double]$p[0]; x=[int]$p[1]; y=[int]$p[2]; sn=[int]$p[6] })
}
$n = $rows.Count

function VariationAfter($i, $ms) {
    $t0 = $rows[$i].el; $v = 0.0; $px = $rows[$i].x; $py = $rows[$i].y
    $k = $i + 1
    while ($k -lt $n -and ($rows[$k].el - $t0) * 1000 -lt $ms) {
        $v += [Math]::Abs($rows[$k].x - $px) + [Math]::Abs($rows[$k].y - $py)
        $px = $rows[$k].x; $py = $rows[$k].y; $k++
    }
    return $v
}

# separation using variation over the NEXT 30ms and NEXT 60ms
Write-Host "jump  count | var30: min   p25   med   p75   max | var60: med"
foreach ($j in 8..20) {
    $vars30 = New-Object System.Collections.ArrayList
    $vars60 = New-Object System.Collections.ArrayList
    for ($i = 1; $i -lt $n; $i++) {
        if ($rows[$i].sn -ne 1) { continue }
        $jump = [Math]::Max([Math]::Abs($rows[$i].x - $rows[$i-1].x), [Math]::Abs($rows[$i].y - $rows[$i-1].y))
        if ($jump -ne $j) { continue }
        [void]$vars30.Add((VariationAfter $i 30))
        [void]$vars60.Add((VariationAfter $i 60))
    }
    if ($vars30.Count -eq 0) { continue }
    $s = $vars30 | Sort-Object
    $med = $s[[int]($s.Count/2)]
    $p25 = $s[[int]($s.Count*0.25)]
    $p75 = $s[[int]($s.Count*0.75)]
    $s60 = $vars60 | Sort-Object
    Write-Host ("  {0,3}  {1,5} | {2,6} {3,5} {4,5} {5,5} {6,5} | {7,5}" -f $j, $vars30.Count, $s[0], $p25, $med, $p75, $s[-1], $s60[[int]($s60.Count/2)])
}

# how many consecutively-snapped frames are there (clusters)?
Write-Host ""
Write-Host "=== runs of consecutive snapped frames ==="
$runs = @{}
$i = 1; $cur = 0
while ($i -lt $n) {
    if ($rows[$i].sn -eq 1) { $cur++ } else { if ($cur -gt 0) { if (-not $runs.ContainsKey($cur)) { $runs[$cur]=0 }; $runs[$cur]++ }; $cur = 0 }
    $i++
}
$runs.Keys | Sort-Object | ForEach-Object { Write-Host ("  runLen={0,3} : {1,4}" -f $_, $runs[$_]) }

# measure the cursor backlash for a few thresholds: total px sent in the wrong direction at lifts
Write-Host ""
Write-Host "=== simulated: for each rule, backward px injected and real px lost ==="
$res = New-Object System.Collections.ArrayList
for ($i = 1; $i -lt $n; $i++) {
    if ($rows[$i].sn -ne 1) { continue }
    $jump = [Math]::Max([Math]::Abs($rows[$i].x - $rows[$i-1].x), [Math]::Abs($rows[$i].y - $rows[$i-1].y))
    $var = VariationAfter $i 40
    [void]$res.Add([pscustomobject]@{ jump=$jump; frozen=($var -le 3) })
}
foreach ($J in 8,9,10,11,12,14,16) {
    $falseKept = 0; $falseKeptPx = 0; $realLost = 0
    foreach ($r in $res) {
        if ($r.jump -lt $J) { continue }
        if ($r.frozen) { $falseKept++; $falseKeptPx += $r.jump } else { $realLost++ }
    }
    Write-Host ("  J={0,2} : lifts that would still be applied = {1,3} (={2,5} units of false cursor travel x gain)" -f $J, $falseKept, $falseKeptPx)
}
