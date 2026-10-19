// Odyssey Mouse - メインウィンドウ（設定 + 状態表示）
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace OdysseyMouse
{
    internal sealed class MainWindow : Window
    {
        readonly Engine _eng;
        Settings _s;

        // ライブ更新するコントロール
        Ellipse _dot;
        TextBlock _statusText, _hintText, _rateText, _rateCap, _outText, _outCap, _devText;
        Sparkline _spark;
        Switch _powerSwitch;
        TextBlock _powerState, _powerDesc;
        MonitorPanel _monitor;
        Segmented _tabs;
        ContentControl _host;
        Card _powerCard;
        DispatcherTimer _timer;
        int _tabIndex;
        bool _closingForReal;

        public MainWindow(Engine eng, Settings s)
        {
            _eng = eng;
            _s = s;
            Loc.Lang = s.Lang;

            Title = Loc.T("app.name");
            Width = 920; MinWidth = 820;
            Height = 820; MinHeight = 620;
            Background = Theme.Br(Theme.Bg);
            FontFamily = Theme.Font;
            FontSize = 13;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            SnapsToDevicePixels = true;

            if (AppIcon.Wpf != null) Icon = AppIcon.Wpf;
            if (Program.StartTab >= 0 && Program.StartTab <= 3) _tabIndex = Program.StartTab;

            Build();

            _timer = new DispatcherTimer(DispatcherPriority.Render);
            _timer.Interval = TimeSpan.FromMilliseconds(50);   // 20 fps
            _timer.Tick += delegate { Tick(); };
            _timer.Start();

            Closing += OnClosing;
            StateChanged += delegate { if (WindowState == WindowState.Minimized && _s.MinimizeToTray) Hide(); };
            Loaded += delegate { CenterOnCursorMonitor(); };
        }

        bool _centered;

        /// <summary>カーソルのあるモニタの中央に開く（マルチモニタでも毎回同じ場所に来る）。</summary>
        void CenterOnCursorMonitor()
        {
            if (_centered) return;
            _centered = true;
            try
            {
                var pt = WF.Control.MousePosition;
                var wa = WF.Screen.FromPoint(pt).WorkingArea;
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = wa.Left + (wa.Width - ActualWidth) / 2;
                Top = wa.Top + (wa.Height - ActualHeight) / 2;
            }
            catch { }
        }

        void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_closingForReal && _s.MinimizeToTray)
            {
                e.Cancel = true;
                Hide();
                if (TrayIcon.Instance != null)
                    TrayIcon.Instance.ShowBalloon(Loc.T("tray.balloonTitle"), Loc.T("tray.balloonBody"));
            }
        }

        /// <summary>トレイの「終了」から。設定に関係なく本当に閉じる。</summary>
        public void ForceClose()
        {
            _closingForReal = true;
            if (_timer != null) _timer.Stop();
            Close();
        }

        /// <summary>トレイから設定画面を出す。</summary>
        public void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        /// <summary>ホットキーで切り替わったときにスイッチ表示を合わせる（イベントを発火させない）。</summary>
        public void SyncPowerSwitch()
        {
            if (_powerSwitch == null) return;
            _powerSwitch.On = _s.Enabled;
            UpdatePowerLook();
        }

        // ==================================================================
        //  組み立て
        // ==================================================================
        void Rebuild() { Build(); UpdatePowerLook(); }

        void Build()
        {
            var root = new Grid();
            for (int i = 0; i < 4; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Add(root, BuildHeader(), 0);
            Add(root, BuildStatusCard(), 1);
            Add(root, BuildPowerCard(), 2);
            Add(root, BuildTabBar(), 3);

            var hostBorder = new Border { Padding = new Thickness(18, 0, 18, 0) };
            _host = new ContentControl { VerticalContentAlignment = VerticalAlignment.Top, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            hostBorder.Child = _host;
            Add(root, hostBorder, 4);

            Add(root, BuildFooter(), 5);
            Content = root;
            SelectTab(Math.Min(_tabIndex, 3));
        }

        static void Add(Grid g, UIElement e, int row) { Grid.SetRow(e, row); g.Children.Add(e); }

        FrameworkElement BuildHeader()
        {
            var g = new Grid { Margin = new Thickness(18, 14, 18, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = AppIcon.Image(36);
            if (icon != null) { Grid.SetColumn(icon, 0); icon.Margin = new Thickness(0, 0, 12, 0); icon.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(icon); }

            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = Loc.T("app.name"), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Theme.Br(Theme.Text) });
            sp.Children.Add(new TextBlock { Text = Loc.T("app.subtitle"), FontSize = 11.5, Foreground = Theme.Br(Theme.TextDim), Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(sp, 1); g.Children.Add(sp);

            var lang = new Segmented(new string[] { "日本語", "English" }, Loc.IsJa ? 0 : 1, 80);
            lang.VerticalAlignment = VerticalAlignment.Center;
            lang.Selected += delegate(int i)
            {
                string nl = i == 0 ? "ja" : "en";
                if (nl == _s.Lang) return;
                _s.Lang = nl; Loc.Lang = nl; _s.Save();
                Rebuild();
                if (TrayIcon.Instance != null) TrayIcon.Instance.RefreshMenu();
            };
            Grid.SetColumn(lang, 2); g.Children.Add(lang);
            return g;
        }

        FrameworkElement BuildStatusCard()
        {
            var card = new Card(null);
            card.Margin = new Thickness(18, 4, 18, 6);
            card.Padding = new Thickness(16, 14, 16, 12);

            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _dot = new Ellipse { Width = 12, Height = 12, Fill = Theme.Br(Theme.Warn), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 0) };
            Grid.SetColumn(_dot, 0); top.Children.Add(_dot);

            var left = new StackPanel();
            _statusText = new TextBlock { Text = Loc.T("status.searching"), FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Theme.Br(Theme.Text) };
            _hintText = new TextBlock { Text = "", FontSize = 11.5, Foreground = Theme.Br(Theme.TextDim), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 12, 0), MaxWidth = 470, HorizontalAlignment = HorizontalAlignment.Left };
            left.Children.Add(_statusText);
            left.Children.Add(_hintText);
            Grid.SetColumn(left, 1); top.Children.Add(left);

            var stats = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            stats.Children.Add(StatBlock(out _rateText, out _rateCap, Theme.Text));
            stats.Children.Add(StatBlock(out _outText, out _outCap, Theme.Accent));
            Grid.SetColumn(stats, 2); top.Children.Add(stats);
            card.Body.Children.Add(top);

            _devText = new TextBlock
            {
                FontSize = 10.5, FontFamily = Theme.Mono, Foreground = Theme.Br(Theme.TextDim),
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(24, 8, 0, 0), Text = ""
            };
            card.Body.Children.Add(_devText);

            _spark = new Sparkline(_eng.St) { Margin = new Thickness(24, 6, 0, 0) };
            card.Body.Children.Add(new TextBlock { Text = Loc.T("status.sparkCaption"), FontSize = 10.5, Foreground = Theme.Br(Theme.TextDim), Margin = new Thickness(24, 10, 0, 0) });
            card.Body.Children.Add(_spark);
            return card;
        }

        /// <summary>生のデバイスパスを短く整形して表示する（詳細はツールチップと診断ダンプに残す）。</summary>
        static string ShortDevicePath(string p)
        {
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(p, @"vid&([0-9a-f]+)_pid&([0-9a-f]+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string vid = m.Groups[1].Value.ToUpperInvariant();
                    string pid = m.Groups[2].Value.ToUpperInvariant();
                    if (vid.Length > 4) vid = vid.Substring(vid.Length - 4);   // "0002045e" → "045e"
                    if (pid.Length > 4) pid = pid.Substring(pid.Length - 4);
                    // {00001124-...} は Bluetooth の HID サービス UUID（USB 接続時は出ない）
                    bool bt = p.IndexOf("00001124-0000-1000-8000-00805f9b34fb", StringComparison.OrdinalIgnoreCase) >= 0
                           || p.IndexOf("bthenum", StringComparison.OrdinalIgnoreCase) >= 0;
                    return "Motion Controller  ·  " + vid + ":" + pid + (bt ? "  (Bluetooth)" : "");
                }
            }
            catch { }
            return p;
        }

        StackPanel StatBlock(out TextBlock value, out TextBlock caption, System.Windows.Media.Color c)
        {
            var sp = new StackPanel { Margin = new Thickness(22, 0, 0, 0), MinWidth = 100 };
            value = new TextBlock { Text = "—", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Theme.Br(c), HorizontalAlignment = HorizontalAlignment.Right, FontFamily = Theme.Mono };
            caption = new TextBlock { Text = "", FontSize = 10.5, Foreground = Theme.Br(Theme.TextDim), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
            sp.Children.Add(value);
            sp.Children.Add(caption);
            return sp;
        }

        FrameworkElement BuildPowerCard()
        {
            var card = new Card(null);
            _powerCard = card;
            card.Margin = new Thickness(18, 6, 18, 8);
            card.Padding = new Thickness(16, 11, 16, 11);

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _powerState = new TextBlock { Text = Loc.T("power.on"), FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Theme.Br(Theme.Text), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(_powerState);
            _powerDesc = new TextBlock { Text = Loc.T("power.descOn"), FontSize = 11.5, Foreground = Theme.Br(Theme.TextDim), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
            left.Children.Add(_powerDesc);
            Grid.SetColumn(left, 0); g.Children.Add(left);

            _powerSwitch = new Switch(_s.Enabled);
            _powerSwitch.LayoutTransform = new ScaleTransform(1.35, 1.35);
            _powerSwitch.Margin = new Thickness(0, 0, 7, 0);
            _powerSwitch.VerticalAlignment = VerticalAlignment.Center;
            _powerSwitch.Checked += delegate { if (_s.Enabled) return; _s.Enabled = true; Apply(); UpdatePowerLook(); };
            _powerSwitch.Unchecked += delegate { if (!_s.Enabled) return; _s.Enabled = false; Apply(); UpdatePowerLook(); };
            Grid.SetColumn(_powerSwitch, 1); g.Children.Add(_powerSwitch);

            card.Body.Children.Add(g);
            return card;
        }

        void UpdatePowerLook()
        {
            if (_powerCard == null) return;
            bool on = _s.Enabled;
            _powerState.Text = on ? Loc.T("power.on") : Loc.T("power.off");
            _powerState.Foreground = on ? Theme.Br(Theme.Ok) : Theme.Br(Theme.TextDim);
            _powerDesc.Text = on ? Loc.T("power.descOn") : Loc.T("power.descOff");
            _powerCard.Background = on ? Theme.Br("#FFFFFF") : Theme.Br("#FAFAFC");
            _powerCard.BorderBrush = on ? Theme.Br("#BBE7CC") : Theme.Br(Theme.Line);
        }

        FrameworkElement BuildTabBar()
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 2, 18, 4) };
            _tabs = new Segmented(new string[] { Loc.T("tab.basic"), Loc.T("tab.sens"), Loc.T("tab.adv"), Loc.T("tab.about") }, Math.Min(_tabIndex, 3), 118);
            _tabs.Selected += delegate(int i) { SelectTab(i); };
            sp.Children.Add(_tabs);
            return sp;
        }

        void SelectTab(int i)
        {
            _tabIndex = i;
            FrameworkElement page;
            switch (i)
            {
                case 1: page = BuildSensitivity(); break;
                case 2: page = BuildAdvanced(); break;
                case 3: page = BuildHelp(); break;
                default: page = BuildBasic(); break;
            }
            var sv = new ScrollViewer
            {
                Content = page,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 6, 0, 12),
                Focusable = false
            };
            _host.Content = sv;
        }

        FrameworkElement BuildFooter()
        {
            var g = new Grid { Margin = new Thickness(18, 6, 18, 14) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new TextBlock { Text = Loc.T("footer.autosave"), FontSize = 11, Foreground = Theme.Br(Theme.TextDim), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(l, 0); g.Children.Add(l);
            var r = new TextBlock
            {
                Text = (Loc.IsJa ? "権限: " : "Privileges: ") + (AppEnv.IsElevated ? Loc.T("status.elevated") : Loc.T("status.notElevated")),
                FontSize = 11, Foreground = Theme.Br(Theme.TextDim), VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(r, 1); g.Children.Add(r);
            return g;
        }

        // ------------------------------------------------------------------
        //  行ヘルパ
        // ------------------------------------------------------------------
        Row Toggle(string labelKey, string descKey, bool value, Action<bool> changed, bool line)
        {
            var sw = new Switch(value);
            sw.Checked += delegate { changed(true); };
            sw.Unchecked += delegate { changed(false); };
            return new Row(Loc.T(labelKey), descKey == null ? null : Loc.T(descKey), sw, line);
        }

        FrameworkElement Slide(string labelKey, string descKey, double min, double max, double val, string unit, string fmt, double step, Action<double> changed)
        {
            return new SliderRow(Loc.T(labelKey), descKey == null ? null : Loc.T(descKey), min, max, val, unit, fmt, step, changed, null);
        }

        Card Box(string titleKey, string subKey, params UIElement[] rows)
        {
            var c = new Card(Loc.T(titleKey));
            c.Margin = new Thickness(0, 0, 0, 12);
            if (subKey != null)
                c.Body.Children.Add(new TextBlock { Text = Loc.T(subKey), FontSize = 11.5, Foreground = Theme.Br(Theme.TextDim), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) });
            foreach (var r in rows) c.Body.Children.Add(r);
            return c;
        }

        /// <summary>右寄せのボタンを 1 つ持つ行。</summary>
        Row ButtonRow(string labelKey, string descKey, string buttonText, Action click, bool line)
        {
            var b = ActionButton(buttonText, click, false);
            return new Row(Loc.T(labelKey), descKey == null ? null : Loc.T(descKey), b, line);
        }

        Button ActionButton(string text, Action click, bool primary)
        {
            var b = new Button
            {
                Content = text,
                FontSize = 13,
                Padding = new Thickness(14, 7, 14, 8),
                Cursor = Cursors.Hand,
                Background = primary ? Theme.Br(Theme.Accent) : Theme.Br("#FFFFFF"),
                Foreground = primary ? Brushes.White : Theme.Br(Theme.Text),
                BorderBrush = primary ? Theme.Br(Theme.Accent) : Theme.Br("#CFD8E6"),
                BorderThickness = new Thickness(1)
            };
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            var t = new ControlTemplate(typeof(Button));
            t.VisualTree = border;
            b.Template = t;
            b.Click += delegate { if (click != null) click(); };
            return b;
        }

        TextBlock Text(string s)
        {
            return new TextBlock { Text = s, FontSize = 12.5, LineHeight = 21, Foreground = Theme.Br(Theme.Text), TextWrapping = TextWrapping.Wrap };
        }

        /// <summary>設定を保存してエンジンへ反映する。</summary>
        void Apply(bool rebuild = false)
        {
            _s.Save();
            _eng.SetSettings(_s.Clone());
            if (rebuild) Rebuild();
        }

        // ------------------------------------------------------------------
        //  基本タブ
        // ------------------------------------------------------------------
        FrameworkElement BuildBasic()
        {
            var sp = new StackPanel();

            sp.Children.Add(Box("sec.input", null,
                Toggle("row.stick", "row.stick.d", _s.StickEnabled, delegate(bool b) { _s.StickEnabled = b; Apply(); }, true),
                Toggle("row.pad", "row.pad.d", _s.PadEnabled, delegate(bool b) { _s.PadEnabled = b; Apply(); }, true),
                Toggle("row.tap", "row.tap.d", _s.TapClick, delegate(bool b) { _s.TapClick = b; Apply(); }, true),
                Toggle("row.clamp", "row.clamp.d", _s.CursorClamp, delegate(bool b) { _s.CursorClamp = b; Apply(); }, false)));

            sp.Children.Add(Box("sec.buttons", null,
                Slide("row.trigger", "row.trigger.d", 0, 255, _s.TriggerThreshold, "", "0", 5, delegate(double v) { _s.TriggerThreshold = (int)Math.Round(v); Apply(); }),
                Slide("row.wheelRate", "row.wheelRate.d", 2, 60, _s.WheelRate, Loc.T("unit.notch"), "0.#", 1, delegate(double v) { _s.WheelRate = v; Apply(); })));

            sp.Children.Add(Box("sec.haptics", null,
                Toggle("row.haptics", "row.haptics.d", _s.HapticsOnClick, delegate(bool b) { _s.HapticsOnClick = b; Apply(); }, true),
                Slide("row.hapticsStrength", null, 10, 100, _s.HapticsStrength, "%", "0", 5, delegate(double v) { _s.HapticsStrength = (int)Math.Round(v); Apply(); }),
                ButtonRow("row.hapticsTest", null, Loc.T("btn.testHaptics"), delegate { _eng.TestHaptics(_s.HapticsStrength, _s.HapticsMs); }, false)));

            sp.Children.Add(Box("sec.boost", null,
                Slide("row.boost", "row.boost.d", 1.0, 6.0, _s.BoostFactor, Loc.T("unit.x"), "0.0#", 0.1, delegate(double v) { _s.BoostFactor = v; Apply(); })));

            sp.Children.Add(Box("sec.startup", null,
                Toggle("row.startup", "row.startup.d", AppEnv.GetStartWithWindows(), delegate(bool b) { AppEnv.SetStartWithWindows(b); }, true),
                Toggle("row.tray", "row.tray.d", _s.MinimizeToTray, delegate(bool b) { _s.MinimizeToTray = b; Apply(); }, true),
                Toggle("row.hotkey", "row.hotkey.d", _s.HotkeyEnabled, delegate(bool b)
                {
                    _s.HotkeyEnabled = b; Apply();
                    if (b) HotkeyManager.Register(); else HotkeyManager.Unregister();
                }, false)));

            sp.Children.Add(Box("sec.privileges", null,
                ButtonRow("row.admin", "row.admin.d", Loc.T("btn.restartAdmin"), delegate
                {
                    if (MessageBox.Show(this, Loc.T("msg.adminAsk"), Loc.T("app.name"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        Program.RestartElevated();
                }, false)));

            return sp;
        }

        // ------------------------------------------------------------------
        //  感度タブ
        // ------------------------------------------------------------------
        FrameworkElement BuildSensitivity()
        {
            var sp = new StackPanel();

            var seg = new Segmented(new string[] { Loc.T("preset.standard"), Loc.T("preset.precise"), Loc.T("preset.fast") }, -1, -1);
            seg.Selected += delegate(int i)
            {
                _s.ApplyPreset(i);
                Apply(true);
                _tabIndex = 1;
            };
            seg.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(Box("preset.title", "preset.desc", seg));

            sp.Children.Add(Box("sec.stick", null,
                Slide("row.stickSens", "row.stickSens.d", 100, 4000, _s.StickSensPx, Loc.T("unit.pxSec"), "0", 50, delegate(double v) { _s.StickSensPx = v; Apply(); }),
                Slide("row.stickDead", "row.stickDead.d", 0, 900, _s.StickDeadzone, "", "0", 10, delegate(double v) { _s.StickDeadzone = (int)Math.Round(v); Apply(); }),
                Slide("row.stickCurve", "row.stickCurve.d", 1.0, 4.0, _s.StickExponent, "", "0.0#", 0.1, delegate(double v) { _s.StickExponent = Math.Round(v, 3); Apply(); })));

            sp.Children.Add(Box("sec.pad", null,
                Slide("row.padGain", "row.padGain.d", 0.5, 8.0, _s.PadGain, Loc.T("unit.pxUnit"), "0.0#", 0.1, delegate(double v) { _s.PadGain = Math.Round(v, 3); Apply(); }),
                Slide("row.padSmooth", "row.padSmooth.d", 0.0, 0.030, _s.PadSmoothTau, Loc.T("unit.sec"), "0.0000", 0.001, delegate(double v) { _s.PadSmoothTau = Math.Round(v, 5); Apply(); }),
                Slide("row.padSmoothSlow", "row.padSmoothSlow.d", 0.0, 0.060, _s.PadSmoothSlowTau, Loc.T("unit.sec"), "0.0000", 0.002, delegate(double v) { _s.PadSmoothSlowTau = Math.Round(v, 5); Apply(); }),
                Slide("row.padAccelMax", "row.padAccelMax.d", 1.0, 8.0, _s.PadAccelMax, Loc.T("unit.x"), "0.0#", 0.1, delegate(double v) { _s.PadAccelMax = Math.Round(v, 3); Apply(); }),
                Slide("row.padAccelRef", "row.padAccelRef.d", 200, 3000, _s.PadAccelRef, Loc.T("unit.pxSec"), "0", 25, delegate(double v) { _s.PadAccelRef = v; Apply(); }),
                Toggle("row.invertY", "row.invertY.d", _s.InvertY, delegate(bool b) { _s.InvertY = b; Apply(); }, false)));

            _monitor = new MonitorPanel(_eng.St);
            sp.Children.Add(Box("sec.monitor", "monitor.note", _monitor));
            return sp;
        }

        // ------------------------------------------------------------------
        //  詳細タブ
        // ------------------------------------------------------------------
        FrameworkElement BuildAdvanced()
        {
            var sp = new StackPanel();

            sp.Children.Add(Box("sec.tap", "sec.tap.d",
                Slide("row.tapMin", null, 0.0, 0.30, _s.TapMinSec, Loc.T("unit.sec"), "0.000", 0.005, delegate(double v) { _s.TapMinSec = Math.Round(v, 4); Apply(); }),
                Slide("row.tapMax", null, 0.05, 1.0, _s.TapMaxSec, Loc.T("unit.sec"), "0.00", 0.01, delegate(double v) { _s.TapMaxSec = Math.Round(v, 3); Apply(); }),
                Slide("row.tapMove", null, 2, 60, _s.TapMaxMove, Loc.T("unit.units"), "0", 1, delegate(double v) { _s.TapMaxMove = v; Apply(); })));

            var rows = new List<UIElement>();
            sp.Children.Add(Box("sec.tuning", null,
                Toggle("row.showAdv", "row.showAdv.d", _s.ShowAdvanced, delegate(bool b) { _s.ShowAdvanced = b; Apply(true); }, false)));
            if (_s.ShowAdvanced)
            {
                rows.Add(Slide("row.jumpMax", null, 1, 20, _s.PadJumpMax, Loc.T("unit.units"), "0", 1, delegate(double v) { _s.PadJumpMax = v; Apply(); }));
                rows.Add(Slide("row.snapEnd", null, 5, 60, _s.PadSnapEnd, Loc.T("unit.units"), "0", 1, delegate(double v) { _s.PadSnapEnd = v; Apply(); }));
                rows.Add(Slide("row.restBand", null, 0, 20, _s.PadRestBand, Loc.T("unit.units"), "0", 1, delegate(double v) { _s.PadRestBand = v; Apply(); }));
                rows.Add(Slide("row.padStepMax", null, 10, 200, _s.PadStepMax, Loc.T("unit.px"), "0", 5, delegate(double v) { _s.PadStepMax = v; Apply(); }));
                sp.Children.Add(Box("sec.snap", "sec.snap.d", rows.ToArray()));
            }

            var diag = new Card(Loc.T("sec.diag"));
            diag.Margin = new Thickness(0, 0, 0, 12);
            diag.Body.Children.Add(Toggle("row.logCsv", "row.logCsv.d", _s.LogPadCsv, delegate(bool b) { _s.LogPadCsv = b; Apply(); }, true));
            var btns = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var b1 = ActionButton(Loc.T("btn.openLogs"), delegate { Program.OpenPath(AppEnv.LogDir); }, false);
            var b2 = ActionButton(Loc.T("btn.openSettings"), delegate { Program.OpenPath(AppEnv.SettingsPath); }, false);
            var b3 = ActionButton(Loc.T("btn.copyDiag"), delegate
            {
                try { Clipboard.SetText(BuildDiag()); MessageBox.Show(this, Loc.T("msg.copied"), Loc.T("app.name"), MessageBoxButton.OK, MessageBoxImage.Information); }
                catch { }
            }, false);
            var b4 = ActionButton(Loc.T("btn.reset"), delegate
            {
                if (MessageBox.Show(this, Loc.T("msg.resetAsk"), Loc.T("app.name"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                var fresh = new Settings();
                fresh.Lang = _s.Lang;
                fresh.Enabled = _s.Enabled;
                _s = fresh;
                _eng.SetSettings(_s.Clone());
                _s.Save();
                Rebuild();
            }, false);
            b1.Margin = b2.Margin = b3.Margin = new Thickness(0, 0, 8, 8);
            b4.Margin = new Thickness(0, 0, 8, 8);
            btns.Children.Add(b1); btns.Children.Add(b2); btns.Children.Add(b3); btns.Children.Add(b4);
            diag.Body.Children.Add(btns);
            sp.Children.Add(diag);

            return sp;
        }

        string BuildDiag()
        {
            var st = _eng.St;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Odyssey Mouse " + AppEnv.Version);
            sb.AppendLine("lang=" + _s.Lang + "  elevated=" + AppEnv.IsElevated + "  os=" + Environment.OSVersion.VersionString);
            sb.AppendLine("state=" + st.State + "  reports/s=" + st.ReportsPerSec + "  out px/s=" + st.OutPxPerSec.ToString("0", CultureInfo.InvariantCulture));
            sb.AppendLine("center=(" + st.CenterX + "," + st.CenterY + ") valid=" + st.CenterValid + "  lastReadErr=" + st.LastReadErr);
            sb.AppendLine("device=" + st.DevicePath);
            sb.AppendLine("lastEvent=" + _eng.LastEvent);
            sb.AppendLine("csv=" + (_eng.CsvPath ?? "-"));
            sb.AppendLine();
            try { sb.AppendLine(System.IO.File.ReadAllText(AppEnv.SettingsPath)); } catch { }
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        //  使い方・情報
        // ------------------------------------------------------------------
        FrameworkElement BuildHelp()
        {
            var sp = new StackPanel();

            var map = new TextBlock
            {
                Text = Loc.T("about.howtoBody"),
                FontSize = 12,
                LineHeight = 22,
                FontFamily = Theme.Mono,
                Foreground = Theme.Br(Theme.Text),
                TextWrapping = TextWrapping.NoWrap
            };
            sp.Children.Add(Box("about.howto", null, map));
            sp.Children.Add(Box("about.limits", null, Text(Loc.T("about.limitsBody"))));
            sp.Children.Add(Box("about.trouble", null, Text(Loc.T("about.troubleBody"))));
            sp.Children.Add(Box("about.about", null, Text(Loc.T("about.aboutBody"))));

            var info = new Card(Loc.T("app.version"));
            info.Margin = new Thickness(0, 0, 0, 12);
            info.Body.Children.Add(new TextBlock
            {
                Text = Loc.T("app.name") + "  " + AppEnv.Version + "\n" + AppEnv.DataDir,
                FontSize = 11.5,
                FontFamily = Theme.Mono,
                Foreground = Theme.Br(Theme.TextDim),
                TextWrapping = TextWrapping.Wrap
            });
            var b = ActionButton(Loc.T("btn.openSettings"), delegate { Program.OpenPath(AppEnv.SettingsPath); }, false);
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.Margin = new Thickness(0, 10, 0, 0);
            info.Body.Children.Add(b);
            sp.Children.Add(info);
            return sp;
        }

        // ==================================================================
        //  定期更新
        // ==================================================================
        string _lastState;
        void Tick()
        {
            var st = _eng.St;

            string key;
            System.Windows.Media.Color col;
            string hint = "";

            if (!_s.Enabled)
            {
                key = "status.disabled"; col = Theme.TextDim;
                hint = Loc.T("power.descOff");
            }
            else if (st.State == EngineState.Calibrating) { key = "status.calibrating"; col = Theme.Warn; }
            else if (st.State == EngineState.Reconnecting)
            {
                key = "status.reconnecting"; col = Theme.Warn;
                hint = Loc.T("status.hint");
            }
            else if (st.State == EngineState.Searching)
            {
                key = "status.searching"; col = Theme.Bad;
                hint = Loc.T("status.hintNoDevice") + "  " + Loc.T("status.retry");
            }
            else { key = "status.connected"; col = Theme.Ok; }

            if (key != _lastState)
            {
                _lastState = key;
                _statusText.Text = Loc.T(key);
            }
            _dot.Fill = Theme.Br(col);
            if (hint.Length > 0)
            {
                if (_hintText.Text != hint) _hintText.Text = hint;
                _hintText.Visibility = Visibility.Visible;
            }
            else _hintText.Visibility = Visibility.Collapsed;

            _rateText.Text = st.ReportsPerSec > 0 ? st.ReportsPerSec.ToString("0", CultureInfo.InvariantCulture) : "—";
            _rateCap.Text = Loc.T("status.rate");
            _outText.Text = st.OutPxPerSec > 0.5 ? st.OutPxPerSec.ToString("0", CultureInfo.InvariantCulture) : "—";
            _outCap.Text = Loc.T("status.outRate");

            bool hasDev = st.DevicePath != null && st.DevicePath.Length > 0;
            _devText.Text = hasDev ? ShortDevicePath(st.DevicePath) : (Loc.IsJa ? "（デバイス未検出）" : "(device not found)");
            _devText.ToolTip = hasDev ? st.DevicePath : null;   // 正規のパスはツールチップに

            _spark.InvalidateVisual();
            if (_monitor != null && _tabIndex == 1) _monitor.Update(st);
        }
    }

    /// <summary>ライブモニタ（スティック / パッド / トリガー / ボタン）</summary>
    internal sealed class MonitorPanel : StackPanel
    {
        readonly Status _st;
        readonly Bar _sx = new Bar("Stick X", -100, 100, true);
        readonly Bar _sy = new Bar("Stick Y", -100, 100, true);
        readonly Bar _px = new Bar("Pad X", -100, 100, true);
        readonly Bar _py = new Bar("Pad Y", -100, 100, true);
        readonly Bar _tg = new Bar("Trigger", 0, 255, false);
        readonly Lamp _lL, _lR, _lM, _lGrip, _lMenu, _lWin, _lPad, _lScroll;
        readonly TextBlock _note;

        public MonitorPanel(Status st)
        {
            _st = st;
            _sx.Margin = new Thickness(0, 3, 0, 3);
            _sy.Margin = new Thickness(0, 3, 0, 3);
            _px.Margin = new Thickness(0, 3, 0, 3);
            _py.Margin = new Thickness(0, 3, 0, 3);
            _tg.Margin = new Thickness(0, 8, 0, 3);
            Children.Add(_sx); Children.Add(_sy); Children.Add(_px); Children.Add(_py); Children.Add(_tg);

            var lampRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            _lL = new Lamp(Loc.T("btn.l")); _lR = new Lamp(Loc.T("btn.r")); _lM = new Lamp(Loc.T("btn.m"));
            _lGrip = new Lamp(Loc.T("btn.grip")); _lMenu = new Lamp(Loc.T("btn.menu")); _lWin = new Lamp(Loc.T("btn.win"));
            _lPad = new Lamp(Loc.T("btn.pad")); _lScroll = new Lamp(Loc.T("monitor.scroll"));
            lampRow.Children.Add(_lL); lampRow.Children.Add(_lR); lampRow.Children.Add(_lM);
            lampRow.Children.Add(_lGrip); lampRow.Children.Add(_lMenu); lampRow.Children.Add(_lWin);
            lampRow.Children.Add(_lPad); lampRow.Children.Add(_lScroll);
            Children.Add(lampRow);

            _note = new TextBlock { FontSize = 11.5, Foreground = Theme.Br(Theme.TextDim), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
            Children.Add(_note);
        }

        public void Update(Status s)
        {
            double cx = s.CenterValid ? s.CenterX : 2048;
            double cy = s.CenterValid ? s.CenterY : 2048;
            _sx.Set(Norm(s.StickX - cx, 2047), s.StickX.ToString(CultureInfo.InvariantCulture));
            _sy.Set(Norm(s.StickY - cy, 2047), s.StickY.ToString(CultureInfo.InvariantCulture));

            bool padOk = s.PadX != 255 && s.PadY != 255;
            if (padOk)
            {
                _px.SetOk(); _py.SetOk();
                _px.Set(((s.PadX - 50) / 50.0) * 100.0, s.PadX.ToString(CultureInfo.InvariantCulture));
                _py.Set(((s.PadY - 50) / 50.0) * 100.0, s.PadY.ToString(CultureInfo.InvariantCulture));
            }
            else { _px.SetError("—"); _py.SetError("—"); }
            _tg.Set(s.Trig, s.Trig.ToString(CultureInfo.InvariantCulture));

            _lL.On = s.BtnL; _lR.On = s.BtnR; _lM.On = s.BtnM;
            _lGrip.On = s.BtnGrip; _lMenu.On = s.BtnMenu; _lWin.On = s.BtnWin;
            _lPad.On = s.PadSession; _lScroll.On = s.ScrollMode;

            string t = Loc.T("monitor.contact") + ": " + (s.PadTouch ? Loc.T("monitor.yes") : Loc.T("monitor.no"));
            if (s.PadSession) t += "   (" + (Loc.IsJa ? "操作中" : "active") + ")";
            if (s.Boost) t += "   " + (Loc.IsJa ? "速度ブースト" : "boost");
            if (s.SendBlocked) t += "   " + (Loc.IsJa ? "※入力を送れません（管理者権限の画面？）" : "input blocked (elevated window?)");
            _note.Text = t;
        }

        static double Norm(double v, double max)
        {
            double f = v / max * 100.0;
            if (f > 100) f = 100;
            if (f < -100) f = -100;
            return f;
        }
    }

    /// <summary>アプリのアイコン（コードで描くので .ico が無くても表示できる）。</summary>
    internal static class AppIcon
    {
        public static readonly ImageSource Wpf;
        static readonly ImageSource _drawing;

        static AppIcon()
        {
            try
            {
                const double S = 64.0;
                var dg = new DrawingGroup();
                var bg = new StreamGeometry();
                using (var c = bg.Open())
                {
                    var r = 14.0;
                    c.BeginFigure(new Point(r, 0), true, true);
                    c.LineTo(new Point(S - r, 0), true, false);
                    c.ArcTo(new Point(S, r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    c.LineTo(new Point(S, S - r), true, false);
                    c.ArcTo(new Point(S - r, S), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    c.LineTo(new Point(r, S), true, false);
                    c.ArcTo(new Point(0, S - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                    c.LineTo(new Point(0, r), true, false);
                    c.ArcTo(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
                }
                bg.Freeze();
                var grad = new LinearGradientBrush(Theme.C("#2563EB"), Theme.C("#7C3AED"), new Point(0, 0), new Point(1, 1));
                grad.Freeze();
                var d1 = new GeometryDrawing(grad, null, bg);
                d1.Freeze();

                // マウスポインタ
                var arrow = new StreamGeometry();
                using (var c = arrow.Open())
                {
                    c.BeginFigure(new Point(22, 14), true, true);
                    c.LineTo(new Point(22, 48), true, false);
                    c.LineTo(new Point(30.5, 39.5), true, false);
                    c.LineTo(new Point(36.5, 52), true, false);
                    c.LineTo(new Point(42, 49.5), true, false);
                    c.LineTo(new Point(36, 37.5), true, false);
                    c.LineTo(new Point(46, 36.5), true, false);
                }
                arrow.Freeze();
                var d2 = new GeometryDrawing(Brushes.White, null, arrow);
                d2.Freeze();

                dg.Children.Add(d1);
                dg.Children.Add(d2);
                dg.Freeze();
                _drawing = new DrawingImage(dg);
                ((DrawingImage)_drawing).Freeze();
                Wpf = _drawing;
            }
            catch { }
        }

        public static Image Image(double size)
        {
            if (_drawing == null) return null;
            return new Image { Source = _drawing, Width = size, Height = size, Stretch = Stretch.Uniform };
        }
    }
}
