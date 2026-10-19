param([Parameter(Mandatory=$true)][string[]]$Path)

foreach ($f in $Path) {
    $rows = New-Object System.Collections.ArrayList
    foreach ($l in Get-Content $f) {
        if ($l -like 'el,*') { continue }
        $p = $l.Split(',')
        if ($p.Count -lt 14) { continue }
        [void]$rows.Add([pscustomobject]@{ el=[double]$p[0]; x=[int]$p[1]; y=[int]$p[2]; a=[int]$p[5] })
    }
    Write-Host "==================== $f  (rows=$($rows.Count)) ===================="

    # dwell map for home detection
    $dwell = @{}
    $i = 0
    while ($i -lt $rows.Count) {
        $key = "$($rows[$i].x),$($rows[$i].y)"
        $j = $i
        while ($j + 1 -lt $rows.Count -and $rows[$j+1].x -eq $rows[$i].x -and $rows[$j+1].y -eq $rows[$i].y) { $j++ }
        $dur = $rows[$j].el - $rows[$i].el
        if (-not $dwell.ContainsKey($key)) { $dwell[$key] = 0.0 }
        if ($dur -gt $dwell[$key]) { $dwell[$key] = $dur }
        $i = $j + 1
    }

    Write-Host "--- histogram of per-frame |d| (max axis), both frames non-255 ---"
    $hist = @{}
    $detail = New-Object System.Collections.ArrayList
    for ($k = 1; $k -lt $rows.Count; $k++) {
        $pr = $rows[$k-1]; $r = $rows[$k]
        if ($pr.x -eq 255 -or $pr.y -eq 255 -or $r.x -eq 255 -or $r.y -eq 255) { continue }
        $d = [Math]::Max([Math]::Abs($r.x - $pr.x), [Math]::Abs($r.y - $pr.y))
        if ($d -eq 0) { continue }
        if (-not $hist.ContainsKey($d)) { $hist[$d] = 0 }
        $hist[$d]++
        $key = "$($r.x),$($r.y)"
        $dw = 0.0
        if ($dwell.ContainsKey($key)) { $dw = $dwell[$key] }
        if ($d -ge 6) { [void]$detail.Add([pscustomobject]@{ t=$r.el; px=$pr.x; py=$pr.y; x=$r.x; y=$r.y; d=$d; dwell=$dw }) }
    }
    $hist.Keys | Sort-Object | ForEach-Object {
        Write-Host ("   |d|={0,3} : {1,5}" -f $_, $hist[$_])
    }
    Write-Host "--- every frame with |d|>=6 (dwell = how long the NEW value then stays exactly) ---"
    $detail | Sort-Object d -Descending | Select-Object -First 30 | ForEach-Object {
        Write-Host ("   t={0,7:F2} ({1,3},{2,3})->({3,3},{4,3}) d={5,3} newDwell={6,6:F2}s" -f $_.t, $_.px, $_.py, $_.x, $_.y, $_.d, $_.dwell)
    }
    Write-Host ""
}
