using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SMAP_WPF;

/// <summary>每乐器移调(半音)持久化: %APPDATA%\SMAP\pitch.json, 乐器名→半音偏移。
/// 默认按母采样音域还原游戏相对音高(低音乐器默认降八度), 玩家可在音高 pill 里改。</summary>
public static class PitchConfig
{
    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP");
    static readonly string FilePath = Path.Combine(Dir, "pitch.json");

    // 内置默认: 母采样音域偏低的乐器降八度以贴近游戏(O=3→-12, O=2→-24);
    // O=5 高音源已统一到 C3..C5 且用户认可, 默认 0。
    static readonly Dictionary<string, int> Default = new()
    {
        ["Cello"] = -12, ["Horn"] = -12, ["Handpan"] = -12, ["GoldHandpan"] = -12,
        ["Dundun"] = -12, ["APBell1"] = -12, ["APBell2"] = -12,
        ["Contrabass"] = -24, ["4thAnnivBass"] = -24, ["GoldDundun"] = -24,
    };

    static Dictionary<string, int> _user = LoadFile();

    static Dictionary<string, int> LoadFile()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { /* 损坏回退空 */ }
        return new();
    }

    /// <summary>当前生效的半音偏移(玩家自定义优先, 否则内置默认, 否则 0)。</summary>
    public static int Get(string name) =>
        _user.TryGetValue(name, out var v) ? v : (Default.TryGetValue(name, out var d) ? d : 0);

    public static void Set(string name, int semitone)
    {
        _user[name] = semitone;
        try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(_user)); }
        catch { /* 忽略写失败 */ }
    }

    /// <summary>清除所有玩家自定义, 回到内置默认音调。</summary>
    public static void ResetAll()
    {
        _user.Clear();
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { /* 忽略 */ }
    }
}
