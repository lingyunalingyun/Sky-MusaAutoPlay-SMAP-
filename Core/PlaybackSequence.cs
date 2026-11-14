using System.Collections.ObjectModel;

namespace SMAP_WPF.Core;

public sealed record PreviewSettings(string Instrument, int? SemitoneOffset);
public readonly record struct NoteOutputEvent(TimedProjectNote Note, bool IsOn, bool IsResume = false);

public interface INoteOutput
{
    void Apply(IReadOnlyList<NoteOutputEvent> changes);
    void ReleaseAll() => StopAll();
    void StopAll();
}

public interface IMonotonicClock
{
    double Milliseconds { get; }
}

public sealed class StopwatchClock : IMonotonicClock
{
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    public double Milliseconds => _clock.Elapsed.TotalMilliseconds;
}

public sealed class PlaybackSequence
{
    public static PlaybackSequence Empty { get; } = new(Array.Empty<TimedProjectNote>());
    public IReadOnlyList<TimedProjectNote> Notes { get; }
    public IReadOnlyDictionary<Guid, PreviewSettings> Preview { get; }
    public decimal DurationMicroseconds { get; }
    public double DurationMs => (double)(DurationMicroseconds / 1000);

    public PlaybackSequence(IEnumerable<TimedProjectNote> notes, IReadOnlyDictionary<Guid, PreviewSettings>? preview = null,
        decimal minimumDurationMicroseconds = 0)
    {
        if (minimumDurationMicroseconds < 0) throw new ArgumentOutOfRangeException(nameof(minimumDurationMicroseconds));
        DurationMicroseconds = minimumDurationMicroseconds;
        var values = notes.OrderBy(n => n.StartMicroseconds).ThenBy(n => n.Key).ThenBy(n => n.NoteId).ToArray();
        var ids = new HashSet<Guid>();
        if (values.Length > ProjectValidation.MaxNotes) throw new ArgumentException("音符数量超出范围。", nameof(notes));
        foreach (var n in values)
        {
            if (n.NoteId == Guid.Empty || n.TrackId == Guid.Empty || !ids.Add(n.NoteId) || n.Key is < 0 or >= 15
                || n.StartMicroseconds < 0 || !((n.Action == NoteAction.Tap && n.LengthMicroseconds == 0)
                    || (n.Action == NoteAction.Hold && n.LengthMicroseconds > 0)) || (n.RetriggerOnOverlap && n.Action != NoteAction.Hold))
                throw new ArgumentException("演奏音符无效。", nameof(notes));
            DurationMicroseconds = Math.Max(DurationMicroseconds, checked(n.StartMicroseconds + n.LengthMicroseconds));
        }
        Notes = Array.AsReadOnly(values);
        Preview = new ReadOnlyDictionary<Guid, PreviewSettings>(preview?.ToDictionary(p => p.Key, p => p.Value) ?? new());
    }

    public static PlaybackSequence FromProject(SmapProject project, bool useTrackPreview = true, Guid? trackId = null)
    {
        ProjectValidation.Validate(project);
        var tracks = trackId is { } id ? project.Tracks.Where(t => t.Id == id).ToArray() : project.AudibleTracks().ToArray();
        if (trackId != null && tracks.Length == 0) throw new ArgumentException("音轨不存在。", nameof(trackId));
        var timeline = new ProjectTimeline(project.Tempos);
        return new(tracks.SelectMany(timeline.GetNotes), useTrackPreview
            ? tracks.ToDictionary(t => t.Id, t => new PreviewSettings(t.PreviewInstrument, t.PreviewSemitoneOffset)) : null);
    }

    public static PlaybackSequence FromLegacy(IEnumerable<(int key, double ms)> notes)
    {
        var track = Guid.NewGuid();
        return new(notes.Where(n => n.key is >= 0 and < 15).Select(n =>
        {
            if (!double.IsFinite(n.ms) || n.ms < 0) throw new ArgumentException("旧音符时间无效。", nameof(notes));
            return new TimedProjectNote(track, Guid.NewGuid(), n.key, NoteAction.Tap, (decimal)n.ms * 1000, 0);
        }));
    }
}
