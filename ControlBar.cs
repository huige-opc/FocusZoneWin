using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace FocusZoneWin;

public class ControlBar : Window
{
    private readonly Button _colorBtn;
    private readonly Button _captureBtn;
    private readonly Button _bandBtn;
    private readonly TextBlock _bandLabel;
    private readonly TextBlock _captureLabel;
    private readonly TextBlock _dimPercent;
    private readonly Slider _dimSlider;
    private readonly TextBlock _colorIcon;
    private readonly TextBlock _colorLabel;

    private bool _dashed = true;
    private double _dim;
    private int _colorIndex;
    private bool _excludeFromCapture;


    public event Action? SelectRequested;
    public event Action<bool>? BandStyleChanged;
    public event Action<double>? DimChanged;
    public event Action<int>? ColorChanged;
    public event Action<bool>? CaptureModeChanged;
    public event Action? ExitRequested;
    public event Action<double, double>? Moved;

    public ControlBar(bool excludeFromCapture, double dim, int colorIndex, double barX, double barY)
    {
        _excludeFromCapture = excludeFromCapture;
        _dim = dim;
        _colorIndex = colorIndex;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(6, 4, 6, 4)
        };

        // 线型切换
        _bandLabel = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        _bandBtn = MakeBtn("⬜", _bandLabel, "点击切换虚线/实线", (_, _) => ToggleBandStyle());
        panel.Children.Add(_bandBtn);

        // 选区
        panel.Children.Add(MakeBtn("▶", "选区", "进入选区模式", (_, _) => SelectRequested?.Invoke()));

        // 录屏
        _captureLabel = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        _captureBtn = MakeBtn("🔴", _captureLabel, "切换录屏/投屏捕获", (_, _) => ToggleCaptureMode());
        panel.Children.Add(_captureBtn);

        // 暗度滑条
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
        sp.Children.Add(new TextBlock { Text = "🌙", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        _dimSlider = new Slider
        {
            Minimum = 0, Maximum = 100, Value = dim * 100,
            Width = 80, Height = 20, VerticalAlignment = VerticalAlignment.Center
        };
        _dimSlider.ValueChanged += (_, _) => OnDimChanged();
        sp.Children.Add(_dimSlider);
        _dimPercent = new TextBlock { Text = $"{dim * 100:F0}%", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Width = 32, TextAlignment = TextAlignment.Center };
        sp.Children.Add(_dimPercent);
        panel.Children.Add(sp);

        // 颜色
        _colorIcon = new TextBlock { FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 3, 0) };
        _colorLabel = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var cs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        cs.Children.Add(_colorIcon); cs.Children.Add(_colorLabel);
        _colorBtn = new Button
        {
            Content = cs, ToolTip = "点击切换选区边框颜色", Cursor = Cursors.Hand,
            Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(2, 0, 2, 0),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)), BorderThickness = new Thickness(0)
        };
        AddHover(_colorBtn);
        _colorBtn.Click += (_, _) => CycleColor();
        panel.Children.Add(_colorBtn);

        // 退出
        panel.Children.Add(MakeBtn("✕", "退出", "退出程序", (_, _) => ExitRequested?.Invoke()));

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(235, 240, 240, 243)),
            Child = panel, Padding = new Thickness(2),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = 0.25, Color = Color.FromRgb(0, 0, 0) }
        };
        border.MouseLeftButtonDown += (_, _) => DragMove();
        Content = border;

        SourceInitialized += OnSourceInitialized;
        LocationChanged += (_, _) => Moved?.Invoke(Left, Top);
        Loaded += (_, _) => PositionInitially(barX, barY);

        UpdateColorDisplay();
        UpdateSelectDisplay();
        _captureLabel.Text = excludeFromCapture ? "录屏:隐藏" : "录屏:可见";
        _captureBtn.ToolTip = excludeFromCapture ? "录屏/投屏已隐藏" : "录屏/投屏可见";
    }

    public void UpdateDim(double dim) { _dim = dim; _dimSlider.Value = dim * 100; _dimPercent.Text = $"{dim * 100:F0}%"; }
    public void UpdateColorIndex(int idx) { _colorIndex = idx % ThemeConstants.BandColors.Length; UpdateColorDisplay(); }
    public void UpdateSelectDisplay() { _bandLabel.Text = _dashed ? "虚线" : "实线"; _bandBtn.ToolTip = $"选区边框：{_bandLabel.Text}，点击切换"; }

    /// <summary>选区激活时冻结颜色/滑条（渲染失效时避免误导）。</summary>
    public void SetControlsFrozen(bool frozen)
    {
        _colorBtn.IsEnabled = !frozen;
        _dimSlider.IsEnabled = !frozen;
        _bandBtn.IsEnabled = true;
        double op = frozen ? 0.4 : 1.0;
        _bandBtn.Opacity = 1.0;
        _colorBtn.Opacity = op;
        _dimSlider.Opacity = op;
    }

    private void ToggleBandStyle() { _dashed = !_dashed; UpdateSelectDisplay(); BandStyleChanged?.Invoke(_dashed); }
    private void OnDimChanged() { double v = Math.Round(_dimSlider.Value); _dim = v / 100.0; _dimPercent.Text = $"{v:F0}%"; DimChanged?.Invoke(_dim); }
    private void CycleColor() { _colorIndex = (_colorIndex + 1) % ThemeConstants.BandColors.Length; UpdateColorDisplay(); ColorChanged?.Invoke(_colorIndex); }
    private void UpdateColorDisplay() { _colorIcon.Text = "■"; _colorIcon.Foreground = new SolidColorBrush(ThemeConstants.BandColors[_colorIndex]); _colorLabel.Text = ThemeConstants.ColorNames[_colorIndex]; }

    private void ToggleCaptureMode()
    {
        _excludeFromCapture = !_excludeFromCapture;
        _captureLabel.Text = _excludeFromCapture ? "录屏:隐藏" : "录屏:可见";
        _captureBtn.ToolTip = _excludeFromCapture ? "录屏/投屏已隐藏" : "录屏/投屏可见";
        ApplyCaptureAffinity();
        CaptureModeChanged?.Invoke(_excludeFromCapture);
    }

    private void ApplyCaptureAffinity()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        bool ok = NativeMethods.SetWindowDisplayAffinity(hwnd, _excludeFromCapture ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE);
        if (!ok)
            Logger.Log("WARN: SetWindowDisplayAffinity failed — needs Win10 2004+ (build 19041)");
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);
        ApplyCaptureAffinity();
    }

    private void PositionInitially(double barX, double barY)
    {
        if (!double.IsNaN(barX) && !double.IsNaN(barY)) { Left = barX; Top = barY; }
        else { var a = SystemParameters.WorkArea; Left = (a.Width - ActualWidth) / 2; Top = a.Top + 12; }
    }

    private static void AddHover(Button btn)
    {
        var h = new SolidColorBrush(Color.FromArgb(255, 220, 220, 224));
        btn.MouseEnter += (_, _) => btn.Background = h;
        btn.MouseLeave += (_, _) => btn.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    }

    private static Button MakeBtn(string icon, string text, string tip, RoutedEventHandler onClick)
    {
        return MakeBtn(icon, new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, tip, onClick);
    }

    private static Button MakeBtn(string icon, TextBlock label, string tip, RoutedEventHandler onClick)
    {
        var tb = new TextBlock { Text = icon, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        var s = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        s.Children.Add(tb); s.Children.Add(label);
        var btn = new Button
        {
            Content = s, ToolTip = tip, Cursor = Cursors.Hand,
            Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(2, 0, 2, 0),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)), BorderThickness = new Thickness(0)
        };
        AddHover(btn); btn.Click += onClick;
        return btn;
    }
}
