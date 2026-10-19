// Odyssey Mouse - 表示文字列 (日本語 / English)
using System.Collections.Generic;

namespace OdysseyMouse
{
    internal static class Loc
    {
        public static string Lang = "ja";
        public static bool IsJa { get { return Lang != "en"; } }

        static readonly Dictionary<string, string[]> S = new Dictionary<string, string[]>();

        static void A(string k, string ja, string en) { S[k] = new string[] { ja, en }; }

        static Loc()
        {
            // ---- 全般 ----
            A("app.name", "Odyssey Mouse", "Odyssey Mouse");
            A("app.subtitle", "Samsung Odyssey コントローラをマウスとして使う", "Use your Samsung Odyssey controller as a mouse");
            A("app.version", "バージョン", "Version");

            // ---- 状態 ----
            A("status.title", "状態", "Status");
            A("status.connected", "接続中", "Connected");
            A("status.disabled", "停止中", "Paused");
            A("status.searching", "コントローラを検索中…", "Searching for controller…");
            A("status.reconnecting", "再接続しています…", "Reconnecting…");
            A("status.calibrating", "中心位置を調整中…（触れないでください）", "Calibrating center (do not touch)…");
            A("status.rate", "レポート/秒", "reports/s");
            A("status.sparkCaption", "カーソル出力の推移（直近 8 秒）", "Cursor output over the last 8 seconds");
            A("status.outRate", "カーソル出力", "Cursor output");
            A("status.device", "デバイス", "Device");
            A("status.hint", "コントローラの Windows ボタンを押すと復帰します", "Press the Windows button on the controller to wake it");
            A("status.hintNoDevice", "コントローラの電源を入れてペアリングしてください", "Turn the controller on and pair it via Bluetooth");
            A("status.retry", "自動で再検索します", "Retrying automatically");
            A("status.elevated", "管理者として実行中", "Running as administrator");
            A("status.notElevated", "標準ユーザーとして実行中", "Running as standard user");

            // ---- 電源 ----
            A("power.title", "マウス機能", "Mouse control");
            A("power.on", "有効", "On");
            A("power.off", "無効", "Off");
            A("power.descOn", "コントローラでカーソルを操作できます", "You can move the cursor with the controller");
            A("power.descOff", "入力は送りません（モニタと接続状態は動きます）", "Input is not sent (monitor and status keep running)");

            // ---- プリセット ----
            A("preset.title", "プリセット", "Preset");
            A("preset.standard", "標準", "Standard");
            A("preset.precise", "精密", "Precise");
            A("preset.fast", "速い", "Fast");
            A("preset.desc", "感度まわりをまとめて切り替えます（あとから個別に調整できます）",
                              "Switch the sensitivity group at once (fine-tune afterwards)");

            // ---- タブ ----
            A("tab.basic", "基本", "Basic");
            A("tab.sens", "感度", "Sensitivity");
            A("tab.adv", "詳細", "Advanced");
            A("tab.about", "使い方・情報", "Help / About");

            // ---- セクション ----
            A("sec.input", "カーソル移動", "Cursor movement");
            A("sec.buttons", "ボタン", "Buttons");
            A("sec.haptics", "振動フィードバック", "Haptics");
            A("sec.boost", "速度ブースト", "Speed boost");
            A("sec.startup", "起動と常駐", "Startup");
            A("sec.privileges", "権限", "Privileges");
            A("sec.tuning", "調整", "Tuning");
            A("unit.notch", "ノッチ/秒", "notches/s");
            A("unit.sec", "秒", "s");
            A("unit.units", "単位", "units");
            A("unit.px", "px", "px");
            A("btn.testHaptics", "振動を試す", "Test vibration");
            A("row.hapticsTest", "振動の確認", "Check the vibration");
            A("row.showAdv", "上級者向けの項目を表示", "Show advanced tuning");
            A("row.showAdv.d", "機器側のスナップ除去など、通常は変更不要な値を表示します", "Reveals values you normally don't need (device snap removal etc.)");
            A("row.padStepMax", "1 フレームの最大移動量", "Max movement per frame");

            // ---- 基本 ----
            A("row.stick", "スティックでカーソル移動", "Move cursor with the stick");
            A("row.stick.d", "倒した方向へ連続的に動きます（ゲームパッドと同じ感覚）", "Moves continuously in the tilt direction");
            A("row.pad", "タッチパッドでカーソル移動", "Move cursor with the touchpad");
            A("row.pad.d", "指を滑らせた量だけ動きます（ノート PC のパッドと同じ）", "Moves by the distance you slide your finger");
            A("row.tap", "タップで左クリック", "Tap to left-click");
            A("row.tap.d", "パッドを短く叩いて離すとクリックします", "A short touch and release on the pad clicks");
            A("row.haptics", "クリック時に振動", "Vibrate on click");
            A("row.haptics.d", "クリックしたことを振動で知らせます（既定 OFF）", "Gives haptic feedback when clicking (default off)");
            A("row.hapticsStrength", "振動の強さ", "Vibration strength");
            A("row.wheelRate", "スクロール速度", "Scroll speed");
            A("row.wheelRate.d", "メニューボタンで切り替わるスクロールモードの速さ", "Speed in scroll mode (toggle with the Menu button)");
            A("row.boost", "速度ブースト倍率", "Boost factor");
            A("row.boost.d", "Windows ボタンを押している間だけ速くなります", "Hold the Windows button to move faster");
            A("row.trigger", "トリガーで左クリック（引き込み量）", "Trigger as left click (threshold)");
            A("row.trigger.d", "ドラッグ中に使えます。0〜255 で設定", "Useful while dragging. 0-255");
            A("row.clamp", "カーソルを画面内に制限", "Keep the cursor on screen");
            A("row.clamp.d", "画面の端に達したら止めます（マルチモニタ全体が対象）", "Stops at the edge of the virtual desktop");
            A("row.startup", "Windows 起動時に自動で開始", "Start automatically at sign-in");
            A("row.startup.d", "ログオンすると常駐して待機します", "Runs in the background after you sign in");
            A("row.tray", "閉じるときタスクトレイに残す", "Keep running in the tray when closed");
            A("row.tray.d", "OFF にすると × で終了します", "Turn off to quit with the close button");
            A("row.hotkey", "ホットキー Ctrl+Alt+M で有効/無効を切替", "Toggle with the hotkey Ctrl+Alt+M");
            A("row.hotkey.d", "キーボードから手を離さずに止められます", "Pause without leaving the keyboard");
            A("row.admin", "管理者として再起動", "Restart as administrator");
            A("row.admin.d", "管理者権限で動く画面にも入力を送れるようになります（UAC の確認が出ます）",
                                    "Lets input reach elevated windows (a UAC prompt appears)");
            A("btn.restartAdmin", "管理者として再起動", "Restart as administrator");
            A("msg.adminAsk", "管理者として再起動しますか？（UAC の確認が出ます）", "Restart as administrator? (a UAC prompt appears)");

            // ---- 感度 ----
            A("sec.stick", "スティック", "Stick");
            A("sec.pad", "タッチパッド", "Touchpad");
            A("sec.monitor", "ライブモニタ", "Live monitor");
            A("row.stickSens", "感度", "Speed");
            A("row.stickSens.d", "スティックを最大まで倒したときの速さ", "Cursor speed at full tilt");
            A("row.stickDead", "デッドゾーン", "Dead zone");
            A("row.stickDead.d", "中心付近の遊び。大きくすると止めやすい", "Play around the center; larger is easier to stop");
            A("row.stickCurve", "カーブ（指数）", "Curve (exponent)");
            A("row.stickCurve.d", "小さい動きを繊細にするほど大きい値", "Higher makes small movements finer");
            A("row.padGain", "感度", "Speed");
            A("row.padGain.d", "指 1 単位あたりの移動量", "Cursor pixels per unit of finger travel");
            A("row.padSmooth", "なめらかさ", "Smoothing");
            A("row.padSmooth.d", "大きくすると滑らか・小さくするとキビキビ", "Larger is smoother, smaller is snappier");
            A("row.padSmoothSlow", "低速時のなめらかさ", "Slow-move smoothing");
            A("row.padSmoothSlow.d", "ゆっくり動かすときだけ効く。大きくすると段差が消えるが少し重い", "Applies only when moving slowly. Larger removes the stepping but adds some weight");
            A("row.padAccelMax", "加速の上限", "Max acceleration");
            A("row.padAccelMax.d", "速く動かしたときに何倍まで加速するか", "How much faster it gets when you move quickly");
            A("row.padAccelRef", "加速が始まる速さ", "Acceleration threshold");
            A("row.padAccelRef.d", "小さいほど少しの動きで加速します", "Lower starts accelerating sooner");
            A("row.invertY", "上下を反転（Y 軸）", "Invert vertical (Y axis)");
            A("row.invertY.d", "スティックとタッチパッドの両方に効きます", "Applies to both stick and touchpad");
            A("unit.pxSec", "px/秒", "px/s");
            A("unit.pxUnit", "px/単位", "px/unit");
            A("unit.x", "倍", "x");
            A("monitor.stick", "スティック", "Stick");
            A("monitor.pad", "パッド", "Pad");
            A("monitor.trigger", "トリガー", "Trigger");
            A("monitor.buttons", "ボタン", "Buttons");
            A("monitor.contact", "接触", "Touch");
            A("monitor.yes", "あり", "yes");
            A("monitor.no", "なし", "no");
            A("monitor.scroll", "スクロールモード", "Scroll mode");
            A("monitor.note", "ここを開いたままコントローラを動かすと、値とボタンの反応を確認できます",
                              "Leave this open and move the controller to check values and buttons");
            A("btn.l", "左", "L");
            A("btn.r", "右", "R");
            A("btn.m", "中", "M");
            A("btn.grip", "グリップ", "Grip");
            A("btn.menu", "メニュー", "Menu");
            A("btn.win", "Windows", "Windows");
            A("btn.pad", "パッド押込", "Pad press");

            // ---- 詳細 ----
            A("sec.tap", "タップ判定", "Tap detection");
            A("sec.tap.d", "誤クリックを減らしたいときは「最短」を大きく、「許容する移動量」を小さくします",
                            "To reduce mis-clicks, raise the minimum hold and lower the allowed travel");
            A("row.tapMin", "最短の接触時間", "Minimum hold");
            A("row.tapMax", "最長の接触時間", "Maximum hold");
            A("row.tapMove", "許容する移動量（指の移動がこれを超えるとクリックしない）", "Allowed travel (beyond this it is not a tap)");
            A("sec.snap", "機器側スナップの除去（上級者向け）", "Device snap removal (advanced)");            A("sec.snap.d", "指を置く/離す瞬間に出る機器の値ジャンプを捨てる閾値。通常は変更不要です",
                            "Thresholds that discard the value jumps the device emits on touch/release. Usually no change needed");
            A("row.jumpMax", "「動いた」と見なす最小ジャンプ", "Minimum jump treated as movement");
            A("row.snapEnd", "即リフトと見なす大きなジャンプ", "Large jump treated as a release");
            A("row.restBand", "休止値と見なす幅", "Width considered the resting value");
            A("sec.diag", "診断", "Diagnostics");
            A("row.logCsv", "タッチパッドのログを CSV に記録", "Log touchpad data to CSV");
            A("row.logCsv.d", "アプリのログフォルダに pad-日時.csv を作ります（解析用）", "Creates pad-<date>.csv in the log folder (for analysis)");
            A("btn.openLogs", "ログフォルダを開く", "Open log folder");
            A("btn.openSettings", "設定ファイルを開く", "Open settings file");
            A("btn.copyDiag", "診断情報をコピー", "Copy diagnostics");
            A("btn.reset", "初期値に戻す", "Reset to defaults");
            A("msg.resetAsk", "すべての設定を初期値に戻しますか？", "Reset all settings to their defaults?");
            A("msg.resetDone", "初期値に戻しました", "Reset to defaults");
            A("msg.copied", "クリップボードにコピーしました", "Copied to the clipboard");
            A("msg.saved", "保存しました", "Saved");
            A("label.advancedOff", "「詳細」タブを表示する", "Show the Advanced tab");
            A("label.advancedOff.d", "通常は不要な設定を隠します", "Hides settings you normally don't need");

            // ---- 情報 ----
            A("about.howto", "ボタンの割り当て", "Button mapping");
            A("about.howtoBody",
                "スティック            : カーソル移動\n" +
                "タッチパッド          : カーソル移動（タップで左クリック）\n" +
                "パッド押込 / トリガー : 左ボタン（押している間ドラッグ）\n" +
                "グリップ              : 右ボタン\n" +
                "スティック押込        : 中ボタン\n" +
                "メニュー              : スクロールモード切替（スクロール中はスティックでホイール）\n" +
                "Windows               : 押している間だけ速度ブースト",
                "Stick            : cursor movement\n" +
                "Touchpad         : cursor movement (tap = left click)\n" +
                "Pad press / Trigger : left button (hold to drag)\n" +
                "Grip             : right button\n" +
                "Stick press      : middle button\n" +
                "Menu             : toggle scroll mode (stick becomes the wheel)\n" +
                "Windows          : hold for a speed boost");
            A("about.limits", "知っておいてほしいこと", "Good to know");
            A("about.limitsBody",
                "・タッチパッドの中心付近ではタップが反応しません（中心は「触れていない」と同じ値になるため）。少しずらして叩いてください。\n" +
                "・タッチパッドの分解能は約 100 段階です。細かい作業はスティックの方が有利なことがあります。\n" +
                "・管理者権限で動く画面（UAC のダイアログ等）には入力を送れません。「管理者として再起動」で解決できます。\n" +
                "・Bluetooth が不安定で接続が切れることがあります。切れたら自動で再接続します。頻発する場合はコントローラの電池（単4×2）を新品に交換してください。\n" +
                "・プレイ中のゲームによっては、この方式のカーソル移動を無視することがあります。",
                "- Taps near the center of the touchpad are ignored (the center reads the same as \"not touched\"). Tap slightly off-center.\n" +
                "- The touchpad has roughly 100 steps of resolution; the stick can be better for fine work.\n" +
                "- Input cannot reach elevated windows (e.g. UAC dialogs). Use \"Restart as administrator\".\n" +
                "- Bluetooth may drop the connection. The app reconnects automatically; if it happens often, replace the controller batteries (2x AAA).\n" +
                "- Some games ignore cursor movement injected this way.");
            A("about.about", "このアプリについて", "About this app");
            A("about.aboutBody",
                "Samsung Odyssey (Windows Mixed Reality) モーションコントローラを Bluetooth で接続し、\n" +
                "タッチパッドとスティックをマウスとして使えるようにする常駐アプリです。\n\n" +
                "・デバイスを直接読み取り（HID）、Windows の標準機能でカーソルを動かします。\n" +
                "・ドライバのインストールは不要で、元に戻すときはアンインストールするだけです。\n" +
                "・設定は %APPDATA%\\OdysseyMouse\\settings.ini に保存されます。",
                "A small resident app that lets you use a Samsung Odyssey (Windows Mixed Reality)\n" +
                "motion controller's touchpad and stick as a mouse over Bluetooth.\n\n" +
                "- Reads the device directly (HID) and moves the cursor with standard Windows APIs.\n" +
                "- No driver installation; uninstall to remove it completely.\n" +
                "- Settings are stored in %APPDATA%\\OdysseyMouse\\settings.ini.");
            A("about.trouble", "うまく動かないとき", "Troubleshooting");
            A("about.troubleBody",
                "1) 状態が「接続中」にならない\n" +
                "   Windows の Bluetooth 設定で「Motion controller」が接続されているか確認してください。\n" +
                "   コントローラの Windows ボタンを押すと復帰します。\n" +
                "2) カーソルが動かない\n" +
                "   上部の「マウス機能」が有効になっているか確認してください（Ctrl+Alt+M で切替）。\n" +
                "3) 動きが速すぎる / 遅すぎる\n" +
                "   「感度」タブで調整してください。ライブモニタを見ながら追い込めます。",
                "1) Status never becomes \"Connected\"\n" +
                "   Check that \"Motion controller\" is connected in Windows Bluetooth settings.\n" +
                "   Press the Windows button on the controller to wake it.\n" +
                "2) The cursor does not move\n" +
                "   Make sure \"Mouse control\" at the top is On (toggle with Ctrl+Alt+M).\n" +
                "3) Too fast / too slow\n" +
                "   Adjust it on the Sensitivity tab. Use the live monitor while tuning.");

            // ---- トレイ ----
            A("tray.enable", "マウス機能を有効にする", "Enable mouse control");
            A("tray.disable", "マウス機能を無効にする", "Disable mouse control");
            A("tray.settings", "設定を開く", "Open settings");
            A("tray.exit", "終了", "Quit");
            A("tray.balloonTitle", "Odyssey Mouse", "Odyssey Mouse");
            A("tray.balloonBody", "タスクトレイで動作を続けます。アイコンを右クリックで設定を開けます。",
                                  "Still running in the tray. Right-click the icon to open settings.");
            A("tray.tip", "Odyssey Mouse - クリックで設定", "Odyssey Mouse - click for settings");

            // ---- 共通 ----
            A("btn.close", "閉じる", "Close");
            A("btn.yes", "はい", "Yes");
            A("btn.no", "いいえ", "No");
            A("btn.ok", "OK", "OK");
            A("footer.autosave", "設定は変更するとすぐ保存されます", "Settings are saved as you change them");
            A("lang.ja", "日本語", "日本語");
            A("lang.en", "English", "English");
        }

        public static string T(string key)
        {
            string[] v;
            if (!S.TryGetValue(key, out v)) return key;
            return IsJa ? v[0] : v[1];
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }
    }
}
