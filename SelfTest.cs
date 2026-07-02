using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FocusZoneWin;

/// <summary>
/// 自检：证明“选区定格遮罩”确实不透明、画面被定格，不依赖人眼。
/// 流程：抓当前屏当定格图 → 盖不透明遮罩(dim=0，直接显原图) → 在遮罩底下弹满屏品红窗
/// → 再抓屏，与定格图逐像素比对。遮罩不透明则品红被完全挡住，差≈0；能看穿则差很大。
/// </summary>
internal static class SelfTest
{
    public static void Run()
    {
        Logger.Clear();
        Logger.Log("SelfTest start");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) => Begin(app);
        app.Run();
    }

    private static void Begin(Application app)
    {
        var frozen = ScreenCapture.CaptureVirtualScreen();
        if (frozen == null)
        {
            Logger.Log("SelfTest FAIL: capture returned null");
            app.Shutdown();
            return;
        }
        Logger.Log($"SelfTest frozen {frozen.PixelWidth}x{frozen.PixelHeight} fmt={frozen.Format}");

        // dim=0：遮罩不压暗，直接铺定格原图，方便和原图逐像素比。
        var overlay = new SelectionWindow(0.0, frozen);
        overlay.Show();
        overlay.Activate();

        // 在置顶遮罩“底下”造一个满屏品红窗口当“实时变化”。
        var live = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = false,
            WindowState = WindowState.Maximized,
            Background = new SolidColorBrush(Color.FromRgb(255, 0, 255))
        };
        live.Show();

        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var after = ScreenCapture.CaptureVirtualScreen();
            if (after == null)
            {
                Logger.Log("SelfTest FAIL: recapture returned null");
                Finish(app, live, overlay);
                return;
            }

            var (diff, fLum, aLum) = CompareRegion(frozen, after);
            SavePng(after, Path.Combine(AppContext.BaseDirectory, "proof.png"));
            string verdict = diff < 25 ? "PASS (opaque & frozen)" : "FAIL (see-through to live)";
            Logger.Log($"SelfTest result: regionDiff={diff:F2} frozenLum={fLum:F1} screenLum={aLum:F1} -> {verdict}");
            Finish(app, live, overlay);
        };
        timer.Start();
    }

    private static void Finish(Application app, Window a, Window b)
    {
        try { a.Close(); } catch { }
        try { b.Close(); } catch { }
        app.Shutdown();
    }

    /// <summary>比对两张同尺寸 Bgr24 图在中下区域(避开顶部提示文字)的平均亮度差。</summary>
    private static (double diff, double fLum, double aLum) CompareRegion(BitmapSource f, BitmapSource a)
    {
        int w = Math.Min(f.PixelWidth, a.PixelWidth);
        int h = Math.Min(f.PixelHeight, a.PixelHeight);
        int stride = w * 3;
        var fb = new byte[stride * h];
        var ab = new byte[stride * h];
        f.CopyPixels(new Int32Rect(0, 0, w, h), fb, stride, 0);
        a.CopyPixels(new Int32Rect(0, 0, w, h), ab, stride, 0);

        int x0 = (int)(w * 0.2), x1 = (int)(w * 0.8);
        int y0 = (int)(h * 0.5), y1 = (int)(h * 0.9);
        double sumDiff = 0, sumF = 0, sumA = 0; int n = 0;
        for (int y = y0; y < y1; y += 10)
            for (int x = x0; x < x1; x += 10)
            {
                int i = y * stride + x * 3;
                double lf = (fb[i] + fb[i + 1] + fb[i + 2]) / 3.0;
                double la = (ab[i] + ab[i + 1] + ab[i + 2]) / 3.0;
                sumDiff += Math.Abs(lf - la); sumF += lf; sumA += la; n++;
            }
        return (sumDiff / n, sumF / n, sumA / n);
    }

    private static void SavePng(BitmapSource src, string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Create);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            enc.Save(fs);
        }
        catch (Exception ex) { Logger.Log($"SavePng failed: {ex.Message}"); }
    }
}
