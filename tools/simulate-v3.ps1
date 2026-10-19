param(
    [Parameter(Mandatory=$true)][string]$Path,
    [double]$JumpMax = 6.0,
    [double]$SnapEnd = 14.0,
    [double]$RestBand = 4.0,
    [double]$TapMinSec = 0.04,
    [double]$TapMaxSec = 0.25,
    [double]$TapMaxMove = 12,
    [double]$Gain = 2.4
)

# 前バージョンの CSV（rawX,rawY,...）を新ロジックに通して、
# タッチダウン/リフト/タップが何回出るかを再現する。

$rows = New-Object System.Collections.ArrayList
foreach ($l in Get-Content $Path) {
    if ($l -like 'el,*') { continue }
    $p = $l.Split(',')
    if ($p.Count -lt 15) { continue }
    [void]$rows.Add([pscustomobject]@{ el=[double]$p[0]; x=[int]$p[1]; y=[int]$p[2] })
}
$n = $rows.Count
Write-Host ("file={0}  rows={1}  span={2:F1}s" -f (Split-Path $Path -Leaf), $n, $rows[-1].el)
Write-Host ("params: jumpMax={0} snapEnd={1} restBand={2} tap={3}..{4}s / {5}unit" -f $JumpMax,$SnapEnd,$RestBand,$TapMinSec,$TapMaxSec,$TapMaxMove)
Write-Host ""

$prevX = -1; $prevY = -1; $prevMoved = $false
$stillSince = $rows[0].el
$restX = 0.0; $restY = 0.0; $restValid = $false
$session = $false
$tapT0 = 0.0; $tapX0 = 0; $tapY0 = 0; $tapMoved = $false

$downs = 0; $ups = 0; $taps = 0
$tapsByReason = @{ toRest=0; bigJump=0 }
$sessionList = New-Object System.Collections.ArrayList
$cur = $null
$revertUnits = 0.0; $revertEvents = 0
$discardN = 0; $discardDown = 0; $discardUp = 0; $discard255 = 0; $discardUnits = 0
$appliedN = 0; $appliedUnits = 0

for ($i = 0; $i -lt $n; $i++) {
    $r = $rows[$i]
    $x = $r.x; $y = $r.y; $t = $r.el

    $moved = ($x -ne $prevX) -or ($y -ne $prevY)
    $jump = [Math]::Max([Math]::Abs($x - $prevX), [Math]::Abs($y - $prevY))
    $prevFrozen = -not $prevMoved
    $stillSec = $t - $stillSince
    if ($moved) { $stillSince = $t }
    elseif ($stillSec -ge 0.05 -and $x -ne 255) { $restX = [double]$x; $restY = [double]$y; $restValid = $true }
    $toRest = 999.0
    if ($restValid) { $toRest = [Math]::Max([Math]::Abs($x - $restX), [Math]::Abs($y - $restY)) }

    $noTouch = ($x -eq 255 -or $y -eq 255)
    $down = (-not $noTouch) -and (-not $session) -and $prevFrozen -and ($jump -ge 3) -and ($stillSec -ge 0.05)
    $up   = (-not $noTouch) -and (-not $prevFrozen) -and ($jump -ge $JumpMax) -and (($toRest -le $RestBand) -or ($jump -ge $SnapEnd))

    if ($down) {
        $downs++
        $session = $true
        $tapT0 = $t; $tapX0 = $x; $tapY0 = $y; $tapMoved = $false
        $cur = [pscustomobject]@{ t0=$t; x0=$x; y0=$y; tEnd=$t; travel=0; durs=$null; cause='' }
    } elseif ($noTouch -or $up) {
        if ($session) {
            $ups++
            $dur = $t - $tapT0
            if ($cur) { $cur.tEnd = $t; $cur.durs = $dur; $cur.cause = $(if ($noTouch) {'255'} else {'up'}) }
            if ((-not $tapMoved) -and $dur -ge $TapMinSec -and $dur -lt $TapMaxSec) {
                $taps++
                if ($toRest -le $RestBand) { $tapsByReason.toRest++ } else { $tapsByReason.bigJump++ }
                if ($cur) { $cur.cause += '+TAP' }
            }
            if ($cur) { [void]$sessionList.Add($cur); $cur = $null }
        }
        $session = $false
    } else {
        if ($session) {
            $d = [Math]::Max([Math]::Abs($x - $tapX0), [Math]::Abs($y - $tapY0))
            if ($d -gt $TapMaxMove) { $tapMoved = $true }
            if ($cur -and $d -gt $cur.travel) { $cur.travel = $d }
        }
    }

    # 破棄したフレームの内訳（捨てた移動量の合計）
    if ($down -or $up -or $noTouch) {
        $discardN++
        $discardUnits += [Math]::Abs($x - $prevX) + [Math]::Abs($y - $prevY)
        if ($down) { $discardDown++ } elseif ($up) { $discardUp++ } else { $discard255++ }
    } elseif ($moved) {
        $appliedN++
        $appliedUnits += [Math]::Abs($x - $prevX) + [Math]::Abs($y - $prevY)
    }

    $prevX = $x; $prevY = $y; $prevMoved = $moved
}

Write-Host "=== 新ロジックのシミュレーション結果 ==="
Write-Host ("  touch-downs = {0}" -f $downs)
Write-Host ("  releases    = {0}" -f $ups)
Write-Host ("  TAPS fired  = {0}   (toRest判定={1}, 巨大ジャンプ判定={2})" -f $taps, $tapsByReason.toRest, $tapsByReason.bigJump)
Write-Host ("  gain換算の理論逆走 = {0} 件 / {1:F0}px  (ゲイン固定時)" -f $revertEvents, $revertUnits)
Write-Host ""
Write-Host "=== 破棄した移動 vs 適用した移動（単位=生値の合計） ==="
Write-Host ("  破棄: {0} フレーム / {1,7} 単位  (down={2} up={3} 255={4})" -f $discardN, $discardUnits, $discardDown, $discardUp, $discard255)
Write-Host ("  適用: {0} フレーム / {1,7} 単位" -f $appliedN, $appliedUnits)
Write-Host ("  => 破棄率 {0:P1} (捨てるべきリフト/タッチダウンが大半)" -f ($discardUnits / [Math]::Max(1.0, ($discardUnits + $appliedUnits))))

Write-Host ""
Write-Host "=== 全セッション一覧 (down -> up) ==="
$sessionList | ForEach-Object {
    $mark = ''
    if ($_.cause -like '*TAP*') { $mark = '  <== TAP' }
    Write-Host ("  t={0,7:F2} hold={1,6:F3}s travel={2,4} at ({3,3},{4,3})  {5}{6}" -f $_.t0, $_.durs, $_.travel, $_.x0, $_.y0, $_.cause, $mark)
}
