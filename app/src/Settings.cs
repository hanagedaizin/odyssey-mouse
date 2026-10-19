// Odyssey Mouse - 設定モデルと保存 (%APPDATA%\OdysseyMouse\settings.ini)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace OdysseyMouse
{
    internal static class AppEnv
    {
        public const string AppName = "Odyssey Mouse";
        public const string AppId = "OdysseyMouse";
        public const string RunKeyName = "OdysseyMouse";

        public static string DataDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppId);
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
        }
        public static string LogDir
        {
            get
            {
                string d = Path.Combine(DataDir, "logs");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
        }
        public static string SettingsPath { get { return Path.Combine(DataDir, "settings.ini"); } }
        public static string InstallDir { get { return Path.GetDirectoryName(ExecutablePath); } }
        public static string ExecutablePath
        {
            get
            {
                var asm = System.Reflection.Assembly.GetEntryAssembly();
                if (asm == null) return System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                return asm.Location;
            }
        }
        public static string Version
        {
            get
            {
                var v = System.Reflection.Assembly.GetEntryAssembly();
                if (v == null) return "1.0.0";
                var a = (System.Reflection.AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(v, typeof(System.Reflection.AssemblyFileVersionAttribute));
                return a != null ? a.Version : "1.0.0";
            }
        }
        public static bool IsElevated
        {
            get
            {
                try
                {
                    var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                    var p = new System.Security.Principal.WindowsPrincipal(id);
                    return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch { return false; }
            }
        }

        // ---- Windows 起動時の自動開始 (HKCU\...\Run) ----
        public static bool GetStartWithWindows()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
            {
                if (k == null) return false;
                var v = k.GetValue(RunKeyName) as string;
                return !string.IsNullOrEmpty(v);
            }
        }
        public static void SetStartWithWindows(bool on)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (k == null) return;
                if (on) k.SetValue(RunKeyName, "\"" + ExecutablePath + "\"");
                else if (k.GetValue(RunKeyName) != null) k.DeleteValue(RunKeyName, false);
            }
        }
    }

    /// <summary>設定値。UI から変更されたら Clone() してエンジンへ渡す（1参照の差し替えで安全に反映）。</summary>
    internal sealed class Settings
    {
        // ---- 全体 ----
        public bool Enabled = true;
        public string Lang = "ja";
        public bool HotkeyEnabled = true;
        public bool MinimizeToTray = true;
        public bool ShowAdvanced = false;

        // ---- スティック ----
        public bool StickEnabled = true;
        public double StickSensPx = 1200;
        public int StickDeadzone = 350;
        public double StickExponent = 2.0;
        public double BoostFactor = 2.5;
        public int StickCenterX = 0;   // 0 = 自動
        public int StickCenterY = 0;
        public bool InvertY = true;    // 上下を反転（スティック・パッド共通）

        // ---- タッチパッド ----
        public bool PadEnabled = true;
        public double PadGain = 2.4;
        public double PadSmoothTau = 0.004;
        public double PadSmoothSlowTau = 0.030;   // 低速時の平滑時間（刻みを消す）。0 で無効
        public double PadSmoothRef = 150;         // 低速→高速の切り替え速度（パッド単位/秒）
        public double PadAccelTau = 0.040;
        public double PadAccelRef = 1100;
        public double PadAccelMax = 3.5;
        public double PadJumpMax = 6;
        public double PadSnapEnd = 30;
        public double PadRestBand = 4;
        public double PadStepMax = 80;   // 1 フレームで消費する残量の上限

        // ---- タップ ----
        public bool TapClick = true;
        public double TapMinSec = 0.04;
        public double TapMaxSec = 0.25;
        public double TapMaxMove = 12;

        // ---- ボタン ----
        public int TriggerThreshold = 100;
        public double WheelRate = 15;

        // ---- 振動 ----
        public bool HapticsOnClick = false;
        public int HapticsStrength = 60;
        public int HapticsMs = 50;

        // ---- ログ ----
        public bool LogPadCsv = false;
        public bool CursorClamp = true;

        public Settings Clone()
        {
            return (Settings)MemberwiseClone();
        }

        // ------------------------------------------------------------------
        // プリセット
        // ------------------------------------------------------------------
        public static readonly string[] PresetKeys = { "presetStandard", "presetPrecise", "presetFast" };
        public static readonly string[] PresetNames = { "標準", "精密", "速い" };

        public void ApplyPreset(int i)
        {
            switch (i)
            {
                case 1: // 精密（ゆっくり丁寧に操作したい）
                    StickSensPx = 700; StickDeadzone = 300; StickExponent = 2.0;
                    PadGain = 1.4; PadSmoothTau = 0.008; PadSmoothSlowTau = 0.045; PadAccelTau = 0.060; PadAccelRef = 1400; PadAccelMax = 1.8;
                    BoostFactor = 3.0; WheelRate = 12;
                    break;
                case 2: // 速い（広い画面を素早く移動）
                    StickSensPx = 2000; StickDeadzone = 400; StickExponent = 2.2;
                    PadGain = 3.6; PadSmoothTau = 0.003; PadSmoothSlowTau = 0.020; PadAccelTau = 0.035; PadAccelRef = 900; PadAccelMax = 5.0;
                    BoostFactor = 2.0; WheelRate = 20;
                    break;
                default: // 標準（実機で追い込んだ既定値）
                    StickSensPx = 1200; StickDeadzone = 350; StickExponent = 2.0;
                    PadGain = 2.4; PadSmoothTau = 0.004; PadSmoothSlowTau = 0.030; PadAccelTau = 0.040; PadAccelRef = 1100; PadAccelMax = 3.5;
                    BoostFactor = 2.5; WheelRate = 15;
                    break;
            }
        }

        // ------------------------------------------------------------------
        // 保存 / 読み込み
        // ------------------------------------------------------------------
        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(AppEnv.SettingsPath)) return s;
                foreach (var raw in File.ReadAllLines(AppEnv.SettingsPath, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    s.Set(k, v);
                }
            }
            catch { /* 壊れていたら既定値で動く */ }
            return s;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Odyssey Mouse 設定ファイル");
                sb.AppendLine("# アプリの UI から変更できます。手で編集してもかまいません（次回起動時に読み込みます）。");
                sb.AppendLine();
                sb.AppendLine("# ---- 全体 ----");
                sb.AppendLine("enabled=" + B(Enabled));
                sb.AppendLine("lang=" + Lang);
                sb.AppendLine("hotkey=" + B(HotkeyEnabled));
                sb.AppendLine("minimizeToTray=" + B(MinimizeToTray));
                sb.AppendLine("showAdvanced=" + B(ShowAdvanced));
                sb.AppendLine();
                sb.AppendLine("# ---- スティック ----");
                sb.AppendLine("stickEnabled=" + B(StickEnabled));
                sb.AppendLine("stickSensPx=" + N(StickSensPx));
                sb.AppendLine("stickDeadzone=" + StickDeadzone);
                sb.AppendLine("stickExponent=" + N(StickExponent));
                sb.AppendLine("boostFactor=" + N(BoostFactor));
                sb.AppendLine("stickCenterX=" + StickCenterX);
                sb.AppendLine("stickCenterY=" + StickCenterY);
                sb.AppendLine("invertY=" + B(InvertY));
                sb.AppendLine();
                sb.AppendLine("# ---- タッチパッド ----");
                sb.AppendLine("padEnabled=" + B(PadEnabled));
                sb.AppendLine("padGain=" + N(PadGain));
                sb.AppendLine("padSmoothTau=" + N(PadSmoothTau));
                sb.AppendLine("padSmoothSlowTau=" + N(PadSmoothSlowTau));
                sb.AppendLine("padSmoothRef=" + N(PadSmoothRef));
                sb.AppendLine("padAccelTau=" + N(PadAccelTau));
                sb.AppendLine("padAccelRef=" + N(PadAccelRef));
                sb.AppendLine("padAccelMax=" + N(PadAccelMax));
                sb.AppendLine("padJumpMax=" + N(PadJumpMax));
                sb.AppendLine("padSnapEnd=" + N(PadSnapEnd));
                sb.AppendLine("padRestBand=" + N(PadRestBand));
                sb.AppendLine("padStepMax=" + N(PadStepMax));
                sb.AppendLine();
                sb.AppendLine("# ---- タップ（短く触れて離すと左クリック）----");
                sb.AppendLine("tapClick=" + B(TapClick));
                sb.AppendLine("tapMinSec=" + N(TapMinSec));
                sb.AppendLine("tapMaxSec=" + N(TapMaxSec));
                sb.AppendLine("tapMaxMove=" + N(TapMaxMove));
                sb.AppendLine();
                sb.AppendLine("# ---- ボタン ----");
                sb.AppendLine("triggerThreshold=" + TriggerThreshold);
                sb.AppendLine("wheelRate=" + N(WheelRate));
                sb.AppendLine();
                sb.AppendLine("# ---- 振動フィードバック ----");
                sb.AppendLine("hapticsOnClick=" + B(HapticsOnClick));
                sb.AppendLine("hapticsStrength=" + HapticsStrength);
                sb.AppendLine("hapticsMs=" + HapticsMs);
                sb.AppendLine();
                sb.AppendLine("# ---- その他 ----");
                sb.AppendLine("logPadCsv=" + B(LogPadCsv));
                sb.AppendLine("cursorClamp=" + B(CursorClamp));
                File.WriteAllText(AppEnv.SettingsPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        static string B(bool b) { return b ? "1" : "0"; }        static string N(double d) { return d.ToString("0.####", CultureInfo.InvariantCulture); }
        static bool PB(string s) { return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("on", StringComparison.OrdinalIgnoreCase); }

        void Set(string k, string v)
        {
            double d; int i;
            switch (k.ToLowerInvariant())
            {
                case "enabled": Enabled = PB(v); break;
                case "lang": Lang = v; break;
                case "hotkey": HotkeyEnabled = PB(v); break;
                case "minimizetotray": MinimizeToTray = PB(v); break;
                case "showadvanced": ShowAdvanced = PB(v); break;

                case "stickenabled": StickEnabled = PB(v); break;
                case "sticksenspx": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) StickSensPx = d; break;
                case "stickdeadzone": if (int.TryParse(v, out i)) StickDeadzone = i; break;
                case "stickexponent": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) StickExponent = d; break;
                case "boostfactor": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) BoostFactor = d; break;
                case "stickcenterx": if (int.TryParse(v, out i)) StickCenterX = i; break;
                case "stickcentery": if (int.TryParse(v, out i)) StickCenterY = i; break;
                case "inverty": InvertY = PB(v); break;

                case "padenabled": PadEnabled = PB(v); break;
                case "padgain": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) PadGain = d; break;
                case "padsmoothtau": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) PadSmoothTau = d; break;
                case "padsmoothslowtau": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) PadSmoothSlowTau = d; break;
                case "padsmoothref": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) PadSmoothRef = d; break;
                case "padacceltau": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) PadAccelTau = d; break;
                case "padaccelref": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) PadAccelRef = d; break;
                case "padaccelmax": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) PadAccelMax = d; break;
                case "padjumpmax": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) PadJumpMax = d; break;
                case "padsnapend": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) PadSnapEnd = d; break;
                case "padrestband": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) PadRestBand = d; break;
                case "padstepmax": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) PadStepMax = d; break;

                case "tapclick": TapClick = PB(v); break;
                case "tapminsec": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) TapMinSec = d; break;
                case "tapmaxsec": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) TapMaxSec = d; break;
                case "tapmaxmove": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) TapMaxMove = d; break;

                case "triggerthreshold": if (int.TryParse(v, out i)) TriggerThreshold = i; break;
                case "wheelrate": if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) WheelRate = d; break;

                case "hapticsonclick": HapticsOnClick = PB(v); break;
                case "hapticsstrength": if (int.TryParse(v, out i)) HapticsStrength = i; break;
                case "hapticsms": if (int.TryParse(v, out i)) HapticsMs = i; break;

                case "logpadcsv": LogPadCsv = PB(v); break;
                case "cursorclamp": CursorClamp = PB(v); break;
            }
        }
    }
}
