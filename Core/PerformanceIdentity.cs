using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SMAP_WPF.Core;

public static class PerformanceIdentity
{
    public const int CanonicalVersion = 1;
    public const int RetriggerCanonicalVersion = 2;
    public const int CapabilityCanonicalVersion = 3;

    public static string ForProject(SmapProject project) => ForTracks(project, project.AudibleTracks().Select(t => t.Id));

    public static string ForTrack(SmapProject project, Guid trackId) => ForTracks(project, new[] { trackId });

    public static string ForTracks(SmapProject project, IEnumerable<Guid> trackIds)
    {
        ProjectValidation.Validate(project);
        var ids = trackIds.ToHashSet();
        if (ids.Except(project.Tracks.Select(t => t.Id)).Any()) throw new ArgumentException("音轨不存在。", nameof(trackIds));
        // 轨道和音符 ID 仅用于编辑关联，不参与演奏版本身份。
        var selected = project.Tracks.Where(t => ids.Contains(t.Id)).ToArray();
        bool capability = selected.Any(t => t.Capability != null || t.KeyDomain != NoteKeyDomain.LegacyOrdinal);
        bool extended = capability || selected.Any(t => t.Notes.Any(n => n.RetriggerOnOverlap));
        var tracks = selected.Select(t => CanonicalTrack(t, extended, capability))
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(capability ? CapabilityCanonicalVersion : extended ? RetriggerCanonicalVersion : CanonicalVersion);
            writer.WriteStartArray();
            foreach (var tempo in project.Tempos.OrderBy(t => t.Beat))
            {
                writer.WriteStartArray(); Number(writer, tempo.Beat); Number(writer, tempo.MicrosecondsPerBeat); writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WriteStartArray();
            foreach (var meter in project.Meters.OrderBy(m => m.Beat))
            {
                writer.WriteStartArray(); Number(writer, meter.Beat);
                writer.WriteNumberValue(meter.Numerator); writer.WriteNumberValue(meter.Denominator); writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WriteStartArray();
            foreach (string track in tracks) writer.WriteRawValue(track);
            writer.WriteEndArray(); writer.WriteEndArray();
        }
        return Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
    }

    static string CanonicalTrack(ProjectTrack track, bool extended, bool capability)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            if (track.GameTarget is not { } target) writer.WriteNullValue();
            else
            {
                writer.WriteStartArray(); writer.WriteStringValue(target.InstrumentId); writer.WriteStringValue(target.ScoreId);
                writer.WriteStringValue(target.ProfileId); writer.WriteNumberValue(target.ProfileVersion);
                writer.WriteStartArray(); foreach (decimal pitch in target.KeyMidiPitches) Number(writer, pitch);
                writer.WriteEndArray(); writer.WriteEndArray();
            }
            if (capability)
            {
                writer.WriteStartArray(); writer.WriteNumberValue((int)track.KeyDomain);
                if (track.Capability is not { } c) writer.WriteNullValue();
                else
                {
                    writer.WriteStartArray(); writer.WriteStringValue(c.Id); writer.WriteNumberValue(c.Version);
                    writer.WriteStringValue(c.InstrumentId); writer.WriteNumberValue((int)c.Kind);
                    foreach (var k in c.Keys.OrderBy(k => k.LogicalKey))
                    {
                        writer.WriteStartArray(); writer.WriteStringValue(k.Id); writer.WriteNumberValue(k.LogicalKey); writer.WriteNumberValue(k.LegacyKey);
                        if (k.MidiPitch is { } pitch) writer.WriteNumberValue(pitch); else writer.WriteNullValue();
                        writer.WriteStringValue(k.SoundId); writer.WriteNumberValue((int)k.Availability); writer.WriteEndArray();
                    }
                    writer.WriteEndArray();
                }
                writer.WriteEndArray();
            }
            writer.WriteStartArray();
            foreach (var note in track.Notes.OrderBy(n => n.Beat).ThenBy(n => n.Key).ThenBy(n => n.Action).ThenBy(n => n.LengthBeats).ThenBy(n => n.RetriggerOnOverlap))
            {
                // 规范单位为拍；保存起点与长度，避免相加时丢失极长音符的小数精度。
                writer.WriteStartArray(); writer.WriteNumberValue(note.Key); Number(writer, note.Beat);
                Number(writer, note.LengthBeats); writer.WriteNumberValue((int)note.Action);
                // 无重触发数据仍使用原有 v1 字节规则；v2 明确记录每个音符的重触发语义。
                if (extended) writer.WriteBooleanValue(note.RetriggerOnOverlap);
                writer.WriteEndArray();
            }
            writer.WriteEndArray(); writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    static void Number(Utf8JsonWriter writer, decimal value) =>
        writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture));
}
