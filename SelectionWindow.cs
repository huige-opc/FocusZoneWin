using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FocusZoneWin;

/// <summary>
/// 全屏选区窗：冻结画面 + 暗幕，拖框选专注区，框虚线圆角可换色。
/// ESC/右键 退出冻结并确认选区。单 Path 实现，不修改 visual tree。
/// </summary>
public class SelectionWindow : Window
{
    private readonly Canvas _canvas;
    private readonly Path _mask;
    private readonly Path _band;
    private readonly double _dim;
    private Point _start;
    private bool _dragging;
    private bool _closing;
    private bool _confirmed;
    private bool _dashed = true;
    private Rect _current = Rect.Empty;

    private DispatcherTimer? _poller;
    private bool _escWasDown;
    private bool _rbWasDown;

    public int BandColorIndex { get; set; } = 0;
    private Color BandColor => ThemeConstants.BandColors[BandColorIndex % ThemeConstants.BandColors.Length];

    public bool BandDashed
    {
        get => _dashed;
        set { _dashed = value; UpdateBandStyle(); }
    }

    public event Action<Rect>? Selected;
    public event Action? Cancelled;

    public SelectionWindow(double dim, BitmapSource? frozen = null)
    {
        _dim = Math.Clamp(dim, 0, 1);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        Background = Brushes.Black;
        Topmost = false;
        ShowInTaskbar = false;
        ShowActivated = true;
        Focusable = true;
        Cursor = Cursors.Cross;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

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

        var root = new Grid();
        root.Background = Brushes.Black;
        if (frozen != null)
            root.Children.Add(new Image { Source = frozen, Stretch = Stretch.Fill, IsHitTestVisible = false });
        root.Children.Add(_canvas);
        Content = root;

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        PreviewMouseRightButtonDown += (_, e) => { e.Handled = true; if (!_dragging) FinishSelection(); };
        PreviewKeyDown += OnKey;
        Loaded += OnLoaded;
        Closed += (_, _) => StopPolling();
        SizeChanged += (_, _) => UpdateMask();
    }

    public void UpdateBandStyle()
    {
        _band.Stroke = new SolidColorBrush(BandColor);
        _band.StrokeDashArray = _dashed ? new DoubleCollection { 5, 3 } : null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        bool fg = hwnd != IntPtr.Zero && NativeMethods.SetForegroundWindow(hwnd);
        Activate(); Focus(); Keyboard.Focus(this);
        UpdateMask(); UpdateBandStyle();
        Logger.Log($"SelectionWindow Loaded: hwnd={hwnd} setFg={fg}");

        _escWasDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0;
        _rbWasDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0;
        _poller = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
        _poller.Tick += PollInput;
        _poller.Start();
    }

    private void PollInput(object? sender, EventArgs e)
    {
        bool escDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0;
        bool rbDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0;
        if (escDown && !_escWasDown) { FinishSelection(); return; }
        if (rbDown && !_rbWasDown) { FinishSelection(); return; }
        _escWasDown = escDown; _rbWasDown = rbDown;
    }

    private void StopPolling() { if (_poller == null) return; _poller.Stop(); _poller.Tick -= PollInput; _poller = null; }

    private void UpdateMask()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0) return;
        var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
        g.Children.Add(new RectangleGeometry(new Rect(0, 0, w, h)));
        if (!_current.IsEmpty && _current.Width > 1 && _current.Height > 1)
            g.Children.Add(new RectangleGeometry(_current, 8, 8));
        _mask.Data = g;
        _mask.Fill = new SolidColorBrush(Color.FromArgb((byte)(_dim * 255), 0, 0, 0));
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(_canvas);
        _dragging = true;
        _band.Data = new RectangleGeometry(new Rect(_start.X, _start.Y, 1, 1), 8, 8);
        _band.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        Point p = e.GetPosition(_canvas);
        double x = Math.Min(p.X, _start.X), y = Math.Min(p.Y, _start.Y);
        double w = Math.Abs(p.X - _start.X), h = Math.Abs(p.Y - _start.Y);
        _current = new Rect(x, y, w, h);
        _band.Data = new RectangleGeometry(_current, 8, 8);
        UpdateMask();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        if (_current.Width < 8 || _current.Height < 8)
        {
            _current = Rect.Empty;
            _band.Data = null;
            _band.Visibility = Visibility.Collapsed;
            UpdateMask();
            return;
        }
        _confirmed = true;
        Logger.Log("Selection confirmed, waiting for ESC/right-click to exit");
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) FinishSelection();
    }

    private void FinishSelection()
    {
        if (_closing) return;
        _closing = true;
        StopPolling();
        if (_confirmed && _current.Width > 1 && _current.Height > 1)
        {
            var rect = new Rect(_current.X + Left, _current.Y + Top, _current.Width, _current.Height);
            Selected?.Invoke(rect);
        }
        else Cancelled?.Invoke();
        Close();
    }
}
