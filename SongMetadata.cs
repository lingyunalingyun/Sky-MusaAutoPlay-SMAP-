using System.IO;
using System.Text;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed record SongMetadataTarget(string Kind, string Id);
public sealed record SongMetadataValues
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string TranscribedBy { get; init; } = "";
    public string Description { get; init; } = "";
    public int? Difficulty { get; init; }
    public string[] Tags { get; init; } = [];
    public string CoverMode { get; init; } = "original";
    public string? CoverSha256 { get; init; }
    public object Editable() => new { Title, Artist, TranscribedBy, Description, Difficulty, Tags };
    public void Validate(bool writing, int descriptionLimit)
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.EnumerateRunes().Count() > 150 || Artist == null || Artist.EnumerateRunes().Count() > 100
            || TranscribedBy == null || TranscribedBy.EnumerateRunes().Count() > 100 || Description == null || Description.EnumerateRunes().Count() > descriptionLimit
            || Tags == null || Tags.Length > 5 || Tags.Any(t => t == null || t.EnumerateRunes().Count() > (writing ? 32 : 255))
            || Difficulty is < 1 or > 5 || CoverMode is not ("original" or "custom" or "none")
            || CoverSha256 != null && (CoverSha256.Length != 64 || CoverSha256.Any(c => !char.IsAsciiHexDigitLower(c)))
            || CoverMode == "custom" && CoverSha256 == null) throw new SharingException("INVALID_METADATA");
        if (writing)
            try { new SharingPresentation { Difficulty = Difficulty, Tags = Tags }.Validate(); }
            catch (InvalidDataException) { throw new SharingException("INVALID_METADATA"); }
    }
}
public sealed record SongMetadataTiming(decimal? Bpm, decimal? MaxBpm, string[] Meters, int? GridSubdiv, decimal? DurationSeconds);
public sealed record SongMetadataProfile
{
    public int SchemaVersion { get; init; }
    public string Kind { get; init; } = "";
    public string Id { get; init; } = "";
    public int Revision { get; init; }
    public bool CanEdit { get; init; }
    public string? Authority { get; init; }
    public int OwnerUserId { get; init; }
    public int DescriptionLimit { get; init; }
    public SongMetadataValues Metadata { get; init; } = new();
    public SongMetadataTiming? Timing { get; init; }
    public Guid? VersionId { get; init; }
    public int? VersionNumber { get; init; }
    public string? ShareCode { get; init; }
    public string CoverUrl { get; init; } = "";
    public void Validate(SongMetadataTarget target)
    {
        if (SchemaVersion != 1 || Kind != target.Kind || Id != target.Id || Revision < 0 || OwnerUserId < 1
            || Kind is not ("work" or "legacy") || DescriptionLimit != (Kind == "legacy" ? 500 : 2000)
            || CanEdit && Authority is not ("publisher" or "manager") || !CanEdit && Authority != null || Metadata == null
            || CoverUrl == null || CoverUrl.Length > 256 || Kind == "work" && (VersionId == null || VersionId == Guid.Empty || VersionNumber is null or < 1 || ShareCode == null || NativeSharingClient.NormalizeCode(ShareCode) != ShareCode)) throw new SharingException("INVALID_RESPONSE");
        Metadata.Validate(false, DescriptionLimit);
        if (Timing is { } t && (t.Meters == null || t.Meters.Length > 32 || t.Meters.Any(m => m == null || m.Length > 32)
            || t.Bpm <= 0 || t.MaxBpm <= 0 || t.DurationSeconds < 0 || t.GridSubdiv < 1)) throw new SharingException("INVALID_RESPONSE");
    }
}
public sealed record SongMetadataRevision(int Revision, int ActorId, string ActorName, string Authority, long CreatedAt, SongMetadataValues Before, SongMetadataValues After);
public sealed record SongMetadataHistory(List<SongMetadataRevision> Items, int? NextBefore);
