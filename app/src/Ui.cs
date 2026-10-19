// Odyssey Mouse - UI 部品（テーマ / スイッチ / スライダー / バー / ランプ）
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OdysseyMouse
{
    internal static class Theme
    {
        public static readonly Color Bg = C("#F4F6FA");
        public static readonly Color CardBg = Colors.White;
        public static readonly Color Line = C("#E3E7EF");
        public static readonly Color Text = C("#141A24");
        public static readonly Color TextDim = C("#69748A");
        public static readonly Color Accent = C("#2563EB");
        public static readonly Color AccentSoft = C("#E8EFFE");
        public static readonly Color Ok = C("#12B76A");
        public static readonly Color Warn = C("#F79009");
        public static readonly Color Bad = C("#EF4444");
        public static readonly Color TrackCol = C("#E7EAF0");

        static readonly Dictionary<string, SolidColorBrush> _cache = new Dictionary<string, SolidColorBrush>();

        public static Color C(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        public static SolidColorBrush B(string hex)
        {
            SolidColorBrush b;
            if (_cache.TryGetValue(hex, out b)) return b;
            b = new SolidColorBrush(C(hex));
            b.Freeze();
            _cache[hex] = b;
            return b;
        }
        public static SolidColorBrush Br(string hex) { return B(hex); }
        public static SolidColorBrush Br(Color c)
        {
            string k = c.ToString();
            SolidColorBrush b;
            if (_cache.TryGetValue(k, out b)) return b;
            b = new SolidColorBrush(c);
            b.Freeze();
            _cache[k] = b;
            return b;
        }
        public static readonly FontFamily Font = new FontFamily("Yu Gothic UI, Segoe UI, Meiryo, MS Gothic");
        public static readonly FontFamily Mono = new FontFamily("Consolas, MS Gothic");

        public static ControlTemplate SliderTemplate;
        public static ControlTemplate SwitchTemplate;
        public static ControlTemplate TabTemplate;
        public static ControlTemplate ScrollBarTemplate;

        public static void Init()
        {
            SliderTemplate = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Slider}'>
  <Grid VerticalAlignment='Center' Height='22'>
    <Border Height='6' CornerRadius='3' Background='#E7EAF0' VerticalAlignment='Center'/>
    <Track x:Name='PART_Track'>
      <Track.DecreaseRepeatButton>
        <RepeatButton Focusable='False' IsTabStop='False' Command='Slider.DecreaseLarge'>
          <RepeatButton.Template>
            <ControlTemplate TargetType='RepeatButton'>
              <Border Height='6' CornerRadius='3' Background='#2563EB' VerticalAlignment='Center'/>
            </ControlTemplate>
          </RepeatButton.Template>
        </RepeatButton>
      </Track.DecreaseRepeatButton>
      <Track.Thumb>
        <Thumb Width='18' Height='18' Focusable='False'>
          <Thumb.Template>
            <ControlTemplate TargetType='Thumb'>
              <Grid>
                <Ellipse Fill='#FFFFFF' Width='18' Height='18'>
                  <Ellipse.Effect><DropShadowEffect BlurRadius='4' ShadowDepth='1' Opacity='0.3' Direction='270'/></Ellipse.Effect>
                </Ellipse>
                <Ellipse Width='13' Height='13' Stroke='#2563EB' StrokeThickness='3.5'/>
              </Grid>
            </ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
      <Track.IncreaseRepeatButton>
        <RepeatButton Focusable='False' IsTabStop='False' Command='Slider.IncreaseLarge'>
          <RepeatButton.Template>
            <ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate>
          </RepeatButton.Template>
        </RepeatButton>
      </Track.IncreaseRepeatButton>
    </Track>
  </Grid>
</ControlTemplate>");

            SwitchTemplate = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ToggleButton}'>
  <Grid Width='54' Height='30' Background='Transparent'>
    <Border x:Name='track' CornerRadius='15' Background='#CBD3E1'/>
    <Border x:Name='thumb' Width='24' Height='24' CornerRadius='12' Background='#FFFFFF' HorizontalAlignment='Left' Margin='3,0,0,0'>
      <Border.Effect><DropShadowEffect BlurRadius='3' ShadowDepth='1' Opacity='0.35' Direction='270'/></Border.Effect>
    </Border>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property='IsChecked' Value='True'>
      <Setter TargetName='track' Property='Background' Value='#2563EB'/>
      <Setter TargetName='thumb' Property='HorizontalAlignment' Value='Right'/>
      <Setter TargetName='thumb' Property='Margin' Value='0,0,3,0'/>
    </Trigger>
    <Trigger Property='IsMouseOver' Value='True'>
      <Setter TargetName='track' Property='Opacity' Value='0.88'/>
    </Trigger>
    <Trigger Property='IsEnabled' Value='False'>
      <Setter TargetName='track' Property='Opacity' Value='0.4'/>
      <Setter TargetName='thumb' Property='Opacity' Value='0.6'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>");

            ScrollBarTemplate = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>
  <Grid Background='Transparent'>
    <Track x:Name='PART_Track' IsDirectionReversed='True'>
      <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton>
      <Track.Thumb>
        <Thumb>
          <Thumb.Template>
            <ControlTemplate TargetType='Thumb'>
              <Border Background='#C3CAD8' CornerRadius='4' Margin='2,0,2,0'/>
            </ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
      <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton>
    </Track>
  </Grid>
</ControlTemplate>");
        }
    }

    /// <summary>角丸の白いカード。Title / Body を公開する。</summary>
    internal sealed class Card : Border
    {
        public readonly StackPanel Body = new StackPanel();
        TextBlock _title;

        public Card(string title)
        {
            Background = Theme.Br(Theme.CardBg);
            BorderBrush = Theme.Br(Theme.Line);
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(12);
            Padding = new Thickness(18, 14, 18, 16);
            SnapsToDevicePixels = true;
            Body.Margin = new Thickness(0, 8, 0, 0);
            var sp = new StackPanel();
            if (!string.IsNullOrEmpty(title))
            {
                _title = new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Theme.Br(Theme.TextDim) };
                sp.Children.Add(_title);
            }
            sp.Children.Add(Body);
            Child = sp;
        }
        public void SetTitle(string t) { if (_title != null) _title.Text = t; }
    }

    /// <summary>ON/OFF スイッチ（トグルボタン）。</summary>
    internal sealed class Switch : ToggleButton
    {
        public Switch(bool on)
        {
            Template = Theme.SwitchTemplate;
            IsChecked = on;
            Focusable = false;
            Cursor = System.Windows.Input.Cursors.Hand;
        }
        public bool On { get { return IsChecked == true; } set { IsChecked = value; } }
    }

    /// <summary>行: 左に見出し+説明、右にコントロール。</summary>
    internal sealed class Row : Grid
    {
        public Row(string label, string desc, FrameworkElement control, bool bottomLine)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Margin = new Thickness(0, 0, 0, 0);
            var left = new StackPanel { Margin = new Thickness(0, 9, 14, 9), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock { Text = label, FontSize = 14, Foreground = Theme.Br(Theme.Text), TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrEmpty(desc))
                left.Children.Add(new TextBlock { Text = desc, FontSize = 11.5, Foreground = Theme.Br(Theme.TextDim), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            SetColumn(left, 0);
            Children.Add(left);
            if (control != null)
            {
                control.VerticalAlignment = VerticalAlignment.Center;                control.Margin = new Thickness(0, 9, 0, 9);
                SetColumn(control, 1);
                Children.Add(control);
            }
            if (bottomLine)
            {
                var line = new Border { Height = 1, Background = Theme.Br(Theme.Line), VerticalAlignment = VerticalAlignment.Bottom };
                SetColumn(line, 0); SetColumnSpan(line, 2);
                Children.Add(line);
            }
        }
    }

    /// <summary>スライダー行。スライダー + 現在値（数値は直接編集可）。</summary>
    internal sealed class SliderRow : Grid
    {
        public readonly Slider Slider = new Slider();
        public readonly TextBlock ValueText = new TextBlock();
        readonly string _fmt;
        readonly string _unit;
        readonly Action<double> _apply;
        double _min, _max;

        public SliderRow(string label, string desc, double min, double max, double value, string unit, string fmt, double step,
                         Action<double> onChanged, string rightNote)
        {
            _min = min; _max = max; _unit = unit; _fmt = fmt; _apply = onChanged;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lbl = new StackPanel();
            lbl.Children.Add(new TextBlock { Text = label, FontSize = 14, Foreground = Theme.Br(Theme.Text) });
            if (!string.IsNullOrEmpty(desc))
                lbl.Children.Add(new TextBlock { Text = desc, FontSize = 11.5, Foreground = Theme.Br(Theme.TextDim), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            SetColumn(lbl, 0); head.Children.Add(lbl);

            var valPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 1, 0, 0) };
            ValueText.FontSize = 14; ValueText.FontWeight = FontWeights.SemiBold; ValueText.Foreground = Theme.Br(Theme.Accent);
            ValueText.MinWidth = 58; ValueText.TextAlignment = TextAlignment.Right;
            ValueText.FontFamily = Theme.Mono;
            valPanel.Children.Add(ValueText);
            if (!string.IsNullOrEmpty(unit))
                valPanel.Children.Add(new TextBlock { Text = " " + unit, FontSize = 11.5, Foreground = Theme.Br(Theme.TextDim), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) });
            if (!string.IsNullOrEmpty(rightNote))
                valPanel.Children.Add(new TextBlock { Text = rightNote, FontSize = 11, Foreground = Theme.Br(Theme.TextDim), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 2) });
            SetColumn(valPanel, 1); head.Children.Add(valPanel);
            SetRow(head, 0); Children.Add(head);

            Slider.Minimum = min; Slider.Maximum = max; Slider.Value = Math.Max(min, Math.Min(max, value));
            Slider.Template = Theme.SliderTemplate;
            Slider.IsMoveToPointEnabled = true;
            Slider.SmallChange = step; Slider.LargeChange = step * 5;
            Slider.Margin = new Thickness(0, 4, 0, 0);
            Slider.ValueChanged += delegate { ValueText.Text = Slider.Value.ToString(_fmt, CultureInfo.InvariantCulture); if (_apply != null) _apply(Slider.Value); };
            ValueText.Text = Slider.Value.ToString(_fmt, CultureInfo.InvariantCulture);
            SetRow(Slider, 1); Children.Add(Slider);
            Margin = new Thickness(0, 8, 0, 12);
        }

        public double Value { get { return Slider.Value; } set { Slider.Value = Math.Max(_min, Math.Min(_max, value)); } }
        public void SetSilent(double v) { Slider.Value = Math.Max(_min, Math.Min(_max, v)); }
    }

    /// <summary>セクション見出し。</summary>
    internal static class Sec
    {
        public static StackPanel Make(string title, string desc)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 2) };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Theme.Br(Theme.Text) });
            if (!string.IsNullOrEmpty(desc))
                sp.Children.Add(new TextBlock { Text = desc, FontSize = 11.5, Foreground = Theme.Br(Theme.TextDim), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            return sp;
        }
    }

    /// <summary>水平バー（± 表示対応）。</summary>
    internal sealed class Bar : Grid
    {
        readonly Canvas _canvas = new Canvas();
        readonly Rectangle _fill = new Rectangle();
        readonly Border _track = new Border();
        readonly TextBlock _caption = new TextBlock();
        readonly bool _bipolar;
        readonly double _min, _max;
        double _v;
        bool _visible = true;

        public Bar(string caption, double min, double max, bool bipolar)
        {
            _bipolar = bipolar; _min = min; _max = max;
            Height = 24;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            _caption.Text = caption; _caption.FontSize = 12; _caption.Foreground = Theme.Br(Theme.TextDim);
            _caption.VerticalAlignment = VerticalAlignment.Center; _caption.TextTrimming = TextTrimming.CharacterEllipsis;
            SetColumn(_caption, 0); Children.Add(_caption);

            _track.Background = Theme.Br(Theme.TrackCol);
            _track.CornerRadius = new CornerRadius(5);
            _track.Height = 10; _track.VerticalAlignment = VerticalAlignment.Center;
            _fill.Fill = Theme.Br(Theme.Accent);
            _fill.RadiusX = 5; _fill.RadiusY = 5; _fill.Height = 10;
            _canvas.Children.Add(_fill);
            var g = new Grid();
            g.Children.Add(_track);
            g.Children.Add(_canvas);
            g.Margin = new Thickness(0, 0, 10, 0);
            SetColumn(g, 1); Children.Add(g);
            g.SizeChanged += delegate { Layout(); };

            var vtext = new TextBlock { FontSize = 12, FontFamily = Theme.Mono, Foreground = Theme.Br(Theme.Text), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            SetColumn(vtext, 2); Children.Add(vtext);
            _value = vtext;
        }

        readonly TextBlock _value;

        public void Set(double v, string text)
        {
            _v = v;
            _value.Text = text;
            Layout();
        }
        public void SetError(string text)
        {
            _visible = false;
            _value.Text = text;
            _fill.Width = 0;
        }
        public void SetOk() { _visible = true; }

        void Layout()
        {
            if (!_visible) { _fill.Width = 0; return; }
            double w = _canvas.ActualWidth;
            if (w <= 1) return;
            double frac = (_v - _min) / (_max - _min);
            if (frac < 0) frac = 0; if (frac > 1) frac = 1;
            if (_bipolar)
            {
                double mid = w / 2.0;
                double x = frac >= 0.5 ? mid : mid - (mid - frac * w);
                double width = frac >= 0.5 ? (frac * w - mid) : (mid - frac * w);
                Canvas.SetLeft(_fill, x);
                _fill.Width = Math.Max(0, width);
            }
            else
            {
                Canvas.SetLeft(_fill, 0);
                _fill.Width = Math.Max(0, frac * w);
            }
        }
    }

    /// <summary>小さな状態ランプ。</summary>
    internal sealed class Lamp : Border
    {
        readonly TextBlock _t;
        bool _on;
        public Lamp(string text)
        {
            CornerRadius = new CornerRadius(6);
            Padding = new Thickness(9, 3, 9, 4);
            Margin = new Thickness(0, 0, 6, 0);
            Background = Theme.Br(Theme.TrackCol);
            _t = new TextBlock { Text = text, FontSize = 11.5, Foreground = Theme.Br(Theme.TextDim) };
            Child = _t;
        }
        public bool On
        {
            get { return _on; }
            set
            {
                if (_on == value) return;
                _on = value;
                Background = value ? Theme.Br(Theme.Accent) : Theme.Br(Theme.TrackCol);
                _t.Foreground = value ? Brushes.White : Theme.Br(Theme.TextDim);
                _t.FontWeight = value ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
    }

    /// <summary>セグメント選択（プリセット / 言語）。</summary>
    internal sealed class Segmented : Border
    {
        readonly List<RadioButton> _items = new List<RadioButton>();
        public event Action<int> Selected;

        public Segmented(string[] labels, int index, double itemWidth)
        {
            Background = Theme.Br(Theme.TrackCol);
            CornerRadius = new CornerRadius(9);
            Padding = new Thickness(3);
            Panel host;
            if (itemWidth < 0)
            {
                var ug = new UniformGrid();
                ug.Columns = labels.Length;
                host = ug;
            }
            else host = new StackPanel { Orientation = Orientation.Horizontal };
            // GroupName は「この Segmented の中でだけ共有」にする。
            // 毎回 Guid を振ると 1 ボタンずつ独立グループになり、排他が効かない
            // （= 選択済みのタブをもう一度押しても Checked が出ない→切り替わらない）。
            var group = "seg" + Guid.NewGuid().ToString("N");
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var rb = new RadioButton
                {
                    Content = labels[i],
                    GroupName = group,
                    IsChecked = i == index,
                    Style = MakeStyle(),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                if (itemWidth > 0) rb.Width = itemWidth;
                if (itemWidth >= 0 && i > 0) rb.Margin = new Thickness(2, 0, 0, 0);
                rb.Checked += delegate { if (Selected != null) Selected(idx); };
                _items.Add(rb);
                host.Children.Add(rb);
            }
            Child = host;
        }

        public int Index
        {
            get { for (int i = 0; i < _items.Count; i++) if (_items[i].IsChecked == true) return i; return 0; }
        }

        static Style MakeStyle()
        {
            var st = new Style(typeof(RadioButton));
            st.Setters.Add(new Setter(RadioButton.TemplateProperty, Theme.TabTemplate ?? MakeTemplate()));
            st.Setters.Add(new Setter(RadioButton.CursorProperty, System.Windows.Input.Cursors.Hand));
            return st;
        }

        static ControlTemplate MakeTemplate()
        {
            Theme.TabTemplate = (ControlTemplate)XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type RadioButton}'>
  <Border x:Name='b' CornerRadius='7' Background='Transparent' Padding='13,6,13,7'>
    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsChecked' Value='True'>
      <Setter TargetName='b' Property='Background' Value='#FFFFFF'/>
      <Setter Property='TextElement.Foreground' Value='#141A24'/>
      <Setter Property='TextElement.FontWeight' Value='SemiBold'/>
    </Trigger>
    <Trigger Property='IsChecked' Value='False'>
      <Setter Property='TextElement.Foreground' Value='#69748A'/>
    </Trigger>
    <MultiTrigger>
      <MultiTrigger.Conditions>
        <Condition Property='IsMouseOver' Value='True'/>
        <Condition Property='IsChecked' Value='False'/>
      </MultiTrigger.Conditions>
      <Setter Property='TextElement.Foreground' Value='#2563EB'/>
    </MultiTrigger>
  </ControlTemplate.Triggers>
</ControlTemplate>");
            return Theme.TabTemplate;
        }
    }

    /// <summary>カーソル出力の簡易グラフ。</summary>
    internal sealed class Sparkline : FrameworkElement
    {
        readonly Status _st;
        public Sparkline(Status st)
        {
            _st = st;
            Height = 42;
            ClipToBounds = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 4 || h < 4) return;
            dc.DrawRectangle(Theme.Br(Theme.CardBg), null, new Rect(0, 0, w, h));

            double[] vals = new double[Status.HistLen];
            int count, head;
            lock (_st.Lock)
            {
                count = _st.HistCount; head = _st.HistHead;
                Array.Copy(_st.Hist, vals, Status.HistLen);
            }
            if (count < 2) return;
            double max = 200;
            for (int i = 0; i < count; i++)
            {
                int idx = (head - count + i + Status.HistLen * 2) % Status.HistLen;
                if (vals[idx] > max) max = vals[idx];
            }
            var pen = new Pen(Theme.Br(Theme.Accent), 1.6);
            pen.Freeze();
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (int i = 0; i < count; i++)
                {
                    int idx = (head - count + i + Status.HistLen * 2) % Status.HistLen;
                    double x = (w - 2) * i / Math.Max(1.0, count - 1) + 1;
                    double y = h - 3 - (h - 8) * Math.Min(1.0, vals[idx] / max);
                    if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                    else ctx.LineTo(new Point(x, y), true, false);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }
    }
}
