param(
    [Parameter(Mandatory=$true)][string]$Path,
    [double]$MinPx = 4.0,     # この px 以上の逆走だけを「事件」として扱う
    [int]$MaxEvents = 6       # 表示する事件数
)

# 「逆走」= 生値の移動方向と、そのフレームで送られたカーソル移動の符号が逆。
# どの列が原因か（フィルタ遅れ / 残量の後追い / 加速）を前後フレームで確認する。

$rows = New-Object System.Collections.ArrayList
foreach ($l in Get-Content $Path) {
    if ($l -like 'el,*') { continue }
    $p = $l.Split(',')
    if ($p.Count -lt 20) { continue }
    [void]$rows.Add([pscustomobject]@{
        el=[double]$p[0]; x=[int]$p[1]; y=[int]$p[2]; noTouch=[int]$p[4]; active=[int]$p[5]
        down=[int]$p[6]; up=[int]$p[7]; jump=[double]$p[8]; toRest=[double]$p[9]
        rx=[double]$p[10]; ry=[double]$p[11]; fx=[double]$p[12]; fy=[double]$p[13]
        sx=[int]$p[14]; sy=[int]$p[15]; remx=[double]$p[18]; remy=[double]$p[19]
    })
}
Write-Host ("rows={0}" -f $rows.Count)

$events = New-Object System.Collections.ArrayList
for ($i = 1; $i -lt $rows.Count; $i++) {
    $a = $rows[$i]
    if ($a.down -ne 0 -or $a.up -ne 0 -or $a.noTouch -ne 0) { continue }
    $rx = $a.x - $rows[$i-1].x
    $ry = $a.y - $rows[$i-1].y
    # X: カーソルは生値と同方向が正しい
    if ($rx -ne 0 -and $a.sx -ne 0 -and (($rx -gt 0) -xor ($a.sx -gt 0)) -and [Math]::Abs($a.sx) -ge $MinPx) {
        [void]$events.Add([pscustomobject]@{ i=$i; ax='X'; raw=$rx; sent=$a.sx })
    }
    # Y: InvertY なのでカーソルは生値と逆方向が正しい
    if ($ry -ne 0 -and $a.sy -ne 0 -and (($ry -lt 0) -eq ($a.sy -lt 0)) -and [Math]::Abs($a.sy) -ge $MinPx) {
        [void]$events.Add([pscustomobject]@{ i=$i; ax='Y'; raw=$ry; sent=$a.sy })
    }
}

Write-Host ("逆走イベント (>= {0}px) = {1} 件" -f $MinPx, $events.Count)
Write-Host ""

# 同方向の連続はまとめて 1 件として表示
foreach ($e in ($events | Sort-Object { -[Math]::Abs($_.sent) } | Select-Object -First $MaxEvents)) {
    $i = $e.i
    Write-Host ("=== {0} reverse: raw d{1}={2,+} -> sent={3,+}px  at t={4:F2}s ===" -f $e.ax, $e.ax, $e.raw, $e.sent, $rows[$i].el)
    Write-Host "      t      raw      noTouch down up  jump  toRest   filt(x,y)      sent(x,y)     rem(x,y)"
    for ($j = [Math]::Max(0, $i - 5); $j -le [Math]::Min($rows.Count - 1, $i + 5); $j++) {
        $r = $rows[$j]
        $mark = if ($j -eq $i) { ' <<<' } else { '' }
        Write-Host ("  {0,7:F3} ({1,3},{2,3})  {3}      {4}    {5}  {6,4}  {7,5:F0}  ({8,6:F1},{9,5:F1}) ({10,5},{11,5}) ({12,7:F1},{13,6:F1}){14}" -f `
            $r.el, $r.x, $r.y, $r.noTouch, $r.down, $r.up, $r.jump, $r.toRest, $r.fx, $r.fy, $r.sx, $r.sy, $r.remx, $r.remy, $mark)
    }
    Write-Host ""
}
