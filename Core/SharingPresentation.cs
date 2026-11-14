using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SMAP_WPF.Core;

// Version display data lives in the sharing manifest, outside project.json and PerformanceIdentity.
public sealed record SharingPresentation
{
    public int SchemaVersion { get; init; } = 1;
    public int? Difficulty { get; init; }
    public string[] Tags { get; init; } = [];
    public string? CoverSha256 { get; init; }

    public void Validate()
    {
        if (SchemaVersion != 1 || Difficulty is < 1 or > 5 || Tags == null || Tags.Length > 5
            || Tags.Any(t => t == null || t != t.Trim() || t.Length == 0 || t.EnumerateRunes().Count() > 32 || t.Any(char.IsControl))
            || Tags.Select(TagKey).Distinct(StringComparer.Ordinal).Count() != Tags.Length
            || CoverSha256 != null && (CoverSha256.Length != 64 || CoverSha256.Any(c => !char.IsAsciiHexDigitLower(c))))
            throw new InvalidDataException("Invalid sharing presentation.");
    }

    public static string TagKey(string tag)
        => new(tag.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c).ToArray());

    public static string? CoverHash(string? cover)
    {
        if (cover == null) return null;
        if (cover.Length > 180_000) throw new InvalidDataException("Cover too large.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(cover); }
        catch (FormatException e) { throw new InvalidDataException("Invalid cover.", e); }
        if (bytes.Length is < 4 or > 128 * 1024 || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[^2] != 0xff || bytes[^1] != 0xd9)
            throw new InvalidDataException("Invalid JPEG cover.");
        bool dimensions = false;
        for (int i = 2; i < bytes.Length - 1;)
        {
            if (bytes[i++] != 0xff) throw new InvalidDataException("Invalid JPEG marker.");
            while (i < bytes.Length && bytes[i] == 0xff) i++;
            if (i >= bytes.Length) break;
            byte marker = bytes[i++];
            if (marker is 0xda or 0xd9) break;
            if (marker is 0x01 or >= 0xd0 and <= 0xd7) continue;
            if (i + 2 > bytes.Length) throw new InvalidDataException("Truncated JPEG cover.");
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i, 2));
            if (length < 2 || i + length > bytes.Length) throw new InvalidDataException("Truncated JPEG cover.");
            if (marker is 0xc0 or 0xc1 or 0xc2)
            {
                if (length < 8) throw new InvalidDataException("Invalid JPEG frame.");
                int height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i + 3, 2));
                int width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i + 5, 2));
                if (width is < 1 or > 512 || height is < 1 or > 512) throw new InvalidDataException("Invalid cover dimensions.");
                dimensions = true;
            }
            i += length;
        }
        if (!dimensions) throw new InvalidDataException("JPEG dimensions missing.");
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}

public sealed record SharePerformanceInfo(decimal MinBpm, decimal MaxBpm, int TempoCount, string[] Meters, int GridSubdiv, decimal DurationSeconds, int TrackCount, int MeterCount = 0)
{
    public static SharePerformanceInfo FromProject(SmapProject project)
    {
        var tracks = project.AudibleTracks().ToArray();
        decimal end = tracks.SelectMany(t => t.Notes).Select(n => n.Beat + n.LengthBeats).DefaultIfEmpty().Max();
        var tempos = project.Tempos.Where(t => t.Beat <= end).Select(t => 60_000_000m / t.MicrosecondsPerBeat).ToArray();
        var meters = new List<string>();
        foreach (var point in project.Meters.Where(m => m.Beat <= end).OrderBy(m => m.Beat))
        {
            string meter = $"{point.Numerator}/{point.Denominator}";
            if (meters.Count == 0 || meters[^1] != meter) meters.Add(meter);
        }
        return new(tempos.Min(), tempos.Max(), tempos.Length,
            meters.Take(32).ToArray(), project.GridSubdiv,
            new ProjectTimeline(project.Tempos).BeatToMicroseconds(end) / 1_000_000m, tracks.Length, meters.Count);
    }
}
