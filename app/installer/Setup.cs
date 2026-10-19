// OdysseyMouseSetup.exe - 自己完結インストーラー / アンインストーラー
//   OdysseyMouse.exe を埋め込みリソースとして持ち、ユーザー単位でインストールする
//   （管理者権限は不要）。アンインストールは自分自身を <install>\OdysseyMouseSetup.exe
//   としてコピーし、それを /uninstall で呼ぶ。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace OdysseyMouseSetup
{
    internal static class S
    {
        public static string Lang = "ja";
        public static bool IsJa { get { return Lang != "en"; } }
        static readonly Dictionary<string, string[]> D = new Dictionary<string, string[]>();
        static void A(string k, string ja, string en) { D[k] = new string[] { ja, en }; }
        static S()
        {
            A("title", "Odyssey Mouse セットアップ", "Odyssey Mouse Setup");
            A("subtitle", "Samsung Odyssey コントローラをマウスとして使う常駐アプリをインストールします。",
                          "Installs a small resident app that lets you use a Samsung Odyssey controller as a mouse.");
            A("location", "インストール先", "Install location");
            A("perUser", "管理者権限は不要です（このユーザーのみにインストールされます）",
                         "No administrator rights are needed (installs for the current user only)");
            A("optDesktop", "デスクトップにショートカットを作成", "Create a desktop shortcut");
            A("optStart", "スタートメニューに追加", "Add to the Start menu");
            A("optAuto", "Windows 起動時に自動で開始", "Start automatically at sign-in");
            A("optLaunch", "インストール後に起動する", "Launch after installation");
            A("install", "インストール", "Install");
            A("cancel", "キャンセル", "Cancel");
            A("close", "閉じる", "Close");
            A("start", "起動する", "Launch");
            A("step1", "ファイルをコピーしています…", "Copying files…");
            A("step2", "ショートカットを作成しています…", "Creating shortcuts…");
            A("step3", "設定を登録しています…", "Registering the application…");
            A("step4", "仕上げています…", "Finishing…");
            A("done", "インストールが完了しました", "Installation complete");
            A("doneBody", "スタートメニューまたはデスクトップのショートカットから起動できます。\n常駐するとタスクトレイにアイコンが表示されます。",
                           "Start it from the Start menu or the desktop shortcut.\nIt keeps running in the notification area.");
            A("err", "エラー: ", "Error: ");
            A("alreadyTitle", "既にインストールされています", "Already installed");
            A("utitle", "Odyssey Mouse をアンインストール", "Uninstall Odyssey Mouse");
            A("ubody", "Odyssey Mouse をこのコンピューターから削除します。\nよろしいですか？",
                       "This will remove Odyssey Mouse from this computer.\nContinue?");
            A("ukeep", "設定ファイル（%APPDATA%\\OdysseyMouse）は残す", "Keep my settings (%APPDATA%\\OdysseyMouse)");
            A("uinstall", "アンインストール", "Uninstall");
            A("ustep1", "アプリを終了しています…", "Closing the application…");
            A("ustep2", "ショートカットを削除しています…", "Removing shortcuts…");
            A("ustep3", "ファイルを削除しています…", "Removing files…");
            A("udone", "アンインストールが完了しました", "Uninstall complete");
            A("udoneBody", "ご利用ありがとうございました。", "Thank you for using Odyssey Mouse.");
            A("notInstalled", "Odyssey Mouse はインストールされていません。", "Odyssey Mouse is not installed.");
        }
        public static string T(string k) { string[] v; return D.TryGetValue(k, out v) ? (IsJa ? v[0] : v[1]) : k; }
    }

    internal static class Theme
    {
        public static SolidColorBrush B(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(c); b.Freeze(); return b;
        }
        public static readonly FontFamily Font = new FontFamily("Yu Gothic UI, Segoe UI, Meiryo, MS Gothic");
        public static ControlTemplate Switch;
        public static ControlTemplate Button;
        static Theme()
        {
            Switch = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ToggleButton}'>
  <Grid Width='48' Height='26' Background='Transparent'>
    <Border x:Name='track' CornerRadius='13' Background='#CBD3E1'/>
    <Border x:Name='thumb' Width='20' Height='20' CornerRadius='10' Background='#FFFFFF' HorizontalAlignment='Left' Margin='3,0,0,0'/>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property='IsChecked' Value='True'>
      <Setter TargetName='track' Property='Background' Value='#2563EB'/>
      <Setter TargetName='thumb' Property='HorizontalAlignment' Value='Right'/>
      <Setter TargetName='thumb' Property='Margin' Value='0,0,3,0'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>");
            Button = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>
  <Border x:Name='b' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'>
    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.88'/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>");
        }
    }

    internal sealed class SetupApp
    {
        const string AppId = "OdysseyMouse";
        const string AppExe = "OdysseyMouse.exe";
        const string SetupExe = "OdysseyMouseSetup.exe";

        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\OdysseyMouse"); }
        }
        static string UninstallKey { get { return @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OdysseyMouse"; } }

        [STAThread]
        public static void Main(string[] args)
        {
            bool uninstall = false, quiet = false;
            foreach (var a in args)
            {
                if (a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) || a.Equals("-uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                else if (a.Equals("/quiet", StringComparison.OrdinalIgnoreCase) || a.Equals("/silent", StringComparison.OrdinalIgnoreCase)) quiet = true;
                else if (a.StartsWith("/lang=", StringComparison.OrdinalIgnoreCase)) S.Lang = a.Substring(6);
            }
            if (quiet)
            {
                try { if (uninstall) Uninstall(true, delegate(string s, double d) { }); } catch { }
                return;
            }
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                MessageBox.Show(e.Exception.ToString(), "OdysseyMouseSetup", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            var w = new SetupWindow(uninstall);
            w.Closed += delegate { Application.Current.Shutdown(); };
            app.Run(w);
        }

        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // ---------------- 実際の処理 ----------------
        public static string PayloadPath { get { return Path.Combine(InstallDir, AppExe); } }
        public static string SetupCopyPath { get { return Path.Combine(InstallDir, SetupExe); } }

        public static bool IsInstalled()
        {
            try { return File.Exists(PayloadPath); } catch { return false; }
        }

        public static string AppVersion()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
            catch { return "1.0.0"; }
        }

        public static void KillRunning()
        {
            foreach (var p in Process.GetProcessesByName("OdysseyMouse"))
            {
                try { p.CloseMainWindow(); p.WaitForExit(1500); } catch { }
                try { if (!p.HasExited) p.Kill(); p.WaitForExit(2500); } catch { }
            }
        }

        public static void Install(InstallOptions o, Action<string, double> progress)
        {
            KillRunning();
            progress(S.T("step1"), 0.10);
            if (!Directory.Exists(InstallDir)) Directory.CreateDirectory(InstallDir);

            // 埋め込みの OdysseyMouse.exe を書き出す
            var asm = Assembly.GetExecutingAssembly();
            Stream src = null;
            foreach (var n in asm.GetManifestResourceNames())
                if (n.EndsWith("OdysseyMouse.exe", StringComparison.OrdinalIgnoreCase)) { src = asm.GetManifestResourceStream(n); break; }
            if (src == null) throw new Exception("payload resource not found");
            string tmp = PayloadPath + ".new";
            using (src)
            using (var fs = File.Create(tmp))
            {
                var buf = new byte[64 * 1024];
                int r;
                while ((r = src.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, r);
            }
            if (File.Exists(PayloadPath)) { try { File.Delete(PayloadPath); } catch { } }
            File.Move(tmp, PayloadPath);

            // 自分自身をアンインストーラーとしてコピー
            try
            {
                var me = Assembly.GetExecutingAssembly().Location;
                if (!string.Equals(me, SetupCopyPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(SetupCopyPath)) { try { File.Delete(SetupCopyPath); } catch { } }
                    File.Copy(me, SetupCopyPath, true);
                }
            }
            catch { }
            progress(S.T("step2"), 0.45);

            if (o.DesktopShortcut) CreateShortcut(Path.Combine(DesktopDir, "Odyssey Mouse.lnk"));
            if (o.StartMenu) CreateShortcut(Path.Combine(StartMenuDir, "Odyssey Mouse.lnk"));
            progress(S.T("step3"), 0.70);

            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Odyssey Mouse");
                k.SetValue("DisplayVersion", AppVersion());
                k.SetValue("Publisher", "Odyssey Mouse");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("DisplayIcon", PayloadPath + ",0");
                k.SetValue("UninstallString", "\"" + SetupCopyPath + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + SetupCopyPath + "\" /uninstall /quiet");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                try { k.SetValue("EstimatedSize", (int)((new FileInfo(PayloadPath).Length / 1024) + (new FileInfo(SetupCopyPath).Length / 1024)), RegistryValueKind.DWord); }
                catch { }
            }
            SetAutostart(o.Autostart);
            progress(S.T("step4"), 0.92);
            Thread.Sleep(250);
            progress("", 1.0);
        }

        /// <summary>
        /// 自分自身がインストール先フォルダにいる場合は、先に別の場所へ動かす。
        /// （削除したいフォルダの中でアンインストーラーが動いていると、そのフォルダを
        ///   削除できないため。Windows は実行中の exe の改名は許可している）
        /// </summary>
        static string _detachedTmp;

        static void DetachSelf()
        {
            try
            {
                string me = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(me)) return;
                if (!me.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase)) return;
                string tmp = Path.Combine(Path.GetTempPath(), "OdysseyMouseUninstall.exe");
                try { File.Move(me, tmp); _detachedTmp = tmp; }
                catch
                {
                    // 別ボリュームなど移動できなかった場合はコピーして作り直す
                    File.Copy(me, tmp, true);
                    var psi = new ProcessStartInfo(tmp);
                    string[] a = Environment.GetCommandLineArgs();
                    for (int i = 1; i < a.Length; i++) psi.Arguments += "\"" + a[i] + "\" ";
                    psi.Arguments += "/detached";
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                    Environment.Exit(0);
                }
            }
            catch { }
        }

        static void CleanupDetached()
        {
            if (_detachedTmp == null) return;
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c ping -n 25 127.0.0.1 >nul & del /f /q \"" + _detachedTmp + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
            catch { }
        }

        public static void Uninstall(bool keepSettings, Action<string, double> progress)
        {
            DetachSelf();
            progress(S.T("ustep1"), 0.15);
            KillRunning();
            Thread.Sleep(200);
            progress(S.T("ustep2"), 0.40);
            TryDelete(Path.Combine(DesktopDir, "Odyssey Mouse.lnk"));
            TryDelete(Path.Combine(StartMenuDir, "Odyssey Mouse.lnk"));
            progress(S.T("ustep3"), 0.65);
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            SetAutostart(false);
            try { Directory.Delete(InstallDir, true); } catch { }
            if (!keepSettings)
            {
                try
                {
                    var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppId);
                    if (Directory.Exists(d)) Directory.Delete(d, true);
                }
                catch { }
            }
            progress("", 1.0);
            CleanupDetached();
        }

        public static void SetAutostart(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(AppId, "\"" + PayloadPath + "\"");
                    else if (k.GetValue(AppId) != null) k.DeleteValue(AppId, false);
                }
            }
            catch { }
        }

        public static string DesktopDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }
        public static string StartMenuDir
        {
            get
            {
                var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
        }

        static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }

        static void CreateShortcut(string lnkPath)
        {
            object sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            object lnk = sh.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath });
            var t = lnk.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { PayloadPath });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { InstallDir });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { PayloadPath + ",0" });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Odyssey Mouse" });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }
    }

    internal sealed class InstallOptions
    {
        public bool DesktopShortcut = true;
        public bool StartMenu = true;
        public bool Autostart = false;
        public bool LaunchAfter = true;
        public bool KeepSettings = true;
    }

    internal sealed class SetupWindow : Window
    {
        readonly bool _uninstall;
        readonly InstallOptions _o = new InstallOptions();
        StackPanel _host;

        public SetupWindow(bool uninstall)
        {
            _uninstall = uninstall;
            Title = uninstall ? S.T("utitle") : S.T("title");
            Width = 640; SizeToContent = SizeToContent.Height; MaxHeight = 700;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F4F6FA"));
            FontFamily = Theme.Font;
            FontSize = 13;
            UseLayoutRounding = true;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (var n in asm.GetManifestResourceNames())
                    if (n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var st = asm.GetManifestResourceStream(n))
                        using (var ico = new System.Drawing.Icon(st))
                        {
                            _iconSrc = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                            Icon = _iconSrc;
                        }
                        break;
                    }
            }
            catch { }
            if (_iconSrc == null) { try { Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(System.Drawing.SystemIcons.Application.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions()); } catch { } }
            _host = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
            Content = _host;
            if (uninstall) ShowUninstallOptions(); else ShowOptions();
            // SizeToContent で高さが変わるので、表示後に中央へ寄せる
            Loaded += delegate
            {
                try
                {
                    Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
                    Top = (SystemParameters.WorkArea.Height - ActualHeight) / 2;
                }
                catch { }
            };
        }

        // ------------------------------------------------------------------
        FrameworkElement Head(string title, string sub)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (_iconSrc != null)
            {
                var img = new Image { Source = _iconSrc, Width = 40, Height = 40, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(img, 0); g.Children.Add(img);
            }
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.Bold, Foreground = Theme.B("#141A24") });
            if (sub != null)
                sp.Children.Add(new TextBlock { Text = sub, FontSize = 11.5, Foreground = Theme.B("#69748A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            Grid.SetColumn(sp, 1); g.Children.Add(sp);
            return g;
        }

        Border Card()
        {
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Theme.B("#E3E7EF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18, 14, 18, 16),
                Margin = new Thickness(0, 0, 0, 14)
            };
        }

        ToggleButton Opt(string key, bool val, Action<bool> set)
        {
            var sw = new ToggleButton { Template = Theme.Switch, IsChecked = val, Focusable = false, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
            sw.Checked += delegate { set(true); };
            sw.Unchecked += delegate { set(false); };
            var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = new TextBlock { Text = S.T(key), FontSize = 13.5, Foreground = Theme.B("#141A24"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t, 0); g.Children.Add(t);
            Grid.SetColumn(sw, 1); g.Children.Add(sw);
            _rowHosts.Add(g);
            return sw;
        }

        readonly List<Grid> _rowHosts = new List<Grid>();
        ImageSource _iconSrc;

        Button Btn(string text, bool primary, Action click)
        {
            var b = new Button
            {
                Content = text,
                Template = Theme.Button,
                FontSize = 13.5,
                Padding = new Thickness(20, 9, 20, 10),
                MinWidth = 120,
                Cursor = Cursors.Hand,
                Background = primary ? Theme.B("#2563EB") : Brushes.White,
                Foreground = primary ? Brushes.White : Theme.B("#141A24"),
                BorderBrush = primary ? Theme.B("#2563EB") : Theme.B("#CFD8E6"),
                BorderThickness = new Thickness(1)
            };
            b.Click += delegate { if (click != null) click(); };
            return b;
        }

        StackPanel LangBar()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var pair in new string[][] { new string[] { "日本語", "ja" }, new string[] { "English", "en" } })
            {
                string code = pair[1];
                bool on = S.Lang == code;
                var b = Btn(pair[0], on, delegate
                {
                    if (S.Lang == code) return;
                    S.Lang = code;
                    if (_uninstall) ShowUninstallOptions(); else ShowOptions();
                });
                b.MinWidth = 76;
                b.Padding = new Thickness(12, 5, 12, 6);
                b.FontSize = 12;
                b.Margin = new Thickness(6, 0, 0, 0);
                sp.Children.Add(b);
            }
            return sp;
        }

        // ------------------------------------------------------------------
        void ShowOptions()
        {
            _rowHosts.Clear();
            _host.Children.Clear();
            _host.Children.Add(LangBar());
            _host.Children.Add(Head(S.T("title"), S.T("subtitle")));

            var card = Card();
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = S.T("location"), FontSize = 11.5, Foreground = Theme.B("#69748A") });
            sp.Children.Add(new TextBlock
            {
                Text = SetupApp.InstallDir,
                FontSize = 12, FontFamily = new FontFamily("Consolas, MS Gothic"),
                Foreground = Theme.B("#141A24"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 2)
            });
            sp.Children.Add(new TextBlock { Text = S.T("perUser"), FontSize = 11, Foreground = Theme.B("#69748A"), Margin = new Thickness(0, 2, 0, 10) });
            Opt("optDesktop", _o.DesktopShortcut, delegate(bool v) { _o.DesktopShortcut = v; });
            Opt("optStart", _o.StartMenu, delegate(bool v) { _o.StartMenu = v; });
            Opt("optAuto", _o.Autostart, delegate(bool v) { _o.Autostart = v; });
            Opt("optLaunch", _o.LaunchAfter, delegate(bool v) { _o.LaunchAfter = v; });
            foreach (var g in _rowHosts) sp.Children.Add(g);
            card.Child = sp;
            _host.Children.Add(card);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = Btn(S.T("cancel"), false, delegate { Application.Current.Shutdown(); });
            var go = Btn(S.T("install"), true, delegate { Run(); });
            cancel.Margin = new Thickness(0, 0, 10, 0);
            btns.Children.Add(cancel);
            btns.Children.Add(go);
            _host.Children.Add(btns);
        }

        void ShowUninstallOptions()
        {
            _host.Children.Clear();
            _host.Children.Add(LangBar());
            _host.Children.Add(Head(S.T("utitle"), null));

            var card = Card();
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = S.T("ubody"), FontSize = 13, Foreground = Theme.B("#141A24"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            if (!SetupApp.IsInstalled())
                sp.Children.Add(new TextBlock { Text = S.T("notInstalled"), FontSize = 12, Foreground = Theme.B("#EF4444"), Margin = new Thickness(0, 0, 0, 10) });
            _rowHosts.Clear();
            var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sw = new ToggleButton { Template = Theme.Switch, IsChecked = _o.KeepSettings, Focusable = false, Cursor = Cursors.Hand };
            sw.Checked += delegate { _o.KeepSettings = true; };
            sw.Unchecked += delegate { _o.KeepSettings = false; };
            g.Children.Add(new TextBlock { Text = S.T("ukeep"), FontSize = 13.5, Foreground = Theme.B("#141A24"), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(sw, 1); g.Children.Add(sw);
            sp.Children.Add(g);
            card.Child = sp;
            _host.Children.Add(card);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = Btn(S.T("cancel"), false, delegate { Application.Current.Shutdown(); });
            var go = Btn(S.T("uinstall"), true, delegate { Run(); });
            cancel.Margin = new Thickness(0, 0, 10, 0);
            btns.Children.Add(cancel);
            btns.Children.Add(go);
            _host.Children.Add(btns);
        }

        ProgressBar _bar;
        TextBlock _status;

        void ShowProgress(string first)
        {
            _host.Children.Clear();
            _host.Children.Add(Head(_uninstall ? S.T("utitle") : S.T("title"), null));
            var card = Card();
            var sp = new StackPanel();
            _status = new TextBlock { Text = first, FontSize = 13, Foreground = Theme.B("#141A24") };
            sp.Children.Add(_status);
            _bar = new ProgressBar { Height = 10, Minimum = 0, Maximum = 1, Value = 0, Margin = new Thickness(0, 12, 0, 4), Foreground = Theme.B("#2563EB"), BorderThickness = new Thickness(0) };
            _bar.Template = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ProgressBar}'>
  <Grid>
    <Border x:Name='PART_Track' CornerRadius='5' Background='#E7EAF0'/>
    <Border x:Name='PART_Indicator' CornerRadius='5' Background='#2563EB' HorizontalAlignment='Left'/>
  </Grid>
</ControlTemplate>");
            sp.Children.Add(_bar);
            card.Child = sp;
            _host.Children.Add(card);
        }

        void Run()
        {
            ShowProgress(_uninstall ? S.T("ustep1") : S.T("step1"));
            var th = new Thread(delegate ()
            {
                string err = null;
                try
                {
                    Action<string, double> prog = delegate(string txt, double v)
                    {
                        Dispatcher.Invoke(new Action(delegate
                        {
                            if (_status != null && !string.IsNullOrEmpty(txt)) _status.Text = txt;
                            if (_bar != null) _bar.Value = v;
                        }));
                    };
                    if (_uninstall) SetupApp.Uninstall(_o.KeepSettings, prog);
                    else SetupApp.Install(_o, prog);
                }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.Invoke(new Action(delegate { ShowFinish(err); }));
            });
            th.IsBackground = true;
            th.Start();
        }

        void ShowFinish(string err)
        {
            _host.Children.Clear();
            _host.Children.Add(Head(err == null ? (_uninstall ? S.T("udone") : S.T("done")) : S.T("err"), null));
            var card = Card();
            var sp = new StackPanel();
            if (err != null)
                sp.Children.Add(new TextBlock { Text = err, FontSize = 12.5, Foreground = Theme.B("#EF4444"), TextWrapping = TextWrapping.Wrap });
            else
                sp.Children.Add(new TextBlock { Text = _uninstall ? S.T("udoneBody") : S.T("doneBody"), FontSize = 13, Foreground = Theme.B("#141A24"), TextWrapping = TextWrapping.Wrap });
            card.Child = sp;
            _host.Children.Add(card);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            bool launched = false;
            if (err == null && !_uninstall && _o.LaunchAfter)
            {
                try { Process.Start(new ProcessStartInfo(SetupApp.PayloadPath) { WorkingDirectory = SetupApp.InstallDir }); launched = true; } catch { }
            }
            if (err == null && !_uninstall && !launched)
            {
                var launch = Btn(S.T("start"), false, delegate
                {
                    try { Process.Start(new ProcessStartInfo(SetupApp.PayloadPath) { WorkingDirectory = SetupApp.InstallDir }); } catch { }
                    Close();
                });
                launch.Margin = new Thickness(0, 0, 10, 0);
                btns.Children.Add(launch);
            }
            var close = Btn(S.T("close"), true, delegate { Close(); });
            btns.Children.Add(close);
            _host.Children.Add(btns);
        }
    }
}
