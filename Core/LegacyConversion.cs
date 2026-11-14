using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SMAP_WPF.Core;

public sealed class LegacyConversionException(string code, int songIndex = -1, int noteIndex = -1) : IOException(code)
{
    public string Code { get; } = code;
    public int SongIndex { get; } = songIndex;
    public int NoteIndex { get; } = noteIndex;
}
public sealed record LegacyConversionEntry(int SongIndex, SmapProject? Project, string? Error, int NoteIndex = -1);

// Version describes conversion, not a new PerformanceIdentity algorithm. Original JSON is retained as provenance.
public static class LegacyConversion
{
    public const int Version = 1;
    public const int MaxSongs = 256;
    public static IReadOnlyList<LegacyConversionEntry> Read(byte[] bytes)
    {
        if (bytes.Length is 0 or > SmapFile.MaxManifestBytes) throw new LegacyConversionException("LEGACY_SIZE_LIMIT");
        string text;
        try
        {
            text = bytes.AsSpan().StartsWith(new byte[] { 255, 254 }) ? new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2)
                : bytes.AsSpan().StartsWith(new byte[] { 254, 255 }) ? new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2)
                : new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            using var parsed = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 48 });
            CheckNames(parsed.RootElement);
        }
        catch (Exception e) when (e is DecoderFallbackException or JsonException) { throw new LegacyConversionException("LEGACY_INVALID_JSON"); }
        var node = JsonNode.Parse(text)!;
        var songs = node is JsonArray array ? array.ToArray() : new[] { node };
        if (songs.Length is 0 or > MaxSongs) throw new LegacyConversionException("LEGACY_SONG_LIMIT");
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return songs.Select((song, index) =>
        {
            try { return new LegacyConversionEntry(index, ConvertSong(song as JsonObject ?? throw new LegacyConversionException("LEGACY_INVALID_SONG"), index, digest), null); }
            catch (LegacyConversionException e) { return new LegacyConversionEntry(index, null, e.Code, e.NoteIndex); }
            catch (Exception e) when (e is InvalidOperationException or FormatException or OverflowException or ArgumentException or InvalidDataException)
            { return new LegacyConversionEntry(index, null, "LEGACY_UNSUPPORTED_DATA"); }
        }).ToArray();
    }
    static void CheckNames(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in node.EnumerateObject()) { if (!names.Add(item.Name)) throw new JsonException(); CheckNames(item.Value); }
        }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var item in node.EnumerateArray()) CheckNames(item);
    }
    public static SmapProject Load(string path, int? index = null)
    {
        if (new FileInfo(path).Length > SmapFile.MaxManifestBytes) throw new LegacyConversionException("LEGACY_SIZE_LIMIT");
        var entries = Read(File.ReadAllBytes(path));
        if (index == null && entries.Count != 1) throw new InvalidOperationException("文件包含多个曲谱，请明确选择曲谱索引。");
        int selected = index ?? 0;
        if (selected < 0 || selected >= entries.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var entry = entries[selected];
        return entry.Project ?? throw new LegacyConversionException(entry.Error!, selected, entry.NoteIndex);
    }
    static SmapProject ConvertSong(JsonObject root, int index, string digest)
    {
        if (root["isEncrypted"]?.GetValue<bool>() == true) throw new LegacyConversionException("LEGACY_ENCRYPTED");
        if (root["songNotes"] is not JsonArray notes) throw new LegacyConversionException("LEGACY_MISSING_NOTES");
        if (root.ContainsKey("tempoEvents") || root.ContainsKey("timeSignatures")) throw new LegacyConversionException("LEGACY_UNSUPPORTED_TIMELINE");
        int count = root["keyCount"]?.GetValue<int>() ?? 15;
        if (count is not (4 or 8 or 15)) throw new LegacyConversionException("LEGACY_KEY_LAYOUT");
        if (root["keyLayout"] is JsonValue layout && layout.GetValue<string>() != "ordinal") throw new LegacyConversionException("LEGACY_KEY_LAYOUT");
        var unit = root["timeUnit"]?.GetValue<string>() ?? "ms";
        if (unit is not ("ms" or "s" or "seconds" or "beats")) throw new LegacyConversionException("LEGACY_TIME_UNIT");
        double bpm = 120;
        if (root["bpm"] is JsonValue bpmNode && !bpmNode.TryGetValue<double>(out bpm))
        {
            if (!bpmNode.TryGetValue<string>(out var bpmText) || !double.TryParse(bpmText, NumberStyles.Float, CultureInfo.InvariantCulture, out bpm)) throw new LegacyConversionException("LEGACY_BPM");
        }
        if (!double.IsFinite(bpm) || bpm <= 1 || bpm > 600000) throw new LegacyConversionException("LEGACY_BPM");
        var raw = new List<(double Time, int Key, double Length)>();
        for (int i = 0; i < notes.Count; i++)
        {
            if (notes[i] is not JsonObject note || note["time"] is not JsonValue time || !time.TryGetValue<double>(out var t) || !double.IsFinite(t) || t < 0)
                throw new LegacyConversionException("LEGACY_NOTE_TIME", index, i);
            int key = -1;
            if (note["key"] is JsonValue kv)
            {
                if (!kv.TryGetValue<int>(out key))
                {
                    if (!kv.TryGetValue<string>(out var ks) || !Regex.IsMatch(ks.Trim(), @"^(?:\d+Key|Key)?\d+$", RegexOptions.CultureInvariant)) key = -1;
                    else if (!int.TryParse(Regex.Match(ks.Trim(), @"\d+$").Value, out key)) key = -1;
                }
            }
            if (key < 0 || key >= count) throw new LegacyConversionException("LEGACY_NOTE_KEY", index, i);
            double length = note["duration"]?.GetValue<double>() ?? 0;
            if (note["endTime"] != null)
            {
                var endLength = note["endTime"]!.GetValue<double>() - t;
                if (note["duration"] != null && Math.Abs(endLength - length) > 0.000001) throw new LegacyConversionException("LEGACY_HOLD_AMBIGUOUS", index, i);
                length = endLength;
            }
            var action = note["type"]?.GetValue<string>();
            if (action != null && action is not ("tap" or "hold")) throw new LegacyConversionException("LEGACY_NOTE_ACTION", index, i);
            if (!double.IsFinite(length) || length < 0 || (action == "hold" && length <= 0) || (action == "tap" && length != 0)
                || note.ContainsKey("hold") || note.ContainsKey("length")) throw new LegacyConversionException("LEGACY_HOLD_AMBIGUOUS", index, i);
            double scale = unit is "s" or "seconds" ? 1000 : unit == "beats" ? 60000 / bpm : 1;
            raw.Add((t * scale, key, length * scale));
        }
        // Keep the exact v2.2 conversion arithmetic for ordinary millisecond songs.
        double msPerBeat = Refine(raw.Select(n => n.Time), bpm);
        var id = Guid.NewGuid(); var fields = root.DeepClone().AsObject(); fields.Remove("songNotes");
        fields["smapLegacySource"] = new JsonObject { ["converterVersion"] = Version, ["interpretation"] = "legacy-v2.2-audible-ordinal; layer labels retained in originalSong", ["fileSha256"] = digest, ["songIndex"] = index, ["originalSong"] = root.DeepClone() };
        string name = root["name"]?.GetValue<string>() ?? "Untitled";
        InstrumentCapability? capability = null;
        if (count != 15)
        {
            int[] logical = count == 8 ? new[] { 1, 5, 7, 8, 10, 11, 12, 14 } : new[] { 0, 4, 10, 14 };
            int cols = count == 8 ? 4 : 2;
            capability = new InstrumentCapability { Id = $"legacy:ordinal:{count}:1", InstrumentId = count == 4 ? "Custom4" : "Legacy8", LayoutId = $"grid:2x{cols}:1",
                Kind = GameInstrumentKind.UnpitchedPercussion, Verification = PitchVerification.UserProvided, Rows = 2, Columns = cols, Sources = new() { "legacy keyCount; ordinal layout; pitches unverified" },
                Keys = logical.Select((k, i) => new InstrumentKey($"key-{i}", k, i, null, "", k)).ToList(),
                Slots = logical.Select((_, i) => new InstrumentSlot(i / cols, i % cols, $"key-{i}")).ToList() };
        }
        var project = new SmapProject { FormatVersion = capability == null ? 1 : 2, KeyInterpretationVersion = capability == null ? 0 : 1,
            Metadata = new() { Name = name, Author = root["author"]?.GetValue<string>() ?? "", TranscribedBy = root["transcribedBy"]?.GetValue<string>() ?? "", Cover = root["cover"]?.GetValue<string>(), Description = root["description"]?.GetValue<string>() ?? "" },
            Tempos = new() { new(0, (decimal)msPerBeat * 1000) }, Tracks = new() { new() { Id = id, Name = name, LegacyFields = fields, Capability = capability,
                Notes = raw.Select(n => new ProjectNote { TrackId = id, Key = n.Key, Beat = (decimal)(n.Time / msPerBeat), LengthBeats = (decimal)(n.Length / msPerBeat), Action = n.Length > 0 ? NoteAction.Hold : NoteAction.Tap }).ToList() } } };
        ProjectValidation.Validate(project); return project;
    }
    static double Refine(IEnumerable<double> times, double nominalBpm)
    {
        var ts = times.Where(t => t > 0).ToList(); if (ts.Count == 0) return 60000.0 / nominalBpm;
        double bestBpm = nominalBpm, bestErr = double.MaxValue;
        for (double bpm = nominalBpm - 1.0; bpm <= nominalBpm + 1.0 + 1e-9; bpm += 0.01)
        {
            double cell = 15000.0 / bpm, err = 0;
            foreach (double t in ts) { double r = t % cell; err += Math.Min(r, cell - r); }
            if (err < bestErr) { bestErr = err; bestBpm = bpm; }
        }
        return 60000.0 / bestBpm;
    }
}
