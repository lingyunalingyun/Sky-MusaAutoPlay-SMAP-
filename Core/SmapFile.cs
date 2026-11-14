using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SMAP_WPF.Core;

public static class SmapFile
{
    public const int MaxManifestBytes = 16 * 1024 * 1024;
    public const int MaxMidiBytes = 16 * 1024 * 1024;
    public const int MaxArchiveBytes = MaxManifestBytes + MaxMidiBytes + 65536;
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        Converters = { new JsonStringEnumConverter(null, allowIntegerValues: false) }
    };

    public static void Save(SmapProject project, string path)
    {
        ProjectValidation.Validate(project);
        ValidateMidi(project.OriginalMidi);
        MidiProjectConverter.ValidateOriginal(project);
        using var manifest = new LimitedBuffer(MaxManifestBytes);
        JsonSerializer.Serialize(manifest, project, Options);
        string destination = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteEntry(archive, "project.json", manifest.GetBuffer().AsSpan(0, (int)manifest.Length));
                    if (project.OriginalMidi is { } midi) WriteEntry(archive, "source.mid", midi);
                }
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static SmapProject Load(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return LoadArchive(file);
    }

    public static byte[] ToBytes(SmapProject project)
    {
        ProjectValidation.Validate(project);
        ValidateMidi(project.OriginalMidi);
        MidiProjectConverter.ValidateOriginal(project);
        using var manifest = new LimitedBuffer(MaxManifestBytes);
        JsonSerializer.Serialize(manifest, project, Options);
        using var output = new LimitedBuffer(MaxArchiveBytes);
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "project.json", manifest.GetBuffer().AsSpan(0, (int)manifest.Length), deterministic: true);
            if (project.OriginalMidi is { } midi) WriteEntry(archive, "source.mid", midi, deterministic: true);
        }
        return output.ToArray();
    }

    public static SmapProject FromBytes(byte[] bytes)
    {
        if (bytes.Length > MaxArchiveBytes) throw new InvalidDataException("工程压缩包过大。");
        using var stream = new MemoryStream(bytes, writable: false);
        return LoadArchive(stream);
    }

    static SmapProject LoadArchive(Stream file)
    {
        if (file.Length > MaxArchiveBytes) throw new InvalidDataException("工程压缩包过大。");
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        if (archive.Entries.Count is < 1 or > 2) throw new InvalidDataException("工程压缩包条目数量无效。");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName is not ("project.json" or "source.mid") || !entries.TryAdd(entry.FullName, entry))
                throw new InvalidDataException("工程压缩包包含未知、重复或不安全的路径。");
        }
        if (!entries.TryGetValue("project.json", out var manifest)) throw new InvalidDataException("工程缺少 project.json。");
        var bytes = ReadEntry(manifest, MaxManifestBytes);
        SmapProject project;
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            CheckDuplicateProperties(document.RootElement);
            project = document.Deserialize<SmapProject>(Options) ?? throw new InvalidDataException("工程内容为空。");
        }
        catch (JsonException ex) { throw new InvalidDataException("工程 JSON 格式无效或缺少必要字段。", ex); }
        if (entries.TryGetValue("source.mid", out var source))
        {
            var midi = ReadEntry(source, MaxMidiBytes);
            ValidateMidi(midi);
            project = project with { OriginalMidi = midi };
        }
        ProjectValidation.Validate(project);
        if (project.MidiConversion is { } conversion && project.OriginalMidi is { } original)
            project = project with { MidiConversion = conversion with { Source = conversion.Source with { Bytes = original } } };
        MidiProjectConverter.ValidateOriginal(project);
        ProjectValidation.Validate(project);
        return project;
    }

    static void CheckDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("工程 JSON 包含重复字段。");
                CheckDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckDuplicateProperties(item);
    }

    static byte[] ReadEntry(ZipArchiveEntry entry, int limit)
    {
        if (entry.Length > limit) throw new InvalidDataException("工程解压后数据过大。");
        using var input = entry.Open();
        using var output = new LimitedBuffer(limit);
        input.CopyTo(output);
        if (output.Length != entry.Length) throw new InvalidDataException("工程压缩条目长度不匹配。");
        return output.ToArray();
    }

    static void WriteEntry(ZipArchive archive, string name, ReadOnlySpan<byte> bytes, bool deterministic = false)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        if (deterministic) entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var output = entry.Open();
        output.Write(bytes);
    }

    static void ValidateMidi(byte[]? midi)
    {
        if (midi is not null && (midi.Length is < 14 or > MaxMidiBytes || !midi.AsSpan(0, 4).SequenceEqual("MThd"u8)))
            throw new InvalidDataException("原 MIDI 数据头无效或数据过大。");
    }

    sealed class LimitedBuffer(int limit) : MemoryStream
    {
        void Check(int count)
        {
            if (count > limit - Position) throw new InvalidDataException("工程数据超出安全大小限制。");
        }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
    }
}
