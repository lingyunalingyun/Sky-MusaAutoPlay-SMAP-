using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SMAP_WPF;

/// <summary>曲库元数据(收藏 + 标签)持久化到 %APPDATA%\SMAP\library.json, 按文件名索引。</summary>
public static class LibraryMeta
{
    class Data
    {
        public HashSet<string> Favorites { get; set; } = new();
        public Dictionary<string, List<string>> Tags { get; set; } = new();
    }

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP");
    static readonly string File = Path.Combine(Dir, "library.json");
    static Data _d = Load();

    static Data Load()
    {
        try
        {
            if (System.IO.File.Exists(File))
                return JsonSerializer.Deserialize<Data>(System.IO.File.ReadAllText(File)) ?? new Data();
        }
        catch { /* 损坏回退空 */ }
        return new Data();
    }

    static void Save()
    {
        Directory.CreateDirectory(Dir);
        System.IO.File.WriteAllText(File, JsonSerializer.Serialize(_d));
    }

    public static bool IsFav(string fileName) => _d.Favorites.Contains(fileName);

    public static void ToggleFav(string fileName)
    {
        if (!_d.Favorites.Remove(fileName)) _d.Favorites.Add(fileName);
        Save();
    }

    public static IReadOnlyList<string> TagsOf(string fileName) =>
        _d.Tags.TryGetValue(fileName, out var t) ? t : Array.Empty<string>();

    public static void AddTag(string fileName, string tag)
    {
        if (!_d.Tags.TryGetValue(fileName, out var list)) _d.Tags[fileName] = list = new List<string>();
        if (!list.Contains(tag)) { list.Add(tag); Save(); }
    }

    public static void RemoveTag(string fileName, string tag)
    {
        if (_d.Tags.TryGetValue(fileName, out var list) && list.Remove(tag))
        {
            if (list.Count == 0) _d.Tags.Remove(fileName);
            Save();
        }
    }

    public static SortedSet<string> AllTags() => new(_d.Tags.Values.SelectMany(t => t));

    // 删除曲谱时清理其收藏/标签
    public static void Forget(string fileName)
    {
        bool changed = _d.Favorites.Remove(fileName) | _d.Tags.Remove(fileName);
        if (changed) Save();
    }
}
