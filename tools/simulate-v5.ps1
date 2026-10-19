param(
    [Parameter(Mandatory=$true)][string]$CsvPath,
    [double]$JumpMax = 6.0,
    [double]$SnapEnd = 30.0,
    [double]$RestBand = 4.0,
    [double]$SmoothTau = 0.004,
    [double]$FollowTau = 0.0,
    [double]$AccelTau = 0.040,
    [double]$AccelRef = 1100.0,
    [double]$AccelMax = 3.5,
    [double]$Gain = 2.4,
    [double]$TapMinSec = 0.04,
    [double]$TapMaxSec = 0.25,
    [double]$TapMaxMove = 12,
    [double]$StepMax = 80.0,
    [switch]$Legacy,          # 旧実装（フィルタ凍結+フォロワ+即時リフト判定）を再現
    [string]$Label = ''
)

$rows = New-Object System.Collections.ArrayList
foreach ($l in Get-Content $CsvPath) {
    if ($l -like 'el,*') { continue }
    $p = $l.Split(',')
    if ($p.Count -lt 20) { continue }
    [void]$rows.Add([pscustomobject]@{ el=[double]$p[0]; x=[int]$p[1]; y=[int]$p[2] })
}
$n = $rows.Count
if ($Label -eq '') { $Label = Split-Path $CsvPath -Leaf }

$prevX = -1; $prevY = -1; $prevMoved = $false
$stillSince = $rows[0].el
$restX = 49.0; $restY = 50.0
$session = $false; $pending = $false; $pendBigJump = $false
# 旧実装は起動時キャリブレーションで休止値を取得できていた前提なので、
# Legacy では最初から有効。新実装は 255 を返す実機では取得できないため無効から始める。
$restValid = $Legacy
$tapT0 = 0.0; $tapX0 = 0; $tapY0 = 0; $tapMoved = $false
$downs=0; $ups=0; $taps=0; $falseUp=0
$fx = 49.0; $fy = 50.0; $pxf = 49.0; $pyf = 50.0
$remX = 0.0; $remY = 0.0; $ema = 0.0
$accX = 0.0; $accY = 0.0
$lastT = $rows[0].el
$revN = 0; $revPx = 0.0; $nPath = 0.0
$lossUnits = 0.0      # 破棄した生値移動
$falseUpUnits = 0.0   # 偽リフトで捨てた移動
$upLog = New-Object System.Collections.ArrayList

function StillAfter([System.Collections.ArrayList]$r, [int]$i, [double]$sec, [int]$x, [int]$y) {
    $j = $i + 1
    while ($j -lt $r.Count -and ($r[$j].el - $r[$i].el) -le $sec) {
        if ($r[$j].x -ne $x -or $r[$j].y -ne $y) { return $false }
        $j++
    }
    return $true
}

for ($i = 0; $i -lt $n; $i++) {
    $r = $rows[$i]; $x = $r.x; $y = $r.y; $t = $r.el
    $dt = $t - $lastT; $lastT = $t
    if ($dt -le 0) { $dt = 0.005 }
    if ($dt -gt 0.1) { $dt = 0.1 }

    $moved = ($x -ne $prevX) -or ($y -ne $prevY)
    $jump = [Math]::Max([Math]::Abs($x - $prevX), [Math]::Abs($y - $prevY))
    $prevFrozen = -not $prevMoved
    $stillSec = $t - $stillSince
    if ($moved) { $stillSince = $t }
    elseif ($stillSec -ge 0.05 -and $x -ne 255 -and (-not $session) -and (-not $Legacy)) {
        $restX = [double]$x; $restY = [double]$y; $restValid = $true
    }
    elseif ($stillSec -ge 0.05 -and $x -ne 255 -and $Legacy) {
        $restX = [double]$x; $restY = [double]$y; $restValid = $true
    }
    $toRest = 999.0
    if ($restValid) { $toRest = [Math]::Max([Math]::Abs($x - $restX), [Math]::Abs($y - $restY)) }

    $noTouch = ($x -eq 255 -or $y -eq 255)
    $down = (-not $noTouch) -and (-not $session) -and $prevFrozen -and ($jump -ge 3) -and ($stillSec -ge 0.05)
    if ($Legacy) {
        $upCand = (-not $noTouch) -and (-not $prevFrozen) -and ($jump -ge $JumpMax) -and (($toRest -le $RestBand) -or ($jump -ge 14.0))
    } else {
        $upCand = (-not $noTouch) -and $session -and (-not $prevFrozen) -and ($jump -ge $JumpMax) -and (($restValid -and $toRest -le $RestBand) -or ($jump -ge $SnapEnd))
    }

    $act = 'move'
    if ($Legacy) {
        if ($down) { $act = 'down' } elseif ($noTouch -or $upCand) { $act = 'lift' }
    } else {
        if ($noTouch) { $act = 'lift' }
        elseif ($down) { $act = 'down' }
        elseif ($pending) {
            if (($restValid -and $toRest -le $RestBand) -or ($pendBigJump -and (-not $moved))) { $act = 'lift' }
            else { $pending = $false; $act = 'move' }
        }
        elseif ($upCand) { $pending = $true; $pendBigJump = ($jump -ge $SnapEnd); $act = 'pending' }
    }

    if ($act -eq 'down') {
        $downs++
        $session = $true
        $tapT0 = $t; $tapX0 = $x; $tapY0 = $y; $tapMoved = $false
        $fx = $x; $fy = $y; $pxf = $fx; $pyf = $fy; $ema = 0.0
    } elseif ($act -eq 'lift') {
        if ($session) {
            $ups++
            $dur = $t - $tapT0
            if ((-not $tapMoved) -and $dur -ge $TapMinSec -and $dur -lt $TapMaxSec) { $taps++ }
            $genuine = StillAfter $rows $i 0.05 $x $y
            if (-not $genuine) { $falseUp++; $falseUpUnits += [Math]::Abs($x - $prevX) + [Math]::Abs($y - $prevY) }
            [void]$upLog.Add([pscustomobject]@{ t=$t; jump=$jump; toRest=$toRest; dur=$dur; genuine=$genuine })
        }
        $session = $false; $pending = $false; $pendBigJump = $false
        if (-not $noTouch) { $fx = $x; $fy = $y; $pxf = $fx; $pyf = $fy }
        $ema = 0.0
    } elseif ($act -eq 'move') {
        if ($session) {
            $d = [Math]::Max([Math]::Abs($x - $tapX0), [Math]::Abs($y - $tapY0))
            if ($d -gt $TapMaxMove) { $tapMoved = $true }
        }
        $doFilter = $true
        if ($Legacy -and (-not $moved)) { $doFilter = $false }
        if ($doFilter) {
            if ($SmoothTau -gt 0) {
                $aS = 1.0 - [Math]::Exp(-$dt / $SmoothTau)
                $fx = $fx + ($x - $fx) * $aS
                $fy = $fy + ($y - $fy) * $aS
                $dxp = $fx - $pxf; $dyp = $fy - $pyf
                $pxf = $fx; $pyf = $fy
            } else {
                $dxp = $x - $prevX; $dyp = $y - $prevY
            }
            if ($dxp -ne 0.0 -or $dyp -ne 0.0) {
                $spd = [Math]::Sqrt($dxp*$dxp + $dyp*$dyp) / $dt
                $aa = [Math]::Min(1.0, $dt / $AccelTau)
                $ema = (1.0 - $aa) * $ema + $aa * $spd
                $scale = 1.0 + ($ema / $AccelRef)
                if ($scale -gt $AccelMax) { $scale = $AccelMax }
                $g = $Gain * $scale
                $remX += $dxp * $g
                $remY -= $dyp * $g
            }
        }
    }
    # 'pending' は何もしない（移動も破棄もしない）

    if ($down -or $act -eq 'lift' -or $noTouch) { $lossUnits += [Math]::Abs($x - $prevX) + [Math]::Abs($y - $prevY) }

    # ---- 出力 ----
    $sx = 0; $sy = 0
    if ($FollowTau -gt 0) {
        if ([Math]::Abs($remX) -gt 0.05 -or [Math]::Abs($remY) -gt 0.05) {
            $aF = 1.0 - [Math]::Exp(-$dt / $FollowTau)
            $stx = $remX * $aF; $sty = $remY * $aF
            if ($stx -gt $StepMax) { $stx = $StepMax } elseif ($stx -lt -$StepMax) { $stx = -$StepMax }
            if ($sty -gt $StepMax) { $sty = $StepMax } elseif ($sty -lt -$StepMax) { $sty = -$StepMax }
            $remX -= $stx; $remY -= $sty
            $accX += $stx; $accY += $sty
        }
    } else {
        $accX += $remX; $accY += $remY; $remX = 0.0; $remY = 0.0
    }
    if ($accX -gt 200) { $accX = 200 }; if ($accX -lt -200) { $accX = -200 }
    if ($accY -gt 200) { $accY = 200 }; if ($accY -lt -200) { $accY = -200 }
    $dx = [int][Math]::Round($accX); $dy = [int][Math]::Round($accY)
    if ($dx -ne 0 -or $dy -ne 0) { $accX -= $dx; $accY -= $dy; $sx = $dx; $sy = $dy }
    $nPath += [Math]::Abs($sx) + [Math]::Abs($sy)
    # 逆走の判定は「実際に生値が動いたフレーム」だけを見る。
    # 255(未接触)・タッチダウン・リフト確定のフレームは生値が機器側で飛ぶため除外する
    # (analyze-pad-v3.ps1 の集計と同じ条件に合わせる)。
    $skipRev = $noTouch -or $down -or ($act -eq 'lift')
    $rdx = $x - $prevX; $rdy = $y - $prevY
    if (-not $skipRev) {
        if ($sx -ne 0 -and $rdx -ne 0 -and (($rdx -gt 0) -xor ($sx -gt 0))) { $revN++; $revPx += [Math]::Abs($sx) }
        if ($sy -ne 0 -and $rdy -ne 0 -and (($rdy -lt 0) -eq ($sy -lt 0))) { $revN++; $revPx += [Math]::Abs($sy) }
    }

    $prevX = $x; $prevY = $y; $prevMoved = $moved
}

Write-Host ("=== {0} ===" -f $Label)
Write-Host ("  downs={0} ups={1} taps={2}" -f $downs,$ups,$taps)
Write-Host ("  リフト判定: 本物={0} 偽={1} (偽で捨てた移動={2:F0}単位)" -f ($ups-$falseUp),$falseUp,$falseUpUnits)
Write-Host ("  カーソル移動量={0:F0}px  逆向きフレーム={1} 合計{2:F0}px" -f $nPath,$revN,$revPx)
Write-Host ("  破棄した生値移動={0:F0}単位" -f $lossUnits)
Write-Host "  --- 偽リフト（本物の移動を捨てた） ---"
$upLog | Where-Object { -not $_.genuine } | ForEach-Object {
    Write-Host ("    t={0,7:F2} jump={1,4:F0} toRest={2,4:F0} hold={3:F3}s" -f $_.t,$_.jump,$_.toRest,$_.dur)
}
Write-Host ""
