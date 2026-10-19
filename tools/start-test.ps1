param(
    [int]$Seconds = 150,
    [string]$Csv = 'logs\pad-v5.csv'
)

$OutputEncoding = [Console]::OutputEncoding = [Text.Encoding]::UTF8

Write-Host "=== Odyssey Mouse : テスト開始待ち ==="
Write-Host ""
Write-Host "  コントローラの Windows ボタンを押して起こしてください。"
Write-Host "  (Bluetooth は繋がっていても HID 入力が生えていないことがあります)"
Write-Host "  最大 180 秒待ちます。HID が生えたら自動でテストを開始します。"
Write-Host ""

$deadline = (Get-Date).AddSeconds(180)
$dev = $null
while ((Get-Date) -lt $deadline) {
    $dev = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
           Where-Object { $_.InstanceId -match '^HID\\' -and $_.InstanceId -match '045E' -and $_.Status -eq 'OK' } |
           Select-Object -First 1
    if ($dev) { break }
    Start-Sleep -Milliseconds 700
}

if (-not $dev) {
    Write-Host "*** コントローラの HID が見つかりませんでした。"
    Write-Host "    Windows の Bluetooth 設定で「Motion controller - Right」を"
    Write-Host "    いったん削除して再ペアリングしてください。"
    exit 1
}

Write-Host ("  デバイス検出: {0}" -f $dev.InstanceId)
Write-Host ""
Write-Host "####################################################################"
Write-Host ("#  3 秒後にテスト開始 ({0} 秒間)" -f $Seconds)
Write-Host "#"
Write-Host "#   1) ゆっくり大きく往復 (止めてから逆へ動かす) を 10 回くらい"
Write-Host "#   2) 素早い往復を数回"
Write-Host "#   3) タップを 10 回 (パッド中心から少しずらして)"
Write-Host "#"
Write-Host "#   終了は Ctrl+C"
Write-Host "####################################################################"
Write-Host ""
Start-Sleep -Seconds 3

& (Join-Path $PSScriptRoot 'odyssey-mouse.ps1') -Seconds $Seconds -PadCsv $Csv -Trace
