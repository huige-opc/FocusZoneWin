using System;
using System.IO;

namespace FocusZoneWin;

/// <summary>调试日志：追加写入 %APPDATA%\FocusZoneWin\debug.log，用于自测定位问题。</summary>
internal static class Logger
{
    private static readonly object Lock = new();

    private static string Path0 =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FocusZoneWin", "debug.log");

    public static void Log(string msg)
    {
        try
        {
            lock (Lock)
            {
                string dir = System.IO.Path.GetDirectoryName(Path0)!;
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path0, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static void Clear()
    {
        try { lock (Lock) { if (File.Exists(Path0)) File.Delete(Path0); } }
        catch { }
    }
}
