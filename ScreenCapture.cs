using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace FocusZoneWin;

/// <summary>
/// 把整块虚拟桌面（所有显示器）抓成一张静态位图，用作选区时的"定格"背景。
/// 返回已 Freeze 的 BitmapSource，可跨线程安全使用；失败返回 null（选区窗退回实时桌面）。
/// </summary>
internal static class ScreenCapture
{
    public static BitmapSource? CaptureVirtualScreen()
    {
        try
        {
            var vs = WinForms.SystemInformation.VirtualScreen;   // 物理像素边界
            using var bmp = new Drawing.Bitmap(vs.Width, vs.Height,
                Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(vs.Left, vs.Top, 0, 0,
                    new Drawing.Size(vs.Width, vs.Height),
                    Drawing.CopyPixelOperation.SourceCopy);
            }

            // 关键：不要走 GetHbitmap + CreateBitmapSourceFromHBitmap。
            // GetHbitmap 总会返回 32 位 HBITMAP 且 alpha 字节=0，WPF 当成 Bgra32 全透明，
            // 导致"定格图"透明、看穿到实时桌面。改为直接锁原始像素，以 Bgr24（无 alpha
            // 通道，WPF 强制不透明）构造 BitmapSource。
            //
            // 注意：BitmapSource.Create(IntPtr buffer) 不复制像素数据，直接引用 GDI+ 的
            // 未托管内存。Bitmap 被 Dispose 后该内存被释放，WPF 渲染时访问野指针 → 闪退。
            // 因此必须先将像素复制到托管数组，用托管数组重载创建。
            var rect = new Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, Drawing.Imaging.ImageLockMode.ReadOnly,
                Drawing.Imaging.PixelFormat.Format24bppRgb);
            try
            {
                int stride = data.Stride;
                int size = stride * data.Height;
                byte[] pixels = new byte[size];
                Marshal.Copy(data.Scan0, pixels, 0, size);

                var src = BitmapSource.Create(
                    data.Width, data.Height,
                    96, 96,
                    PixelFormats.Bgr24,
                    null,
                    pixels,
                    stride);
                src.Freeze();
                Logger.Log($"Capture format={src.Format} {src.PixelWidth}x{src.PixelHeight}");
                return src;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"ScreenCapture failed: {ex.Message}");
            return null;
        }
    }
}
