using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SMAP_WPF;

public class SongInfo
{
    public string File = "";
    public string Name = "";
    public double Bpm = 120;
    public bool Fav;
    public int NoteCount;
    public double DurationMs;      // 末音符时间, 状态栏/进度条总时长用
    public string FileName => Path.GetFileName(File);
    public override string ToString() => (Fav ? "⭐ " : "") + Name;   // ListView 显示: 收藏前缀 + 曲名
}

/// <summary>一份可编辑的曲谱: 保留原始全部字段(Root), 音符以拍位置存, 保存时 beat→ms 写回。</summary>
public class SongDocument
{
    public JsonObject Root = new();       // 完整原始字段(author/pitchLevel/isEncrypted 等原样保留)
    public readonly List<Note> Notes = new();
    public double MsPerBeat = 500;        // 精确拍长(beat↔ms); 加载=GCD, 新建=120BPM
    public double TotalBeats = 16;
    public int Bpm = 120;                 // 显示/播放速度用的整数 BPM
    public string? FilePath;              // 来源文件(另存为改)
    public string Name = "未命名";
    public string Author = "";
    public string TranscribedBy = "";
}

public static class SongLibrary
{
    // 程序目录下的 songs 文件夹(便携; 首次导入/下载时自动创建)
    public static string SongsDir = Path.Combine(AppContext.BaseDirectory, "songs");

    /// <summary>扫描曲库目录, 解析每个曲谱的曲名/BPM。</summary>
    public static List<SongInfo> Scan()
    {
        var list = new List<SongInfo>();
        if (!Directory.Exists(SongsDir)) return list;
        foreach (var f in Directory.EnumerateFiles(SongsDir))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext != ".txt" && ext != ".json") continue;
            try
            {
                var root = ParseRoot(ReadText(f));
                string name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() ?? Path.GetFileNameWithoutExtension(f)
                    : Path.GetFileNameWithoutExtension(f);
                double bpm = root.TryGetProperty("bpm", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetDouble() : 120;
                int notes = 0; double dur = 0;
                if (root.TryGetProperty("songNotes", out var sn) && sn.ValueKind == JsonValueKind.Array)
                {
                    notes = sn.GetArrayLength();
                    foreach (var e in sn.EnumerateArray())
                        if (e.TryGetProperty("time", out var tm) && tm.ValueKind == JsonValueKind.Number)
                            dur = Math.Max(dur, tm.GetDouble());
                }
                list.Add(new SongInfo { File = f, Name = name, Bpm = bpm <= 0 ? 120 : bpm, NoteCount = notes, DurationMs = dur });
            }
            catch { /* 跳过无法解析的文件 */ }
        }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));
        return list;
    }

    /// <summary>加载曲谱音符, 把绝对时间(ms)按曲谱 BPM 换算成拍位置(beat)。</summary>
    /// <summary>加载完整曲谱文档(保留全部字段), 音符 ms 按 GCD 精确拍长换算成拍位置。</summary>
    public static SongDocument LoadDocument(SongInfo info)
    {
        var doc = new SongDocument { FilePath = info.File };
        var node = JsonNode.Parse(ReadText(info.File));
        JsonObject obj = (node is JsonArray a && a.Count > 0 ? a[0]!.AsObject() : node!.AsObject());
        doc.Root = obj;
        doc.Name = (string?)obj["name"] ?? Path.GetFileNameWithoutExtension(info.File);
        doc.Author = (string?)obj["author"] ?? "";
        doc.TranscribedBy = (string?)obj["transcribedBy"] ?? "";

        // 收集音符
        var raw = new List<(double ms, int key)>();
        if (obj["songNotes"] is JsonArray notes)
            foreach (var e in notes)
            {
                if (e is null) continue;
                double ms = (double?)e["time"] ?? -1;
                int key = ParseKeyNode(e["key"]);
                if (ms < 0 || key < 0) continue;
                raw.Add((ms, key));
            }
        double stdBpm = (obj["bpm"] is JsonValue bv && bv.TryGetValue(out double bb) && bb > 0) ? bb : 120;
        doc.MsPerBeat = RefineMsPerBeat(raw.Select(x => x.ms), stdBpm);
        doc.Bpm = (int)Math.Round(60000.0 / doc.MsPerBeat);

        double maxBeat = 0;
        foreach (var (ms, key) in raw)
        {
            double beat = ms / doc.MsPerBeat;
            doc.Notes.Add(new Note { Key = key, Beat = beat });
            if (beat > maxBeat) maxBeat = beat;
        }
        doc.TotalBeats = Math.Max(16, Math.Ceiling(maxBeat) + 4);
        return doc;
    }

    /// <summary>保存曲谱: 拍位置 beat 按 MsPerBeat 换回 ms, 保留其它原始字段, 写 Sky 格式(数组含一对象)。</summary>
    public static void Save(SongDocument doc, IEnumerable<Note> notes, string path)
    {
        var arr = new JsonArray();
        foreach (var n in notes.OrderBy(x => x.Beat))
            arr.Add(new JsonObject { ["time"] = (long)Math.Round(n.Beat * doc.MsPerBeat), ["key"] = $"1Key{n.Key}" });

        doc.Root["name"] = doc.Name;
        doc.Root["author"] = doc.Author;
        doc.Root["transcribedBy"] = doc.TranscribedBy;
        doc.Root["bpm"] = doc.Bpm;
        doc.Root["keyCount"] = 15;
        doc.Root["songNotes"] = arr;
        doc.Root["isComposed"] ??= true;
        doc.Root["bitsPerPage"] ??= 16;
        doc.Root["pitchLevel"] ??= 0;
        doc.Root["isEncrypted"] ??= false;

        var outArr = new JsonArray(doc.Root.DeepClone());
        File.WriteAllText(path, outArr.ToJsonString(new JsonSerializerOptions { WriteIndented = false }), new UTF8Encoding(false));
        doc.FilePath = path;
    }

    /// <summary>MIDI 导入: 把 (key, ms) 音符写成新 Sky 曲谱, 返回落盘路径(自动避免重名)。</summary>
    public static string WriteImported(string name, string transcribedBy, int bpm, List<(int key, double ms)> notes)
    {
        var arr = new JsonArray();
        foreach (var (key, ms) in notes.OrderBy(n => n.ms))
            arr.Add(new JsonObject { ["time"] = (long)Math.Round(ms), ["key"] = $"1Key{key}" });

        var root = new JsonObject
        {
            ["name"] = name,
            ["author"] = "",
            ["transcribedBy"] = transcribedBy,
            ["isComposed"] = true,
            ["bpm"] = bpm,
            ["bitsPerPage"] = 16,
            ["pitchLevel"] = 0,
            ["isEncrypted"] = false,
            ["keyCount"] = 15,
            ["songNotes"] = arr
        };

        Directory.CreateDirectory(SongsDir);
        string path = UniquePath(name);
        File.WriteAllText(path, new JsonArray(root).ToJsonString(new JsonSerializerOptions { WriteIndented = false }), new UTF8Encoding(false));
        return path;
    }

    // songs 目录下按曲名生成不冲突的 .txt 路径
    static string UniquePath(string name)
    {
        string safe = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
        if (safe.Length == 0) safe = "未命名";
        string path = Path.Combine(SongsDir, safe + ".txt");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(SongsDir, $"{safe} ({i}).txt");
        return path;
    }

    static int ParseKeyNode(JsonNode? k)
    {
        if (k is JsonValue v)
        {
            if (v.TryGetValue(out int iv)) return (iv >= 0 && iv < 15) ? iv : -1;
            if (v.TryGetValue(out string? sv) && sv != null)
            {
                var m = Regex.Match(sv, @"(\d+)\s*$");
                if (m.Success && int.TryParse(m.Value, out int n) && n >= 0 && n < 15) return n;
            }
        }
        return -1;
    }

    // 曲谱存的 BPM 是取整的(如 280 实为 280.37), 直接用会累积偏移。
    // 在标称 BPM 附近微调, 找让全部音符最贴合 16 分音符格(cell=15000/bpm)的真实拍长。
    static double RefineMsPerBeat(IEnumerable<double> times, double nominalBpm)
    {
        var ts = times.Where(t => t > 0).ToList();
        if (ts.Count == 0) return 60000.0 / nominalBpm;
        double bestBpm = nominalBpm, bestErr = double.MaxValue;
        for (double bpm = nominalBpm - 1.0; bpm <= nominalBpm + 1.0 + 1e-9; bpm += 0.01)
        {
            double cell = 15000.0 / bpm;   // 16 分格时长
            double err = 0;
            foreach (double t in ts)
            {
                double r = t % cell;
                err += Math.Min(r, cell - r);   // 到最近格线的距离
            }
            if (err < bestErr) { bestErr = err; bestBpm = bpm; }
        }
        return 60000.0 / bestBpm;
    }

    // key 可能是 "1Key5" 字符串或整数
    static int ParseKey(JsonElement k)
    {
        if (k.ValueKind == JsonValueKind.Number) return k.GetInt32();
        if (k.ValueKind == JsonValueKind.String)
        {
            var m = Regex.Match(k.GetString() ?? "", @"(\d+)\s*$");
            if (m.Success && int.TryParse(m.Value, out int v) && v >= 0 && v < 15) return v;
        }
        return -1;
    }

    // 曲谱根: 可能是对象, 也可能是数组(取第一个元素)
    static JsonElement ParseRoot(string text)
    {
        var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        return root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 ? root[0] : root;
    }

    // 兼容 UTF-8 / UTF-8 BOM / UTF-16 LE/BE BOM (Sky 曲谱来源编码不一)
    static string ReadText(string path)
    {
        var b = File.ReadAllBytes(path);
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        return Encoding.UTF8.GetString(b).Trim();
    }
}
