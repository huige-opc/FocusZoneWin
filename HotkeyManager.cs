using System;
using System.Windows.Interop;

namespace FocusZoneWin;

/// <summary>
/// 用消息专用窗口（HWND_MESSAGE）接收 WM_HOTKEY。
/// 注册 Ctrl+Shift+S 重选选区。
/// </summary>
public class HotkeyManager : IDisposable
{
    private const int ID_RESELECT = 1;

    private const uint VK_S = 0x53;

    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly HwndSource _source;

    public event Action? ReselectRequested;

    public HotkeyManager()
    {
        var parameters = new HwndSourceParameters("FocusZoneHotkeyWindow")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        uint mod = NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT;
        if (!NativeMethods.RegisterHotKey(_source.Handle, ID_RESELECT, mod, VK_S))
        {
            Logger.Log("WARN: RegisterHotKey(Ctrl+Shift+S) failed — shortcut may be taken by another app");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            if (wParam.ToInt32() == ID_RESELECT)
            {
                ReselectRequested?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        NativeMethods.UnregisterHotKey(_source.Handle, ID_RESELECT);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
