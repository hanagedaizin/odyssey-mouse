param([Parameter(Mandatory=$true)][string]$Path, [double]$RestBand=6.0)

$rows = New-Object System.Collections.ArrayList
foreach ($l in Get-Content $Path) {
    if ($l -like 'el,*') { continue }
    $p = $l.Split(',')
    if ($p.Count -lt 15) { continue }
    [void]$rows.Add([pscustomobject]@{ el=[double]$p[0]; x=[int]$p[1]; y=[int]$p[2]; a=[int]$p[5]; sn=[int]$p[6] })
}
$n = $rows.Count

# rest value = last value that stayed exactly constant for >= 0.10s, as of the given index
$restAt = New-Object 'object[]' $n
$curRest = $null; $runStart = 0
for ($i = 1; $i -le $n; $i++) {
    $ended = ($i -eq $n) -or ($rows[$i].x -ne $rows[$i-1].x) -or ($rows[$i].y -ne $rows[$i-1].y)
    if ($ended) {
        if (($rows[$i-1].el - $rows[$runStart].el) -ge 0.10) { $curRest = [pscustomobject]@{ x=$rows[$i-1].x; y=$rows[$i-1].y; t=$rows[$i-1].el } }
        $runStart = $i
    }
    if ($i -lt $n) { $restAt[$i] = $curRest }
}
function DistToRest($r, $i) {
    if ($null -eq $r) { return 999 }
    return [Math]::Max([Math]::Abs($r.x - $rows[$i].x), [Math]::Abs($r.y - $rows[$i].y))
}
function DistToRestFrom($r, $px, $py) {
    if ($null -eq $r) { return 999 }
    return [Math]::Max([Math]::Abs($r.x - $px), [Math]::Abs($r.y - $py))
}

# how much does the value move in the 100ms following index i (excluding the snap frame itself)?
function VariationAfter($i, $ms) {
    $t0 = $rows[$i].el; $v = 0.0; $px = $rows[$i].x; $py = $rows[$i].y
    for ($k = $i+1; $k -lt $n -and ($rows[$k].el - $t0) * 1000 -lt $ms; $k++) {
        $v += [Math]::Abs($rows[$k].x - $px) + [Math]::Abs($rows[$k].y - $py)
        $px = $rows[$k].x; $py = $rows[$k].y
    }
    return $v
}

$buckets = @{}
foreach ($k in 8..34) { $buckets[$k] = [pscustomobject]@{ jump=$k; cnt=0; liftLike=0; moveLike=0 } }

Write-Host "=== classification of each snapped frame (jump >= 8) ==="
Write-Host "  liftLike = value after the snap is basically frozen for 100ms (== released / touched down at a rest)"
Write-Host "  moveLike = value keeps changing after the snap (== the finger really moved fast)"
Write-Host ""
$details = New-Object System.Collections.ArrayList
for ($i = 1; $i -lt $n; $i++) {
    if ($rows[$i].sn -ne 1) { continue }
    $jump = [Math]::Max([Math]::Abs($rows[$i].x - $rows[$i-1].x), [Math]::Abs($rows[$i].y - $rows[$i-1].y))
    $var = VariationAfter $i 100
    $toRest = DistToRest $restAt[$i] $i
    $fromRest = DistToRestFrom $restAt[$i] $rows[$i-1].x $rows[$i-1].y
    $kind = if ($var -le 3) { 'liftLike' } else { 'moveLike' }
    if ($buckets.ContainsKey($jump)) { $buckets[$jump].cnt++; if ($kind -eq 'liftLike') { $buckets[$jump].liftLike++ } else { $buckets[$jump].moveLike++ } }
    [void]$details.Add([pscustomobject]@{ t=$rows[$i].el; jump=$jump; kind=$kind; toRest=$toRest; fromRest=$fromRest; var=$var })
}
Write-Host "  jump : count  liftLike moveLike"
$buckets.Keys | Sort-Object | ForEach-Object {
    $b = $buckets[$_]
    if ($b.cnt -gt 0) { Write-Host ("  {0,4} : {1,5}  {2,8} {3,8}" -f $b.jump, $b.cnt, $b.liftLike, $b.moveLike) }
}

Write-Host ""
Write-Host "=== would RestBand rule work?  discard when (toRest<=band) or (fromRest<=band) ==="
foreach ($band in 6,8,10,12) {
    $wrongKeep = 0; $wrongDrop = 0
    foreach ($d in $details) {
        $rule = ($d.toRest -le $band) -or ($d.fromRest -le $band)
        # ideal: drop liftLike, keep moveLike
        if ($d.kind -eq 'liftLike' -and -not $rule) { $wrongKeep++ }
        if ($d.kind -eq 'moveLike' -and $rule) { $wrongDrop++ }
    }
    Write-Host ("  band={0,2} : misses(lift kept)={1,3}   over-drops(move dropped)={2,3}" -f $band, $wrongKeep, $wrongDrop)
}

Write-Host ""
Write-Host "=== pure threshold rule: discard when jump >= J ==="
foreach ($J in 8,10,11,12,13,14,16) {
    $wrongKeep = 0; $wrongDrop = 0
    foreach ($d in $details) {
        $rule = ($d.jump -ge $J)
        if ($d.kind -eq 'liftLike' -and -not $rule) { $wrongKeep++ }
        if ($d.kind -eq 'moveLike' -and $rule) { $wrongDrop++ }
    }
    Write-Host ("  J={0,2} : lift kept (rubber)={1,3}   move dropped (stutter)={2,3}" -f $J, $wrongKeep, $wrongDrop)
}

Write-Host ""
Write-Host "=== every liftLike snap with jump <= 13 (the dangerous ones) ==="
$details | Where-Object { $_.kind -eq 'liftLike' -and $_.jump -le 13 } | Sort-Object jump | Select-Object -First 30 | ForEach-Object {
    Write-Host ("  t={0,7:F2} jump={1,3} toRest={2,4} fromRest={3,4}" -f $_.t, $_.jump, $_.toRest, $_.fromRest)
}
