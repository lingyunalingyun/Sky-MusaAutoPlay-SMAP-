using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SMAP_WPF;

/// <summary>曲谱封面: 压缩(限最长边+JPEG质量) / 本地 .txt 内嵌读写(首对象 "cover" base64) / 字节转可显示位图。</summary>
static class CoverUtil
{
    /// <summary>压缩图片文件为 JPEG 字节: 最长边限 maxEdge, 质量 quality。</summary>
    public static byte[]? CompressToJpeg(string path, int maxEdge = 512, int quality = 80)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.EndInit();
            double m = Math.Max(bmp.PixelWidth, bmp.PixelHeight);
            BitmapSource src = bmp;
            if (m > maxEdge)
            {
                double s = maxEdge / m;
                src = new TransformedBitmap(bmp, new ScaleTransform(s, s));
            }
            var enc = new JpegBitmapEncoder { QualityLevel = quality };
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    /// <summary>字节 → 冻结的可显示位图(可跨线程)。</summary>
    public static BitmapImage? FromBytes(byte[]? data)
    {
        if (data == null || data.Length == 0) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(data);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>读本地曲谱内嵌封面(首对象 "cover", base64 或 data URI) → JPEG 字节; 无则 null。</summary>
    public static byte[]? ReadEmbedded(string txtPath)
    {
        try
        {
            var text = File.ReadAllText(txtPath);
            if (!text.Contains("\"cover\"")) return null;   // 无封面字段: 免去大 JSON 全解析(库批量扫描更快)
            var o = FirstObject(JsonNode.Parse(text));
            var s = o?["cover"]?.GetValue<string>();
            if (string.IsNullOrEmpty(s)) return null;
            if (s.StartsWith("data:")) { int i = s.IndexOf(','); if (i >= 0) s = s[(i + 1)..]; }
            return Convert.FromBase64String(s);
        }
        catch { return null; }
    }

    /// <summary>把封面 base64 写进本地曲谱(首对象 "cover"), 保留其余字段; 成功 true。</summary>
    public static bool WriteEmbedded(string txtPath, byte[] jpeg)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(txtPath));
            var o = FirstObject(node);
            if (o == null) return false;
            o["cover"] = Convert.ToBase64String(jpeg);
            File.WriteAllText(txtPath, node!.ToJsonString());
            return true;
        }
        catch { return false; }
    }

    static JsonObject? FirstObject(JsonNode? node) =>
        node is JsonArray a ? (a.Count > 0 ? a[0] as JsonObject : null) : node as JsonObject;
}
