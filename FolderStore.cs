using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SMAP_WPF;

// 收藏夹(歌单): 命名 + 曲谱文件路径集合。代替旧的标签系统。
public class Folder : System.ComponentModel.INotifyPropertyChanged
{
    string _name = "";
    public string Name { get => _name; set { _name = value; OnChanged(nameof(Name)); } }
    public List<string> Files { get; set; } = new();

    public int Count => Files.Count;
    public string CountText => $"{Count} {Lang.S("unit.songs")}";
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public void OnChanged(string p) => PropertyChanged?.Invoke(this, new(p));
}

public static class FolderStore
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP");
    static readonly string FilePath = Path.Combine(Dir, "folders.json");

    public static List<Folder> Load()
    {
        List<Folder> list;
        try
        {
            list = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<Folder>>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch { list = new(); }
        if (list.Count == 0) list.Add(new Folder { Name = "默认收藏夹" });   // 首次: 建默认收藏夹
        return list;
    }

    public static void Save(IEnumerable<Folder> folders)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(folders, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
