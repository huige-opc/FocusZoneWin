using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace FocusZoneWin;

public class AppController : IDisposable
{
    private readonly Preferences _prefs;
    private readonly OverlayWindow _overlay;
    private readonly HotkeyManager _hotkeys;
    private readonly ControlBar _bar;
    private readonly WinForms.NotifyIcon _tray;
    private readonly Drawing.Icon _trayIcon;

    private DispatcherTimer? _escWatcher;
    private bool _escWasDown;
    private bool _rbWasDown;
    private bool _selecting;
    private bool _bandDashed = true;
    private SelectionWindow? _currentSel;

    public AppController()
    {
        _prefs = Preferences.Load();

        _overlay = new OverlayWindow();
        _overlay.Show();

        _trayIcon = CreateTrayIcon();
        _tray = BuildTray();
        _tray.ShowBalloonTip(4000, "FocusZone 已启动", "双击图标重新选区。", WinForms.ToolTipIcon.Info);

        _bar = new ControlBar(_prefs.ExcludeFromCapture, _prefs.DimLevel, _prefs.BandColorIndex, _prefs.BarX, _prefs.BarY);
        _bar.SelectRequested += () => RunOnUi(StartSelection);
        _bar.BandStyleChanged += dashed => RunOnUi(() => SetBandStyle(dashed));
        _bar.DimChanged += dim => RunOnUi(() => SetDim(dim));
        _bar.ColorChanged += idx => RunOnUi(() => SetColor(idx));
        _bar.ExitRequested += () => RunOnUi(ExitApp);
        _bar.Moved += (x, y) => { _prefs.BarX = x; _prefs.BarY = y; _prefs.Save(); };
        _bar.Show();

        _hotkeys = new HotkeyManager();
        _hotkeys.ReselectRequested += () => RunOnUi(StartSelection);

        ApplyState();

        _escWasDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0;
        _rbWasDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0;
        _escWatcher = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(20) };
        _escWatcher.Tick += WatchToCloseMask;
        _escWatcher.Start();
    }

    public void RequestReselect() => RunOnUi(StartSelection);

    private void WatchToCloseMask(object? sender, EventArgs e)
    {
        bool escDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0;
        bool rbDown = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_RBUTTON) & 0x8000) != 0;
        bool escRising = escDown && !_escWasDown;
        bool rbRising = rbDown && !_rbWasDown;
        _escWasDown = escDown; _rbWasDown = rbDown;
        if (!escRising && !rbRising) return;
        if (_selecting) return;
        if (!_prefs.HasRect) return;
        Logger.Log(escRising ? "ESC -> exit focus" : "RButton -> exit focus");
        ClearFocus();
    }

    private void ClearFocus()
    {
        _overlay.SetActive(false);
        _overlay.SetFocusRect(Rect.Empty);
        _prefs.Enabled = false;
        _prefs.Save();
        _bar.SetControlsFrozen(false);
        _bar.Show();
    }

    private WinForms.NotifyIcon BuildTray()
    {
        var tray = new WinForms.NotifyIcon { Icon = _trayIcon, Visible = true, Text = "FocusZone" };
        tray.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) _bar.Activate(); };
        tray.MouseDoubleClick += (_, _) => StartSelection();
        return tray;
    }

    private static Drawing.Icon CreateTrayIcon()
    {
        const int s = 64;
        var bmp = new Drawing.Bitmap(s, s);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            var blue = Drawing.Color.FromArgb(255, 0x4E, 0x9F, 0xFF);
            var amber = Drawing.Color.FromArgb(255, 0xFF, 0xC8, 0x3D);
            const int m = 8, len = 20, th = 9;
            using var br = new Drawing.SolidBrush(blue);
            g.FillRectangle(br, m, m, len, th); g.FillRectangle(br, m, m, th, len);
            g.FillRectangle(br, s - m - len, m, len, th); g.FillRectangle(br, s - m - th, m, th, len);
            g.FillRectangle(br, m, s - m - th, len, th); g.FillRectangle(br, m, s - m - len, th, len);
            g.FillRectangle(br, s - m - len, s - m - th, len, th); g.FillRectangle(br, s - m - th, s - m - len, th, len);
            using var dot = new Drawing.SolidBrush(amber);
            g.FillEllipse(dot, s / 2 - 9, s / 2 - 9, 18, 18);
        }
        var icon = (Drawing.Icon)Drawing.Icon.FromHandle(bmp.GetHicon()).Clone(); bmp.Dispose();
        return icon;
    }

    private void ApplyState()
    {
        _overlay.Dim = _prefs.DimLevel;
        _overlay.SetActive(_prefs.Enabled && _prefs.HasRect);
    }

    private async void StartSelection()
    {
        if (_selecting) return;
        Logger.Log("StartSelection enter");
        _selecting = true;
        _bar.SetControlsFrozen(true);
        _overlay.SetActive(false);
        _bar.Hide();
        await Task.Delay(60);
        RunOnUi(ShowSelectionWindow);
    }

    private void ShowSelectionWindow()
    {
        try
        {
            var frozen = ScreenCapture.CaptureVirtualScreen();
            Logger.Log(frozen != null ? $"Frozen captured: {frozen.PixelWidth}x{frozen.PixelHeight}" : "Frozen captured: NULL");
            var sel = new SelectionWindow(_prefs.DimLevel, frozen);
            sel.BandColorIndex = _prefs.BandColorIndex;
        sel.BandDashed = _bandDashed;
        _currentSel = sel;
        sel.Selected += rect =>
        {
            Logger.Log("Selection done"); _selecting = false; _currentSel = null;
            _prefs.RectX = rect.X; _prefs.RectY = rect.Y; _prefs.RectW = rect.Width; _prefs.RectH = rect.Height;
            _prefs.Enabled = true; _prefs.Save();
            _overlay.BandColorIndex = _prefs.BandColorIndex;
            _overlay.BandDashed = _bandDashed;
            _overlay.SetFocusRect(rect);
            _overlay.SetActive(true);
            _bar.Show();
        };
        sel.Cancelled += () =>
        {
            Logger.Log("Selection cancelled"); _selecting = false; _currentSel = null;
            _bar.SetControlsFrozen(false);
            ApplyState(); _bar.Show();
        };
        sel.Closed += (_, _) =>
        {
            if (_selecting)
            {
                Logger.Log("SelectionWindow closed unexpectedly"); _selecting = false; _currentSel = null;
                _bar.SetControlsFrozen(false);
                try { ApplyState(); _bar.Show(); } catch { }
            }
        };
        sel.Show(); sel.Activate();
        _bar.Show();
        Logger.Log("StartSelection: bar on top");
    }
    catch (Exception ex)
    {
        Logger.Log($"ShowSelectionWindow failed: {ex}");
        _selecting = false; _currentSel = null;
        _bar.SetControlsFrozen(false);
        try { _bar.Show(); } catch { }
        ApplyState();
    }
}

private void SetBandStyle(bool dashed)
{
    _bandDashed = dashed;
    if (_currentSel != null) _currentSel.BandDashed = dashed;
    _overlay.BandDashed = dashed;
    _overlay.UpdateBandStyle();
    _bar.Show();
    Logger.Log($"SetBandStyle: dashed={dashed}");
}

private void SetDim(double dim)
{
    if (_overlay.IsShowing()) { Logger.Log("BLOCKED: overlay active"); return; }
    _prefs.DimLevel = Math.Clamp(dim, 0, 1); _prefs.Save();
    _bar.UpdateDim(_prefs.DimLevel); _overlay.Dim = _prefs.DimLevel;
}

private void SetColor(int idx)
{
    if (_overlay.IsShowing()) { Logger.Log("BLOCKED: overlay active"); return; }
    _prefs.BandColorIndex = idx; _prefs.Save();
    _bar.UpdateColorIndex(idx);
    _overlay.BandColorIndex = idx;
    _overlay.UpdateBandStyle();
}

private static void RunOnUi(Action action)
{
    var disp = System.Windows.Application.Current?.Dispatcher;
    if (disp != null && !disp.CheckAccess()) disp.Invoke(action); else action();
}

internal void ExitApp() { ExitCleanup(); System.Windows.Application.Current.Shutdown(); }

internal void ExitCleanup()
{
    _selecting = false; _escWatcher?.Stop(); _escWatcher = null;
    _hotkeys.Dispose(); _tray.Visible = false; _tray.Dispose(); _trayIcon?.Dispose();
    try { _bar.Close(); } catch { }
    try { _overlay.Close(); } catch { }
}

public void Dispose() { ExitCleanup(); }
}
