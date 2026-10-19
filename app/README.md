# Odyssey Mouse

Samsung Odyssey（Windows Mixed Reality）モーションコントローラをマウス代わりに使う常駐アプリです。
PowerShell の PoC（`tools/odyssey-mouse.ps1`）を、そのままのロジックで C# に移植したものです。

**配布用の 1 ファイル**:

| ファイル | 内容 |
| --- | --- |
| `dist\OdysseyMouseSetup.exe` | インストーラー（本体を埋め込み済み・管理者権限不要） |
| `dist\OdysseyMouse.exe` | 本体（インストーラーが展開します） |

## 使い方

1. `OdysseyMouseSetup.exe` を実行してインストール
2. コントローラの **Windows ボタン**を押すと接続され、自動で中心を校准して開始します
3. タスクトレイのアイコンから設定を開けます
4. **Ctrl+Alt+M** で有効/無効を切替

既定の割当は PoC と同じです（スティック／タッチパッド＝カーソル移動、タップ＝左クリック、
タッチパッド押込・トリガー＝左、グリップ＝右、スティック押込＝中、メニュー＝スクロール切替、
Windows＝速度ブースト）。

## ビルド

```
cd app
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

必要なものだけ:

- .NET Framework 4.8 以上（OS に付属）
- C# コンパイルが動くこと（Visual Studio Build Tools の Roslyn `csc.exe`、
  なければ `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` でも可）

`.NET SDK` は不要です。参照アセンブリフォルダも不要（実 dll を直接指定します）。
オプション: `-SkipInstaller` `-SkipIcon`

## 設定ファイル

`%APPDATA%\OdysseyMouse\settings.ini`

`key=value` のフラットなテキストで、そのまま手で編集できます。
アプリを起動中の状態で編集すると、次に何か変更したときに上書きされます。

## 開発用スクリプト

| スクリプト | 用途 |
| --- | --- |
| `build.ps1` | アプリとインストーラーのビルド |
| `make-icon.ps1` | `.ico` の生成（16/32/48 + 256px PNG） |
| `dev-probe.ps1` | 起動して画面キャプチャ。UI Automation でボタンを名前で押せる |
| `dev-shot.ps1` | 簡易スクリーンショット |

`dev-probe.ps1` の例:

```
# 基本タブを開いてスクロールして撮る
powershell -File .\dev-probe.ps1 -Tab 0 -Scroll 8 -Out .\obj\shot.png

# 英語表示
powershell -File .\dev-probe.ps1 -Tab 2 -Lang en

# インストーラーのアンインストール画面
powershell -File .\dev-probe.ps1 -Exe "$env:LOCALAPPDATA\Programs\OdysseyMouse\OdysseyMouseSetup.exe" `
            -ProcName OdysseyMouseSetup -ExtraArgs /uninstall -InvokeButton "アンインストール"
```

## 動作の制限

- **管理者権限で動く画面（UAC ダイアログ等）には入力を送れません**
  （`SendInput` の仕様。管理者として実行すれば大半の画面で動きます）
- コントローラの分解能はタッチパッドが 0..100（101 段階）と粗いため、
  精密な作業は 12bit のスティック側が有利です（仕様書 §3-1 参照）

技術的な詳細は `docs\spec-odyssey-controller.md` を参照してください。
