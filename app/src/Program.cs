// Odyssey Mouse - エントリポイント / タスクトレイ / ホットキー
using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace OdysseyMouse
{
    internal static class Program
    {
        static Mutex _mutex;
        internal static string StartupArgs = "";
        internal static int StartTab = -1;

        [STAThread]
        static void Main(string[] args)
        {
            StartupArgs = args.Length > 0 ? string.Join(" ", args) : "";
            foreach (var a in args)
            {
                // 開発用: 起動時のタブを指定できる (/tab=1)
                if (a.StartsWith("/tab=", StringComparison.OrdinalIgnoreCase))
                {
                    int t;
                    if (int.TryParse(a.Substring(5), out t)) StartTab = t;
                }
            }

            // ---- 二重起動を防ぐ（管理者として再起動した直後は少し待つ）----
            bool created = false;
            for (int i = 0; i < 15; i++)
            {
                try
                {
                    _mutex = new Mutex(true, @"Local\OdysseyMouse.SingleInstance", out created);
                }
                catch { created = true; }
                if (created) break;
                Thread.Sleep(200);
            }
            if (!created)
            {
                MessageBox.Show(
                    Loc.IsJa ? "Odyssey Mouse は既に起動しています。\nタスクトレイのアイコンから設定を開いてください。"
                             : "Odyssey Mouse is already running.\nOpen the settings from the tray icon.",
                    "Odyssey Mouse", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                try
                {
                    MessageBox.Show("Odyssey Mouse error:\n" + (e.ExceptionObject != null ? e.ExceptionObject.ToString() : "?"),
                        "Odyssey Mouse", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
            };

            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs ev)
                {
                    MessageBox.Show("Odyssey Mouse error:\n" + ev.Exception.ToString(), "Odyssey Mouse", MessageBoxButton.OK, MessageBoxImage.Error);
                    ev.Handled = true;
                };
            }
            catch { }

            try
            {
                Theme.Init();
                AddGlobalStyles(app);

                var settings = Settings.Load();
                Loc.Lang = settings.Lang;
                if (!System.IO.File.Exists(AppEnv.SettingsPath)) settings.Save();   // 手で編集できるように初回に書き出す

                var engine = new Engine();
                engine.SetSettings(settings.Clone());
                engine.Start();
                EngineHost.Engine = engine;

                var win = new MainWindow(engine, settings);

                // ---- ホットキー (Ctrl+Alt+M) ----
                HotkeyManager.Init();
                HotkeyManager.Pressed += delegate
                {
                    settings.Enabled = !settings.Enabled;
                    settings.Save();
                    engine.SetSettings(settings.Clone());
                    win.SyncPowerSwitch();
                    if (TrayIcon.Instance != null) TrayIcon.Instance.RefreshMenu();
                };
                if (settings.HotkeyEnabled) HotkeyManager.Register();

                var tray = new TrayIcon(win, settings);
                TrayIcon.Instance = tray;

                app.Exit += delegate
                {
                    HotkeyManager.Unregister();
                    if (TrayIcon.Instance != null) TrayIcon.Instance.Dispose();
                    engine.Stop();
                    try { _mutex.ReleaseMutex(); } catch { }
                };

                if (!StartupArgs.Contains("/tray")) win.Show();
                else win.ShowFromTray();

                app.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Odyssey Mouse を起動できませんでした:\n" + ex.ToString(), "Odyssey Mouse", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        static void AddGlobalStyles(Application app)
        {
            var rd = new ResourceDictionary();
            var sb = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar));
            sb.Setters.Add(new Setter(System.Windows.Controls.Primitives.ScrollBar.TemplateProperty, Theme.ScrollBarTemplate));
            sb.Setters.Add(new Setter(FrameworkElement.WidthProperty, 10.0));
            sb.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
            rd[typeof(System.Windows.Controls.Primitives.ScrollBar)] = sb;
            app.Resources.MergedDictionaries.Add(rd);
        }

        /// <summary>管理者として起動し直す（UAC の確認が出る）。</summary>
        public static void RestartElevated()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(AppEnv.ExecutablePath);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                System.Diagnostics.Process.Start(psi);
                // 子プロセスがミューテックスを取れるように、少し待ってから手放して終了する
                Thread.Sleep(700);
                try { _mutex.ReleaseMutex(); } catch { }
                _mutex = null;
                Application.Current.Shutdown();
            }
            catch
            {
                // UAC をキャンセルした場合は何もしない（今のプロセスのまま動き続ける）
            }
        }

        public static void OpenPath(string path)
        {
            try
            {
                if (System.IO.Directory.Exists(path) || System.IO.File.Exists(path))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch { }
        }

        public static Icon LoadAppIcon()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (var n in asm.GetManifestResourceNames())
                {
                    if (n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var st = asm.GetManifestResourceStream(n))
                        {
                            if (st != null) return new Icon(st);
                        }
                    }
                }
            }
            catch { }
            try { return Icon.ExtractAssociatedIcon(AppEnv.ExecutablePath); }
            catch { }
            return SystemIcons.Application;
        }
    }

    /// <summary>Ctrl+Alt+M のグローバルホットキー。</summary>
    internal static class HotkeyManager
    {
        const int HotkeyId = 0x4F44;
        const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
        const uint VK_M = 0x4D;
        const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        static HwndSource _src;
        public static bool IsRegistered;
        public static event Action Pressed;

        public static void Init()
        {
            if (_src != null) return;
            var p = new HwndSourceParameters("OdysseyMouseHotkey");
            p.Width = 0; p.Height = 0;
            p.WindowStyle = 0;
            p.ParentWindow = new IntPtr(-3);   // HWND_MESSAGE
            try
            {
                _src = new HwndSource(p);
                _src.AddHook(Hook);
            }
            catch { _src = null; }
        }

        static IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == WM_HOTKEY && w.ToInt32() == HotkeyId)
            {
                if (Pressed != null) Pressed();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public static bool Register()
        {
            if (_src == null) return false;
            Unregister();
            try { IsRegistered = RegisterHotKey(_src.Handle, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_M); }
            catch { IsRegistered = false; }
            return IsRegistered;
        }

        public static void Unregister()
        {
            if (_src == null || !IsRegistered) return;
            try { UnregisterHotKey(_src.Handle, HotkeyId); } catch { }
            IsRegistered = false;
        }

        public static void SetEnabled(bool on)
        {
            if (on) Register(); else Unregister();
        }
    }

    /// <summary>タスクトレイ常駐。</summary>
    internal sealed class TrayIcon : IDisposable
    {
        public static TrayIcon Instance;

        readonly MainWindow _win;
        readonly Settings _s;
        readonly WinForms.NotifyIcon _ni;
        WinForms.ToolStripMenuItem _miToggle;

        public TrayIcon(MainWindow win, Settings s)
        {
            _win = win;
            _s = s;
            _ni = new WinForms.NotifyIcon();
            _ni.Icon = Program.LoadAppIcon();
            _ni.Visible = true;
            _ni.DoubleClick += delegate { _win.ShowFromTray(); };
            RefreshMenu();
        }

        public void RefreshMenu()
        {
            var menu = new WinForms.ContextMenuStrip();
            menu.ShowImageMargin = false;

            _miToggle = new WinForms.ToolStripMenuItem(_s.Enabled ? Loc.T("tray.disable") : Loc.T("tray.enable"));
            _miToggle.Click += delegate
            {
                _s.Enabled = !_s.Enabled;
                _s.Save();
                if (EngineHost.Engine != null) EngineHost.Engine.SetSettings(_s.Clone());
                _win.SyncPowerSwitch();
                RefreshMenu();
            };
            menu.Items.Add(_miToggle);

            var miOpen = new WinForms.ToolStripMenuItem(Loc.T("tray.settings"));
            miOpen.Font = new Font(miOpen.Font, System.Drawing.FontStyle.Bold);
            miOpen.Click += delegate { _win.ShowFromTray(); };
            menu.Items.Add(miOpen);

            menu.Items.Add(new WinForms.ToolStripSeparator());

            var miExit = new WinForms.ToolStripMenuItem(Loc.T("tray.exit"));
            miExit.Click += delegate
            {
                if (Application.Current != null) Application.Current.Shutdown();
            };
            menu.Items.Add(miExit);

            var old = _ni.ContextMenuStrip;
            _ni.ContextMenuStrip = menu;
            if (old != null) old.Dispose();

            string state = _s.Enabled ? Loc.T("power.on") : Loc.T("power.off");
            string tip = "Odyssey Mouse — " + state;
            _ni.Text = tip.Length > 62 ? tip.Substring(0, 62) : tip;
        }

        public void ShowBalloon(string title, string body)
        {
            try
            {
                _ni.BalloonTipTitle = title;
                _ni.BalloonTipText = body;
                _ni.ShowBalloonTip(4000);
            }
            catch { }
        }

        public void Dispose()
        {
            try { _ni.Visible = false; _ni.Dispose(); } catch { }
        }
    }

    /// <summary>エンジンの参照をトレイから使えるようにする薄いホルダ。</summary>
    internal static class EngineHost
    {
        public static Engine Engine;
    }
}
