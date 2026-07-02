using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FocusZoneWin;

/// <summary>
/// 全屏置顶、点击穿透的遮罩窗。用 EvenOdd 几何在整屏黑幕上挖出专注矩形（洞），
/// dim 控制黑幕透明度；专注矩形坐标用虚拟桌面坐标系，与选区窗共用。
/// </summary>
public class OverlayWindow : Window
{
    private readonly Path _mask;
    private readonly Path _band;
    private readonly Canvas _canvas;
    private Rect _focusRect = Rect.Empty;
    private double _dim = 0.6;
    private bool _active;

    public int BandColorIndex { get; set; } = 0;
    public bool BandDashed { get; set; } = true;

    private Color BandColor => ThemeConstants.BandColors[BandColorIndex % ThemeConstants.BandColors.Length];

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;

        _canvas = new Canvas();
        _mask = new Path { IsHitTestVisible = false };
        _band = new Path
        {
            StrokeThickness = 2.5,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        _canvas.Children.Add(_mask);
        _canvas.Children.Add(_band);
        Content = _canvas;

        PositionToVirtualScreen();
        SizeChanged += (_, _) => UpdateMask();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED
            | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);
        UpdateMask();
    }

    public void PositionToVirtualScreen()
    {
        var w = SystemParameters.VirtualScreenWidth;
        var h = SystemParameters.VirtualScreenHeight;
        if (w <= 0 || h <= 0)
        {
            Logger.Log("WARN: VirtualScreen invalid, fallback to primary monitor");
            Left = 0; Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            return;
        }
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = w;
        Height = h;
    }

    public double Dim
    {
        get => _dim;
        set { _dim = Math.Clamp(value, 0, 1); UpdateMask(); }
    }

    public void SetFocusRect(Rect virtualRect)
    {
        _focusRect = virtualRect;
        UpdateMask();
    }

    /// <summary>当前遮罩是否激活且有选区。</summary>
    public bool IsShowing() => _active && !_focusRect.IsEmpty && _focusRect.Width > 1;

    /// <summary>启用=正常显示带洞遮罩；禁用=黑幕透明，相当于隐藏效果。</summary>
    public void SetActive(bool active)
    {
        _active = active;
        UpdateMask();
    }

    public void UpdateBandStyle()
    {
        _band.Stroke = new SolidColorBrush(BandColor);
        _band.StrokeDashArray = BandDashed ? new DoubleCollection { 5, 3 } : null;
        // 强制重绘分层窗口（AllowsTransparency=true 时部分系统不会自动刷新）
        _canvas.InvalidateVisual();
    }

    private void UpdateMask()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0)
            return;

        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry(new Rect(0, 0, w, h)));

        if (_active && !_focusRect.IsEmpty && _focusRect.Width > 1 && _focusRect.Height > 1)
        {
            var local = new Rect(
                _focusRect.X - Left,
                _focusRect.Y - Top,
                _focusRect.Width,
                _focusRect.Height);
            group.Children.Add(new RectangleGeometry(local, 8, 8));

            // 边框用独立的几何对象（WPF 不允许一个几何同时属于两个父级）
            _band.Data = new RectangleGeometry(local, 8, 8);
            _band.Visibility = Visibility.Visible;
            UpdateBandStyle();
        }
        else
        {
            _band.Data = null;
            _band.Visibility = Visibility.Collapsed;
        }

        byte alpha = _active ? (byte)(_dim * 255) : (byte)0;
        _mask.Data = group;
        _mask.Fill = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
        // 强制重绘分层窗口（AllowsTransparency=true 时部分系统不会自动刷新）
        _canvas.InvalidateVisual();
    }
}
