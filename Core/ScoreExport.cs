using System.IO;

namespace SMAP_WPF.Core;

public enum ScoreOrientation { Portrait, Landscape }
public enum ScoreSpacing { Standard, Compact }
public enum ScoreKeyState { Idle, Tap, HoldStart, HoldMiddle, HoldEnd, Absent }
public sealed record ScoreColumn(decimal Beat, string Label, IReadOnlyList<ScoreKeyState> Keys,
    decimal RestBeats, int EventOrder = 0)
{
    public decimal DurationBeats { get; init; }
    public bool HasEvent { get; init; }
}
public sealed record ScoreCell(Guid TrackId, long BarNumber, decimal StartBeat, decimal EndBeat, ScoreColumn Column);
public sealed record ScoreSlot(ScoreCell Cell, double Width);
public sealed record ScoreRow(IReadOnlyList<ScoreSlot> Slots);
public sealed record ScorePage(int Index, IReadOnlyList<ScoreRow> Rows);

public sealed record ScoreExportOptions
{
    public required IReadOnlyList<Guid> TrackIds { get; init; }
    public ScoreOrientation Orientation { get; init; }
    public ScoreSpacing Spacing { get; init; }
    public bool SimplifySpeed { get; init; } = true;
    public bool IncludeOriginalMidi { get; init; }
}

public sealed record ScoreExportDocument(SmapProject Project, byte[] Payload, string PerformanceHash,
    int CanonicalVersion, ScoreExportOptions Options, IReadOnlyList<ScorePage> Pages,
    IReadOnlyList<string> Notices, int DisplayFactor)
{
    public string? VerifiedShareCode { get; init; }
    public int Width => Options.Orientation == ScoreOrientation.Landscape ? 2240 : 1260;
    public int Height => Options.Orientation == ScoreOrientation.Landscape ? 1260 : 2240;
}

public static class ScoreExport
{
    public const int MaxPages = 256;
    const int MaxColumns = MaxPages * 48;

    // Serializing the projection also makes a detached snapshot of mutable note/tempo lists.
    public static SmapProject ProjectSelection(SmapProject project, ScoreExportOptions options)
    {
        ProjectValidation.Validate(project);
        var ids = options.TrackIds.ToHashSet();
        if (ids.Count == 0 || ids.Count != options.TrackIds.Count || ids.Except(project.Tracks.Select(t => t.Id)).Any())
            throw new InvalidDataException("ScoreInvalidTracks");
        var selected = options.TrackIds.Select(id => project.Tracks.Single(t => t.Id == id) with
        {
            Muted = false, Solo = false, LegacyFields = null,
            Notes = project.Tracks.Single(t => t.Id == id).Notes.Select(n => options.IncludeOriginalMidi ? n
                : n with { MidiOrigin = null, SourceMidiNoteId = null }).ToList()
        }).ToList();
        var snapshot = project with
        {
            Tracks = selected, Metadata = project.Metadata with { Cover = null },
            OriginalMidi = options.IncludeOriginalMidi ? project.OriginalMidi : null,
            MidiConversion = options.IncludeOriginalMidi ? project.MidiConversion : null
        };
        return SmapFile.FromBytes(SmapFile.ToBytes(snapshot));
    }

    public static ScoreExportDocument Prepare(SmapProject project, ScoreExportOptions options,
        CancellationToken cancellation = default)
    {
        if (!Enum.IsDefined(options.Orientation)) throw new InvalidDataException("ScoreInvalidOrientation");
        if (!Enum.IsDefined(options.Spacing)) throw new InvalidDataException("ScoreInvalidSpacing");
        var snapshot = ProjectSelection(project, options);
        var payload = SmapFile.ToBytes(snapshot);
        var notices = new HashSet<string>();
        int factor = ScoreLayout.DisplayFactor(snapshot, options.SimplifySpeed);
        foreach (var track in snapshot.Tracks)
        {
            var capability = InstrumentCapabilities.ForTrack(track);
            if (capability == null) notices.Add("ScoreUnknownCapability");
            else foreach (var note in track.Notes)
            {
                var key = capability.Resolve(note.Key, track.KeyDomain);
                if (key == null || key.Availability == KeyAvailability.Unavailable)
                    throw new InvalidDataException("ScoreUnavailableKey");
                if (key.Availability == KeyAvailability.Unknown) notices.Add("ScoreUnknownCapability");
            }
        }
        if (options.IncludeOriginalMidi && snapshot.OriginalMidi != null) notices.Add("ScoreIncludesOriginalMidi");
        var notes = snapshot.Tracks.SelectMany(t => t.Notes).ToArray();
        decimal last = notes.Length == 0 ? 0 : notes.Max(n => checked(n.Beat + n.LengthBeats));
        bool tapAtEnd = notes.Any(n => n.Action == NoteAction.Tap && n.Beat == last);
        var map = new EditorMeterMap(snapshot.Meters);
        decimal finish = map.EndOfBar(last, tapAtEnd || last == 0);
        var groups = new List<ScoreCell[]>();
        int columns = 0;
        // Per-track event sweeps retain independent note IDs; no grid rounding or pixel analysis.
        var sweeps = snapshot.Tracks.ToDictionary(t => t.Id, t => new TrackSweep(t));
        var meters = snapshot.Meters.OrderBy(m => m.Beat).ToArray();
        decimal start = 0; long barNumber = 1;
        while (start < finish)
        {
            cancellation.ThrowIfCancellationRequested();
            var meter = meters.Last(m => m.Beat <= start);
            decimal unit = 4m / meter.Denominator;
            if (unit == 0 || meter.Numerator > 128) throw new InvalidDataException("ScoreTooLarge");
            decimal end = Math.Min(finish, checked(start + meter.Numerator * unit));
            var nextMeter = meters.FirstOrDefault(m => m.Beat > start);
            if (nextMeter != null) end = Math.Min(end, nextMeter.Beat);
            if (end <= start) throw new InvalidDataException("ScoreTooLarge");
            var moments = new SortedSet<decimal>();
            foreach (var sweep in sweeps.Values)
                foreach (decimal b in sweep.EventsIn(start, end, end == finish)) moments.Add(b);
            foreach (var t in snapshot.Tempos.Where(t => t.Beat >= start && t.Beat < end)) moments.Add(t.Beat);
            // Skip only redundant background grid points near real events, never the events themselves.
            // Legacy millisecond conversion often puts notes just beside a denominator pulse.
            var events = moments.ToArray();
            for (int i = 0; i < meter.Numerator; i++)
            {
                decimal b = start + i * unit; if (b >= end) break;
                int index = Array.BinarySearch(events, b);
                if (index >= 0) continue;
                index = ~index;
                decimal tolerance = unit / 96;
                if (index < events.Length && events[index] - b <= tolerance
                    || index > 0 && b - events[index - 1] <= tolerance) continue;
                moments.Add(b);
            }
            if (moments.Count > (MaxColumns - columns) / snapshot.Tracks.Count) throw new InvalidDataException("ScoreTooLarge");
            var byTrack = snapshot.Tracks.ToDictionary(t => t.Id, _ => new List<ScoreColumn>());
            var beats = moments.ToArray();
            string previousLabel = "";
            for (int i = 0; i < beats.Length; i++)
            {
                decimal beat = beats[i], next = i + 1 < beats.Length ? beats[i + 1] : end;
                string label = ScoreLayout.BeatLabel(snapshot, start, end, beat, factor);
                if (label == previousLabel) label = "·"; else previousLabel = label;
                foreach (var track in snapshot.Tracks)
                {
                    var cells = sweeps[track.Id].At(beat, label, (next - beat) / unit).ToArray();
                    byTrack[track.Id].AddRange(cells);
                }
            }
            // Align simultaneous tracks, including separate release/restart columns at the same beat.
            var keys = byTrack.Values.SelectMany(c => c.Select(x => (x.Beat, x.EventOrder))).Distinct().OrderBy(x => x.Beat).ThenBy(x => x.EventOrder).ToArray();
            if (keys.Length > (MaxColumns - columns) / snapshot.Tracks.Count) throw new InvalidDataException("ScoreTooLarge");
            columns += keys.Length * snapshot.Tracks.Count;
            foreach (var track in snapshot.Tracks)
            {
                var own = byTrack[track.Id].ToDictionary(c => (c.Beat, c.EventOrder));
                var aligned = new List<ScoreColumn>();
                foreach (var key in keys)
                {
                    if (own.TryGetValue(key, out var column)) aligned.Add(column);
                    else
                    {
                        var existing = own[(key.Beat, 0)];
                        aligned.Add(existing with { Keys = existing.Keys.Select(k => k == ScoreKeyState.HoldStart ? ScoreKeyState.HoldMiddle : k is ScoreKeyState.HoldEnd or ScoreKeyState.Tap ? ScoreKeyState.Idle : k).ToArray(), EventOrder = key.EventOrder, RestBeats = 0, HasEvent = false });
                    }
                }
                byTrack[track.Id] = aligned;
            }
            for (int i = 0; i < keys.Length; i++)
            {
                decimal duration = i + 1 < keys.Length ? keys[i + 1].Beat - keys[i].Beat : end - keys[i].Beat;
                var group = snapshot.Tracks.Select(t =>
                {
                    var c = byTrack[t.Id][i];
                    bool rest = c.Keys.All(k => k is ScoreKeyState.Idle or ScoreKeyState.Absent);
                    return new ScoreCell(t.Id, barNumber, start, end, c with { DurationBeats = duration, RestBeats = rest ? duration / unit : 0 });
                }).ToArray();
                bool context = snapshot.Tempos.Any(t => t.Beat == keys[i].Beat) || snapshot.Meters.Any(m => m.Beat == keys[i].Beat);
                bool silent = group.All(c => c.Column.RestBeats > 0 && !c.Column.HasEvent);
                if (options.Spacing == ScoreSpacing.Compact && !context && silent && groups.Count > 0
                    && groups[^1].All(c => c.BarNumber == barNumber && c.Column.RestBeats > 0 && !c.Column.HasEvent))
                {
                    groups[^1] = groups[^1].Select((cell, j) => cell with { Column = cell.Column with
                    { DurationBeats = cell.Column.DurationBeats + group[j].Column.DurationBeats, RestBeats = cell.Column.RestBeats + group[j].Column.RestBeats } }).ToArray();
                }
                else groups.Add(group);
            }
            start = end; barNumber++;
        }
        var pages = ScoreLayout.Paginate(groups, options, factor, cancellation);
        return new(snapshot, payload, PerformanceIdentity.ForTracks(snapshot, options.TrackIds), Canonical(snapshot),
            options with { TrackIds = options.TrackIds.ToArray() }, pages, notices.Order().ToArray(), factor);
    }

    public static int Canonical(SmapProject p) => p.Tracks.Any(t => t.Capability != null || t.KeyDomain != NoteKeyDomain.LegacyOrdinal)
        ? PerformanceIdentity.CapabilityCanonicalVersion : p.Tracks.Any(t => t.Notes.Any(n => n.RetriggerOnOverlap))
        ? PerformanceIdentity.RetriggerCanonicalVersion : PerformanceIdentity.CanonicalVersion;

    sealed class TrackSweep
    {
        readonly ProjectTrack _track;
        readonly InstrumentCapability? _capability;
        readonly SortedDictionary<decimal, List<(ProjectNote Note, bool Start)>> _events = new();
        readonly Dictionary<Guid, ProjectNote> _active = new();
        readonly decimal[] _beats;
        readonly bool[] _available = Enumerable.Repeat(true, 15).ToArray();
        public TrackSweep(ProjectTrack track)
        {
            _track = track; _capability = InstrumentCapabilities.ForTrack(track);
            if (_capability != null) _available = Enumerable.Range(0, 15).Select(i => _capability.Keys.Any(k => k.LogicalKey == i)).ToArray();
            foreach (var n in track.Notes)
            {
                Add(n.Beat, n, true);
                if (n.Action == NoteAction.Hold) Add(n.Beat + n.LengthBeats, n, false);
            }
            _beats = _events.Keys.ToArray();
        }
        void Add(decimal beat, ProjectNote note, bool start)
        {
            if (!_events.TryGetValue(beat, out var list)) _events[beat] = list = new();
            list.Add((note, start));
        }
        int Key(ProjectNote n) => _capability?.Resolve(n.Key, _track.KeyDomain)?.LogicalKey ?? n.Key;
        public IEnumerable<decimal> EventsIn(decimal start, decimal end, bool last)
        {
            int index = Array.BinarySearch(_beats, start); if (index < 0) index = ~index;
            while (index < _beats.Length && (_beats[index] < end || last && _beats[index] == end)) yield return _beats[index++];
        }
        public IEnumerable<ScoreColumn> At(decimal beat, string label, decimal rest)
        {
            var changes = _events.GetValueOrDefault(beat) ?? new();
            var ended = changes.Where(c => !c.Start).Select(c => c.Note).ToArray();
            foreach (var n in ended) _active.Remove(n.Id);
            var starting = changes.Where(c => c.Start).Select(c => c.Note).ToArray();
            var releaseKeys = ended.Select(Key).Where(k => !_active.Values.Any(n => Key(n) == k)).ToHashSet();
            var retrigger = starting.GroupBy(Key).Where(g => _active.Values.Any(a => Key(a) == g.Key)
                && (g.Any(n => n.Action == NoteAction.Tap) || g.Any(n => n.RetriggerOnOverlap))).Select(g => g.Key);
            releaseKeys.UnionWith(retrigger);
            bool conflict = starting.Any(n => releaseKeys.Contains(Key(n)));
            if (conflict)
                yield return new(beat, label, States(releaseKeys, Array.Empty<ProjectNote>()), 0) { HasEvent = changes.Count > 0 };
            var states = States(conflict ? new HashSet<int>() : releaseKeys, starting);
            foreach (var n in starting.Where(n => n.Action == NoteAction.Hold)) _active[n.Id] = n;
            yield return new(beat, label, states, states.All(s => s is ScoreKeyState.Idle or ScoreKeyState.Absent) ? rest : 0, conflict ? 1 : 0) { HasEvent = changes.Count > 0 };
        }
        ScoreKeyState[] States(HashSet<int> releases, IReadOnlyList<ProjectNote> starts)
        {
            var values = Enumerable.Range(0, 15).Select(k => !_available[k] ? ScoreKeyState.Absent
                : releases.Contains(k) ? ScoreKeyState.HoldEnd : _active.Values.Any(n => Key(n) == k) ? ScoreKeyState.HoldMiddle : ScoreKeyState.Idle).ToArray();
            foreach (var group in starts.GroupBy(Key))
            {
                bool merge = _active.Values.Any(n => Key(n) == group.Key) && group.All(n => n.Action == NoteAction.Hold && !n.RetriggerOnOverlap);
                values[group.Key] = merge ? ScoreKeyState.HoldMiddle : group.Any(n => n.Action == NoteAction.Hold) ? ScoreKeyState.HoldStart : ScoreKeyState.Tap;
            }
            return values;
        }
    }
}
