using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace SMAP_WPF;

sealed class EditorViewPreferences
{
    public Dictionary<Guid, string> Colors { get; set; } = new();
    public bool ReduceMotion { get; set; }
    static string FileName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SMAP", "editor-view.json");
    public static readonly string[] Palette = { "#72b1ff", "#b8a0f5", "#74c6b0", "#efb24f", "#f08db4", "#a5c46d" };
    public Color Color(Guid id, int index)
    {
        string hex = Colors.GetValueOrDefault(id) ?? Palette[Math.Max(0, index) % Palette.Length];
        try { return (Color)ColorConverter.ConvertFromString(hex); } catch { return System.Windows.Media.Colors.CornflowerBlue; }
    }
    public static EditorViewPreferences Load()
    {
        try { return JsonSerializer.Deserialize<EditorViewPreferences>(File.ReadAllText(FileName)) is { Colors: not null } view ? view : new(); }
        catch { return new(); }
    }
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FileName)!);
            string temporary = FileName + ".tmp"; File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, FileName, true);
        }
        catch (Exception error) { Logger.Exception("编辑视图设置保存失败", error); }
    }
}
