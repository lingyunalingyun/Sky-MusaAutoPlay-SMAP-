using System;
using System.IO;
using System.Text.Json;

namespace SMAP_WPF;

/// <summary>15 键键位配置的持久化: 存 %APPDATA%\SMAP\keyconfig.json, 只存 VK 码, 标签由 VK 反推。</summary>
public static class KeyConfig
{
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP");
    static readonly string File = Path.Combine(Dir, "keyconfig.json");

    /// <summary>读取配置; 无文件或损坏时回退默认。返回长度 15 的 VK 数组。</summary>
    public static ushort[] Load()
    {
        try
        {
            if (System.IO.File.Exists(File))
            {
                var vks = JsonSerializer.Deserialize<ushort[]>(System.IO.File.ReadAllText(File));
                if (vks is { Length: 15 }) return vks;
            }
        }
        catch { /* 损坏回退默认 */ }
        return (ushort[])SkyPlayer.DefaultVk.Clone();
    }

    public static void Save(ushort[] vks)
    {
        Directory.CreateDirectory(Dir);
        System.IO.File.WriteAllText(File, JsonSerializer.Serialize(vks));
    }

    /// <summary>VK 码 → 显示标签(琴键面板用)。</summary>
    public static string Label(ushort vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),          // A-Z
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),          // 0-9
        0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-",
        0xBE => ".", 0xBF => "/", 0xC0 => "`",
        0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
        0x20 => "Space",
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),              // F1-F24
        >= 0x25 and <= 0x28 => vk switch { 0x25 => "←", 0x26 => "↑", 0x27 => "→", _ => "↓" },
        _ => "0x" + vk.ToString("X2")
    };
}
