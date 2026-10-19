param([Parameter(Mandatory=$true)][string]$Path, [double]$From=0, [double]$To=10, [int]$MinGapMs=0)

$prev = $null
$lastPrint = -999.0
foreach ($l in Get-Content $Path) {
    if ($l -like 'el,*') { continue }
    $p = $l.Split(',')
    if ($p.Count -lt 15) { continue }
    $el=[double]$p[0]; $x=[int]$p[1]; $y=[int]$p[2]; $on=[int]$p[4]; $a=[int]$p[5]; $sn=[int]$p[6]
    $sx=[int]$p[9]; $sy=[int]$p[10]
    if ($el -lt $From -or $el -gt $To) { continue }
    $d = 0
    if ($null -ne $prev) { $d = [Math]::Max([Math]::Abs($x-$prev.x), [Math]::Abs($y-$prev.y)) }
    $show = $true
    if ($MinGapMs -gt 0 -and ($el - $lastPrint)*1000 -lt $MinGapMs -and $d -eq 0 -and $sx -eq 0 -and $sy -eq 0) { $show = $false }
    if ($show) {
        $tag = ''
        if ($sn -eq 1) { $tag += ' SNAP' }
        if ($on -eq 1) { $tag += ' ON(neutral)' }
        Write-Host ("  t={0,7:F3} raw=({1,3},{2,3}) d={3,3} a={4} sent=({5,4},{6,4}){7}" -f $el,$x,$y,$d,$a,$sx,$sy,$tag)
        $lastPrint = $el
    }
    $prev = [pscustomobject]@{ x=$x; y=$y }
}
