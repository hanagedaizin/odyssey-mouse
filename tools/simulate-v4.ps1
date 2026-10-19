param(
    [Parameter(Mandatory=$true)][string]$Path,
    [double]$JumpMax = 6.0,        # リフト判定に必要な最小ジャンプ
    [double]$RestBand = 4.0,       # 休止値とみなす距離
    [double]$SmoothTau = 0.0,      # 位置フィルタ（0=なし）
    [double]$FollowTau = 0.0,      # 残量フォロワ（0=なし）
    [double]$AccelRef = 1100.0,
    [double]$AccelMax = 3.5,
    [double]$AccelTau = 0.040,     # 速度 EMA の時定数
    [double]$Gain = 2.4,
    [double]$TapMinSec = 0.04,
    [double]$TapMaxSec = 0.25,
    [double]$TapMaxMove = 12,
    [double]$SnapEnd = 999.0,
    [switch]$SessionGatedRest,     # 休止値の追従を「セッション外のときだけ」に限定
    [string]$Label = ''
)

# ---- データ読み込み ----
$rows = New-Object System.Collections.ArrayList
foreach ($l in Get-Content $Path) {
    if ($l -like 'el,*') { continue }
    $p = $l.Split(',')
    if ($p.Count -lt 20) { continue }
    [void]$rows.Add([pscustomobject]@{ el=[double]$p[0]; x=[int]$p[1]; y=[int]$p[2] })
}
$n = $rows.Count
if ($Label -eq '') { $Label = Split-Path $Path -Leaf }

# ---- 状態 ----
$prevX = -1; $prevY = -1; $prevMoved = $false
$stillSince = $rows[0].el
$restX = 49.0; $restY = 50.0
$session = $false
$tapT0 = 0.0; $tapX0 = 0; $tapY0 = 0; $tapMoved = $false
$downs = 0; $ups = 0; $taps = 0
$falseDown = 0; $falseUp = 0; $missedUp = 0
$fx = 49.0; $fy = 50.0; $pxf = 49.0; $pyf = 50.0
$remX = 0.0; $remY = 0.0; $ema = 0.0
$accX = 0.0; $accY = 0.0
$lastT = $rows[0].el
$revN = 0; $revPx = 0.0
$nPath = 0.0; $netX = 0; $netY = 0
$discardUnits = 0.0; $appliedUnits = 0.0
$upDetail = New-Object System.Collections.ArrayList

function LookAhead-Still([System.Collections.ArrayList]$r, [int]$i, [double]$sec, [int]$x, [int]$y) {
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
    elseif ($stillSec -ge 0.05 -and $x -ne 255) {
        if ((-not $SessionGatedRest) -or (-not $session)) { $restX = [double]$x; $restY = [double]$y }
    }
    $toRest = [Math]::Max([Math]::Abs($x - $restX), [Math]::Abs($y - $restY))

    $noTouch = ($x -eq 255 -or $y -eq 255)
    $down = (-not $noTouch) -and (-not $session) -and $prevFrozen -and ($jump -ge 3) -and ($stillSec -ge 0.05)
    $up   = (-not $noTouch) -and $session -and ($jump -ge $JumpMax) -and (($toRest -le $RestBand) -or ($jump -ge $SnapEnd))

    if ($down) {
        $downs++
        if (LookAhead-Still $rows $i 0.05 $x $y) { } else { $falseDown++ }
        $session = $true
        $tapT0 = $t; $tapX0 = $x; $tapY0 = $y; $tapMoved = $false
        $fx = $x; $fy = $y; $pxf = $fx; $pyf = $fy; $ema = 0.0
        if ($SmoothTau -le 0) { } # noop
    } elseif ($noTouch -or $up) {
        if ($session) {
            $ups++
            $dur = $t - $tapT0
            $genuine = LookAhead-Still $rows $i 0.05 $x $y
            if (-not $genuine) { $falseUp++ }
            [void]$upDetail.Add([pscustomobject]@{ t=$t; jump=$jump; toRest=$toRest; dur=$dur; genuine=$genuine; x=$x; y=$y })
            if ((-not $tapMoved) -and $dur -ge $TapMinSec -and $dur -lt $TapMaxSec) { $taps++ }
        }
        $session = $false
        if (-not $noTouch) { $fx = $x; $fy = $y; $pxf = $fx; $pyf = $fy; $ema = 0.0 }
    } else {
        # ---- 通常の移動 ----
        if ($session) {
            $d = [Math]::Max([Math]::Abs($x - $tapX0), [Math]::Abs($y - $tapY0))
            if ($d -gt $TapMaxMove) { $tapMoved = $true }
        }
        # 位置フィルタは毎フレーム更新（生値が止まっても収束させ、遅れを残さない）
        if ($SmoothTau -gt 0) {
            $aS = 1.0 - [Math]::Exp(-$dt / $SmoothTau)
            $fx = $fx + ($x - $fx) * $aS
            $fy = $fy + ($y - $fy) * $aS
            $dxp = $fx - $pxf; $dyp = $fy - $pyf
            $pxf = $fx; $pyf = $fy
        } else {
            $dxp = $x - $prevX; $dyp = $y - $prevY
        }
        if ($moved) { $appliedUnits += [Math]::Abs($x - $prevX) + [Math]::Abs($y - $prevY) }
        if ($dxp -ne 0.0 -or $dyp -ne 0.0) {
            $spd = [Math]::Sqrt($dxp*$dxp + $dyp*$dyp) / $dt
            $aa = [Math]::Min(1.0, $dt / $AccelTau)
            $ema = (1.0 - $aa) * $ema + $aa * $spd
            $scale = 1.0 + ($ema / $AccelRef)
            if ($scale -gt $AccelMax) { $scale = $AccelMax }
            $g = $Gain * $scale
            $remX += $dxp * $g
            $remY -= $dyp * $g     # InvertY
        }
    }
    if ($down -or $up -or $noTouch) {
        if (-not ($down -and $falseDown -eq 0)) { }
    }
    # 破棄したフレームの移動量（タッチダウン/リフト/255 の瞬間）
    if ($down -or $up -or $noTouch) { $discardUnits += [Math]::Abs($x - $prevX) + [Math]::Abs($y - $prevY) }

    # ---- 出力 ----
    $sx = 0; $sy = 0
    if ($FollowTau -gt 0) {
        if ([Math]::Abs($remX) -gt 0.05 -or [Math]::Abs($remY) -gt 0.05) {
            $aF = 1.0 - [Math]::Exp(-$dt / $FollowTau)
            $stx = $remX * $aF; $sty = $remY * $aF
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
    $netX += $sx; $netY += $sy

    # 生値と逆向きにカーソルが動いたフレーム
    $rdx = $x - $prevX; $rdy = $y - $prevY
    if ($sx -ne 0 -and $rdx -ne 0 -and (($rdx -gt 0) -xor ($sx -gt 0))) { $revN++; $revPx += [Math]::Abs($sx) }
    if ($sy -ne 0 -and $rdy -ne 0 -and (($rdy -lt 0) -eq ($sy -lt 0))) { $revN++; $revPx += [Math]::Abs($sy) }

    $prevX = $x; $prevY = $y; $prevMoved = $moved
}

Write-Host ("=== {0} ===" -f $Label)
Write-Host ("  params: smoothTau={0} followTau={1} jumpMax={2} restBand={3} sessionGated={4} accelTau={5}" -f $SmoothTau,$FollowTau,$JumpMax,$RestBand,[bool]$SessionGatedRest,$AccelTau)
Write-Host ("  downs={0} (そのうち直前が凍結でない偽={1})  ups={2} (うち直後も変化する偽={3})" -f $downs,$falseDown,$ups,$falseUp)
Write-Host ("  taps={0}" -f $taps)
Write-Host ("  cursor path={0:F0}px net=({1},{2})px" -f $nPath,$netX,$netY)
Write-Host ("  逆向きフレーム={0} 合計{1:F0}px" -f $revN,$revPx)
Write-Host ("  破棄した移動={0:F0}単位 適用した移動={1:F0}単位" -f $discardUnits,$appliedUnits)
Write-Host ("  --- リフト判定の内訳 ---")
$upDetail | ForEach-Object {
    $v = if ($_.genuine) { '本物' } else { '偽(移動を捨てた)' }
    Write-Host ("    t={0,7:F2} jump={1,4:F0} toRest={2,4:F0} hold={3:F3}s at ({4,3},{5,3})  {6}" -f $_.t,$_.jump,$_.toRest,$_.dur,$_.x,$_.y,$v)
}
Write-Host ""
