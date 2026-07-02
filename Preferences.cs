using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusZoneWin;

public class Preferences
{
    public double DimLevel { get; set; } = 0.6;
    public bool Enabled { get; set; } = true;

    public double RectX { get; set; }
    public double RectY { get; set; }
    public double RectW { get; set; }
    public double RectH { get; set; }

    /// <summary>控制条是否对录屏/截图/投屏防捕获（讲课模式默认开）。</summary>
    public bool ExcludeFromCapture { get; set; } = true;

    /// <summary>浮动控制条左上角位置；NaN 表示未设置，首次显示用默认右上角。</summary>
    public double BarX { get; set; } = double.NaN;
    public double BarY { get; set; } = double.NaN;

    /// <summary>选区边框颜色索引（0=蓝,1=绿,2=橙,3=红,4=紫,5=青）</summary>
    public int BandColorIndex { get; set; } = 0;

    [JsonIgnore]
    public bool HasRect => RectW > 1 && RectH > 1;

    private static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FocusZoneWin");

    private static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static Preferences Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var prefs = JsonSerializer.Deserialize<Preferences>(json);
                if (prefs != null)
                    return prefs;
            }
        }
        catch
        {
            // 配置损坏时回退到默认值，不阻断启动
        }
        return new Preferences();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // 持久化失败不影响运行
        }
    }
}
