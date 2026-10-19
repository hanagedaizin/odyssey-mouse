param([Parameter(Mandatory=$true)][string]$Path, [double]$FreezeSec=0.05)

$rows = New-Object System.Collections.ArrayList
foreach ($l in Get-Content $Path) {
    if ($l -like 'el,*') { continue }
    $p = $l.Split(',')
    if ($p.Count -lt 15) { continue }
    [void]$rows.Add([pscustomobject]@{ el=[double]$p[0]; x=[int]$p[1]; y=[int]$p[2]; sn=[int]$p[6] })
}
$n = $rows.Count

# rest = last value that stayed exactly constant for >= FreezeSec (as of index i)
$restAt = New-Object 'object[]' $n
$curRest = $null; $runStart = 0
for ($i = 1; $i -le $n; $i++) {
    $ended = ($i -eq $n) -or ($rows[$i].x -ne $rows[$i-1].x) -or ($rows[$i].y -ne $rows[$i-1].y)
    if ($ended) {
        if (($rows[$i-1].el - $rows[$runStart].el) -ge $FreezeSec) { $curRest = [pscustomobject]@{ x=$rows[$i-1].x; y=$rows[$i-1].y } }
        $runStart = $i
    }
    if ($i -lt $n) { $restAt[$i] = $curRest }
}

function VarAfter($i, $ms) {
    $t0 = $rows[$i].el; $v = 0.0; $px = $rows[$i].x; $py = $rows[$i].y
    $k = $i + 1
    while ($k -lt $n -and ($rows[$k].el - $t0) * 1000 -lt $ms) {
        $v += [Math]::Abs($rows[$k].x - $px) + [Math]::Abs($rows[$k].y - $py); $px=$rows[$k].x; $py=$rows[$k].y; $k++
    }
    return $v
}

$ev = New-Object System.Collections.ArrayList
for ($i = 1; $i -lt $n; $i++) {
    if ($rows[$i].sn -ne 1) { continue }
    $jump = [Math]::Max([Math]::Abs($rows[$i].x - $rows[$i-1].x), [Math]::Abs($rows[$i].y - $rows[$i-1].y))
    $prevFrozen = $false
    if ($i -ge 2) { $prevFrozen = ($rows[$i-1].x -eq $rows[$i-2].x) -and ($rows[$i-1].y -eq $rows[$i-2].y) }
    $var = VarAfter $i 40
    $toRest = 999
    if ($null -ne $restAt[$i]) { $toRest = [Math]::Max([Math]::Abs($restAt[$i].x - $rows[$i].x), [Math]::Abs($restAt[$i].y - $rows[$i].y)) }
    [void]$ev.Add([pscustomobject]@{ jump=$jump; prevFrozen=[bool]$prevFrozen; toRest=$toRest; frozen=($var -le 3); t=$rows[$i].el })
}
Write-Host ("snap events = {0}  (frozenAfter={1}, movingAfter={2})" -f $ev.Count, ($ev|Where-Object{$_.frozen}).Count, ($ev|Where-Object{-not $_.frozen}).Count)

function Eval($name, $script) {
    $badLift = 0; $badLiftPx = 0; $badMove = 0; $badMovePx = 0
    foreach ($e in $ev) {
        $drop = & $script $e
        if ($e.frozen) { if (-not $drop) { $badLift++; $badLiftPx += $e.jump } }
        else { if ($drop) { $badMove++; $badMovePx += $e.jump } }
    }
    Write-Host ("  {0,-46} : rubber left={1,3} ({2,5} units)   real move dropped={3,3} ({4,5} units)" -f $name, $badLift, $badLiftPx, $badMove, $badMovePx)
}

Write-Host ""
Write-Host "=== candidate rules (drop = discard this frame's delta) ==="
foreach ($P1 in 5,6,8) {
  foreach ($P2 in 12,14,16) {
    Eval ("A: jump>=$P1 && (prevFrozen || jump>=$P2)")   { param($e) return ($e.jump -ge $P1) -and ($e.prevFrozen -or ($e.jump -ge $P2)) }
  }
}
Write-Host ""
foreach ($P1 in 5,6,8) {
  foreach ($B in 4,6) {
    foreach ($P2 in 14,16) {
      Eval ("B: jump>=$P1 && (prevFrozen || toRest<=$B || jump>=$P2)") { param($e) return ($e.jump -ge $P1) -and ($e.prevFrozen -or ($e.toRest -le $B) -or ($e.jump -ge $P2)) }
    }
  }
}
Write-Host ""
Eval ("C: jump>=12 (current behavior)") { param($e) return ($e.jump -ge 12) }
Eval ("D: jump>=6") { param($e) return ($e.jump -ge 6) }
