using System.Windows.Media;

namespace FocusZoneWin;

/// <summary>共享主题常量：选区边框颜色。</summary>
internal static class ThemeConstants
{
    public static readonly Color[] BandColors =
    {
        Color.FromRgb(0x4E, 0x9F, 0xFF), Color.FromRgb(0x4E, 0xFF, 0x4E),
        Color.FromRgb(0xFF, 0x8C, 0x00), Color.FromRgb(0xFF, 0x4E, 0x4E),
        Color.FromRgb(0xB4, 0x4E, 0xFF), Color.FromRgb(0x00, 0xD4, 0xD4),
    };

    public static readonly string[] ColorNames = { "蓝", "绿", "橙", "红", "紫", "青" };
}
