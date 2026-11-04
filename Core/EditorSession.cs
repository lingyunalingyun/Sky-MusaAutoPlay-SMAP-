using System.IO;

namespace SMAP_WPF.Core;

public sealed partial class EditorSession
{
    sealed record NoteDelta(Guid Track, ProjectNote? Before, ProjectNote? After);
    sealed record TrackDelta(int Index, ProjectTrack? Before, ProjectTrack? After);
    sealed record Edit(SmapProject Before, SmapProject After, NoteDelta[] Notes, TrackDelta[] Tracks,
        Guid BeforeState, Guid AfterState)
    {
        public int Weight => Notes.Sum(n => (n.Before == null ? 0 : 1) + (n.After == null ? 0 : 1))
            + Tracks.Sum(t => (t.Before?.Notes.Count ?? 0) + (t.After?.Notes.Count ?? 0));
    }
    const int HistoryLimit = 128, HistoryNoteLimit = 2_000_000;
    readonly LinkedList<Edit> _undo = new();
    readonly Stack<Edit> _redo = new();
    readonly Dictionary<Guid, HashSet<Guid>> _selection = new();
    Dictionary<Guid, ProjectNote> _notes = new();
    MidiConversion? _indexedConversion;
    HashSet<Guid> _sourceIds = new();
    SmapProject _project;
    Guid _state = Guid.NewGuid(), _savedState;
    Guid _active;
    static ProjectNote[] _clipboard = Array.Empty<ProjectNote>();

    public SmapProject Project => _project;
    public ProjectTrack ActiveTrack => _project.Tracks.Single(t => t.Id == _active);
    public Guid ActiveTrackId => _active;
    public HashSet<Guid> Selected => _selection.TryGetValue(_active, out var ids) ? ids : _selection[_active] = new();
    public bool IsDirty => _state != _savedState;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int HistoryNoteVersions => _undo.Sum(e => e.Weight) + _redo.Sum(e => e.Weight);
    public int Revision { get; private set; }
    public event Action? Changed;
    public event Action? SelectionChanged;
    public bool AllowUnsupportedEditing { get; set; }
    public int KeyCount => AllowUnsupportedEditing || _project.FormatVersion >= 2 ? 15 : TrackKeyCount(ActiveTrack);
    public static int TrackKeyCount(ProjectTrack track)
    {
        if (track.GameTarget is not { } p) return 15;
        if (p.KeyMidiPitches.Count > 0) return p.KeyMidiPitches.Count;
        return GamePitchProfiles.Instruments.Contains(p.InstrumentId, StringComparer.Ordinal) ? GamePitchProfiles.Get(p.InstrumentId).KeyCount : 15;
    }

    public EditorSession(SmapProject project)
    {
        ProjectValidation.Validate(project);
        _project = project with { Tracks = project.Tracks.Select(t => t with { Notes = Ordered(t.Notes) }).ToList() };
        _active = _project.Tracks[0].Id; Reindex(); _savedState = _state;
    }
    public static SmapProject NewProject() => new() { Tracks = new() { new() { Name = "", PreviewInstrument = "Piano" } } };
    public void MarkSaved() => _savedState = _state;
    public ProjectNote? Find(Guid id) => _notes.GetValueOrDefault(id);
    public ProjectNote[] SelectedNotes() => Selected.Select(Find).OfType<ProjectNote>().ToArray();
    public void SelectTrack(Guid id)
    {
        if (!_project.Tracks.Any(t => t.Id == id)) throw new ArgumentException("TrackNotFound", nameof(id));
        _active = id; SelectionChanged?.Invoke();
    }
    public void Select(IEnumerable<Guid> ids, bool additive = false)
    {
        if (!additive) Selected.Clear();
        foreach (Guid id in ids) if (_notes.TryGetValue(id, out var n) && n.TrackId == _active) Selected.Add(id);
        SelectionChanged?.Invoke();
    }
    public void ToggleSelection(Guid id)
    {
        if (Find(id)?.TrackId != _active) return;
        if (!Selected.Remove(id)) Selected.Add(id); SelectionChanged?.Invoke();
    }
    void Reindex()
    {
        _notes = _project.Tracks.SelectMany(t => t.Notes).ToDictionary(n => n.Id);
        RefreshSources();
        if (!_project.Tracks.Any(t => t.Id == _active)) _active = _project.Tracks[0].Id;
        foreach (var pair in _selection) pair.Value.RemoveWhere(id => !_notes.TryGetValue(id, out var n) || n.TrackId != pair.Key);
    }
    void RefreshSources()
    {
        if (_indexedConversion == _project.MidiConversion) return;
        _indexedConversion = _project.MidiConversion; _sourceIds = _indexedConversion?.Source.Notes.Select(n => n.Id).ToHashSet() ?? new();
    }
    static ProjectTrack Metadata(ProjectTrack t) => t with { Notes = new() };
    static List<ProjectNote> Ordered(IEnumerable<ProjectNote> notes) => notes.OrderBy(n => n.Beat).ThenBy(n => n.Key).ThenBy(n => n.Id).ToList();
    static SmapProject Header(SmapProject p) => p with { Tracks = new() };
    static bool SameTrack(ProjectTrack a, ProjectTrack b) => a.Id == b.Id && a.Name == b.Name && a.Muted == b.Muted
        && a.Solo == b.Solo && a.PreviewInstrument == b.PreviewInstrument && a.PreviewSemitoneOffset == b.PreviewSemitoneOffset
        && a.GameTarget == b.GameTarget && a.LegacyFields == b.LegacyFields && a.Capability == b.Capability && a.KeyDomain == b.KeyDomain;
    public bool ReplaceProject(SmapProject candidate)
    {
        ProjectValidation.Validate(candidate);
        if (candidate.ProjectId != _project.ProjectId) throw new InvalidDataException("ProjectIdentityChanged");
        var before = _project.Tracks.ToDictionary(t => t.Id); var after = candidate.Tracks.ToDictionary(t => t.Id);
        var notes = new List<NoteDelta>(); var tracks = new List<TrackDelta>();
        foreach (Guid id in before.Keys.Union(after.Keys))
        {
            before.TryGetValue(id, out var a); after.TryGetValue(id, out var b);
            if (a == null || b == null)
            {
                tracks.Add(new(a == null ? candidate.Tracks.IndexOf(b!) : _project.Tracks.IndexOf(a), a, b)); continue;
            }
            if (!SameTrack(a, b)) tracks.Add(new(_project.Tracks.IndexOf(a), Metadata(a), Metadata(b)));
            var old = a.Notes.ToDictionary(n => n.Id); var next = b.Notes.ToDictionary(n => n.Id);
            foreach (Guid note in old.Keys.Union(next.Keys))
            {
                var x = old.GetValueOrDefault(note); var y = next.GetValueOrDefault(note);
                if (x != y) notes.Add(new(id, x, y));
            }
        }
        var from = Header(_project); var to = Header(candidate);
        bool headerChanged = from.FormatVersion != to.FormatVersion || from.Metadata != to.Metadata || !from.Tempos.SequenceEqual(to.Tempos) || !from.Meters.SequenceEqual(to.Meters)
            || from.MidiConversion != to.MidiConversion || from.MidiTicksPerQuarterNote != to.MidiTicksPerQuarterNote;
        if (notes.Count == 0 && tracks.Count == 0 && !headerChanged) return false;
        var edit = new Edit(from, to, notes.ToArray(), tracks.ToArray(), _state, Guid.NewGuid());
        if (edit.Weight > HistoryNoteLimit) throw new InvalidOperationException("HistoryCapacityExceeded");
        Commit(edit); return true;
    }
    void Commit(Edit edit)
    {
        _redo.Clear(); _undo.AddLast(edit);
        int weight = _undo.Sum(e => e.Weight);
        while (_undo.Count > HistoryLimit || (weight > HistoryNoteLimit && _undo.Count > 1))
        { weight -= _undo.First!.Value.Weight; _undo.RemoveFirst(); }
        Apply(edit, true);
    }
    void Apply(Edit edit, bool forward)
    {
        var tracks = _project.Tracks.ToList();
        foreach (var delta in edit.Tracks)
        {
            var from = forward ? delta.Before : delta.After; var to = forward ? delta.After : delta.Before;
            if (from != null && to == null) tracks.RemoveAll(t => t.Id == from.Id);
            else if (from == null && to != null) { tracks.Insert(Math.Min(delta.Index, tracks.Count), to with { Notes = Ordered(to.Notes) }); if (forward) _active = to.Id; }
            else if (to != null)
            {
                int index = tracks.FindIndex(t => t.Id == to.Id);
                tracks[index] = to with { Notes = tracks[index].Notes };
            }
        }
        foreach (var group in edit.Notes.GroupBy(d => d.Track))
        {
            int index = tracks.FindIndex(t => t.Id == group.Key); if (index < 0) continue;
            var replacements = group.ToDictionary(d => (d.Before ?? d.After)!.Id, d => forward ? d.After : d.Before);
            var values = tracks[index].Notes.Where(n => !replacements.ContainsKey(n.Id)).ToList();
            var added = Ordered(replacements.Values.OfType<ProjectNote>());
            if (added.Count < 32)
            {
                foreach (var n in added) { int low = 0, high = values.Count; while (low < high) { int mid = (low + high) / 2; if (Compare(values[mid], n) < 0) low = mid + 1; else high = mid; } values.Insert(low, n); }
            }
            else
            {
                var merged = new List<ProjectNote>(values.Count + added.Count); int a = 0, b = 0;
                while (a < values.Count && b < added.Count) merged.Add(Compare(values[a], added[b]) <= 0 ? values[a++] : added[b++]);
                merged.AddRange(values.Skip(a)); merged.AddRange(added.Skip(b)); values = merged;
            }
            tracks[index] = tracks[index] with { Notes = values };
        }
        foreach (var delta in edit.Tracks)
        {
            var from = forward ? delta.Before : delta.After; var to = forward ? delta.After : delta.Before;
            if (from != null && to == null) foreach (var n in from.Notes) _notes.Remove(n.Id);
        }
        foreach (var delta in edit.Notes) if ((forward ? delta.Before : delta.After) is { } n) _notes.Remove(n.Id);
        foreach (var delta in edit.Tracks)
        {
            var from = forward ? delta.Before : delta.After; var to = forward ? delta.After : delta.Before;
            if (from == null && to != null) foreach (var n in to.Notes) _notes[n.Id] = n;
        }
        foreach (var delta in edit.Notes) if ((forward ? delta.After : delta.Before) is { } n) _notes[n.Id] = n;
        _project = (forward ? edit.After : edit.Before) with { Tracks = tracks, GridSubdiv = _project.GridSubdiv };
        _state = forward ? edit.AfterState : edit.BeforeState; Revision++; RefreshSources();
        if (!_project.Tracks.Any(t => t.Id == _active)) _active = _project.Tracks[0].Id;
        foreach (var pair in _selection) pair.Value.RemoveWhere(id => !_notes.TryGetValue(id, out var n) || n.TrackId != pair.Key);
        if (edit.Notes.Length > 0)
        {
            Selected.Clear();
            foreach (var d in edit.Notes.Where(n => n.Track == _active)) if ((forward ? d.After : d.Before) is { } note) Selected.Add(note.Id);
        }
        Changed?.Invoke();
    }
    static int Compare(ProjectNote a, ProjectNote b)
    {
        int c = a.Beat.CompareTo(b.Beat); if (c != 0) return c; c = a.Key.CompareTo(b.Key); return c != 0 ? c : a.Id.CompareTo(b.Id);
    }
    public void Undo()
    {
        if (_undo.Last == null) return;
        var edit = _undo.Last.Value; _undo.RemoveLast(); _redo.Push(edit); Apply(edit, false);
    }
    public void Redo()
    {
        if (!_redo.TryPop(out var edit)) return;
        _undo.AddLast(edit); Apply(edit, true);
    }
    public bool PutNotes(IEnumerable<ProjectNote> notes, IEnumerable<Guid>? remove = null)
    {
        var put = notes.ToArray(); var deleted = (remove ?? Array.Empty<Guid>()).Concat(put.Select(n => n.Id)).ToHashSet();
        if (put.Any(n => !_project.Tracks.Any(t => t.Id == n.TrackId))) throw new ArgumentException("TrackNotFound", nameof(notes));
        if (put.Any(n => Find(n.Id) is { } old && old.TrackId != n.TrackId)) throw new ArgumentException("TrackMismatch", nameof(notes));
        if (put.Any(n => n.Key < 0 || n.Key >= (AllowUnsupportedEditing || _project.FormatVersion >= 2 ? 15 : TrackKeyCount(_project.Tracks.Single(t => t.Id == n.TrackId))))) throw new ArgumentException("ClipboardKeyRange", nameof(notes));
        if (put.Select(n => n.Id).Distinct().Count() != put.Length) throw new ArgumentException("DuplicateNoteId", nameof(notes));
        int count = _notes.Count - deleted.Count(id => _notes.ContainsKey(id)) + put.Length;
        if (count > ProjectValidation.MaxNotes) throw new InvalidOperationException("NoteCapacityExceeded");
        if (put.Any(n => n.SourceMidiNoteId is { } id && !_sourceIds.Contains(id))) throw new ArgumentException("MidiSourceChanged", nameof(notes));
        if (put.Length > 0)
        {
            var ids = put.Select(n => n.TrackId).ToHashSet();
            ProjectValidation.Validate(_project with
            {
                MidiConversion = null,
                FormatVersion = AllowUnsupportedEditing ? 2 : _project.FormatVersion,
                KeyInterpretationVersion = AllowUnsupportedEditing ? 1 : _project.KeyInterpretationVersion,
                Tracks = _project.Tracks.Where(t => ids.Contains(t.Id))
                .Select(t => t with { Notes = put.Where(n => n.TrackId == t.Id).ToList() }).ToList()
            });
        }
        var deltas = new List<NoteDelta>();
        foreach (var n in put) { var old = Find(n.Id); if (old != n) deltas.Add(new(n.TrackId, old, n)); }
        var retained = put.Select(n => n.Id).ToHashSet();
        foreach (var id in deleted.Where(id => !retained.Contains(id))) if (Find(id) is { } old) deltas.Add(new(old.TrackId, old, null));
        if (deltas.Count == 0) return false;
        var header = Header(_project); var edit = new Edit(header, header with { FormatVersion = AllowUnsupportedEditing ? 2 : header.FormatVersion,
            KeyInterpretationVersion = AllowUnsupportedEditing ? 1 : header.KeyInterpretationVersion }, deltas.ToArray(), Array.Empty<TrackDelta>(), _state, Guid.NewGuid());
        if (edit.Weight > HistoryNoteLimit) throw new InvalidOperationException("HistoryCapacityExceeded");
        Commit(edit); return true;
    }
    public ProjectNote Create(int key, decimal beat)
    {
        if (key < 0 || key >= KeyCount) throw new ArgumentOutOfRangeException(nameof(key));
        var n = new ProjectNote { TrackId = _active, Key = key, Beat = beat, Action = NoteAction.Tap };
        PutNotes(new[] { n }); Select(new[] { n.Id }); return n;
    }
    public void DeleteSelected() => PutNotes(Array.Empty<ProjectNote>(), Selected.ToArray());
    public void Copy()
    {
        var notes = SelectedNotes(); if (notes.Length == 0) return;
        decimal start = notes.Min(n => n.Beat); _clipboard = notes.Select(n => n with { Beat = n.Beat - start }).ToArray();
    }
    public void Cut() { Copy(); DeleteSelected(); }
    public void Paste(decimal beat)
    {
        var notes = _clipboard.Select(n => n with { Id = Guid.NewGuid(), TrackId = _active, Beat = beat + n.Beat, SourceMidiNoteId = null }).ToArray();
        if (notes.Any(n => n.Key >= KeyCount)) throw new InvalidOperationException("ClipboardKeyRange");
        if (notes.Length > 0) { PutNotes(notes); Select(notes.Select(n => n.Id)); }
    }
    public ProjectNote[] MovePreview(IReadOnlyList<ProjectNote> originals, decimal delta, int keys, bool copy = false)
    {
        if (originals.Count == 0) return Array.Empty<ProjectNote>();
        delta = Math.Max(-originals.Min(n => n.Beat), delta);
        keys = Math.Clamp(keys, -originals.Min(n => n.Key), KeyCount - 1 - originals.Max(n => n.Key));
        return originals.Select(n => n with
        {
            Id = copy ? Guid.NewGuid() : n.Id,
            Beat = n.Beat + delta,
            Key = n.Key + keys,
            SourceMidiNoteId = copy ? null : n.SourceMidiNoteId
        }).ToArray();
    }
    public void MoveSelected(decimal delta, int keys, bool copy = false)
    {
        var notes = MovePreview(SelectedNotes(), delta, keys, copy); if (PutNotes(notes)) Select(notes.Select(n => n.Id));
    }
    public ProjectNote[] ResizePreview(IReadOnlyList<ProjectNote> originals, decimal delta, bool free = false)
    {
        if (originals.Count == 0) return Array.Empty<ProjectNote>();
        if (originals.Select(n => n.Action).Distinct().Count() > 1) throw new InvalidOperationException("MixedNoteActions");
        decimal cell = 1m / _project.GridSubdiv;
        delta = Math.Max(delta, -originals.Min(n => n.Action == NoteAction.Tap ? cell : n.LengthBeats) + 0.000000000001m);
        return originals.Select(n =>
        {
            decimal length = (n.Action == NoteAction.Tap ? cell : n.LengthBeats) + delta;
            bool tap = !free && ((n.Action == NoteAction.Tap && length <= cell)
                || (originals.Count == 1 && n.LengthBeats > cell && delta < 0 && length <= cell));
            return n with
            {
                Action = tap ? NoteAction.Tap : NoteAction.Hold,
                LengthBeats = tap ? 0 : length,
                RetriggerOnOverlap = !tap && n.RetriggerOnOverlap
            };
        }).ToArray();
    }
    public void SetSelectedLength(NoteAction action, decimal length)
    {
        PutNotes(SelectedNotes().Select(n => n with
        {
            Action = action,
            LengthBeats = action == NoteAction.Tap ? 0 : length,
            RetriggerOnOverlap = action == NoteAction.Hold && n.RetriggerOnOverlap
        }));
    }
    public void QuantizeSelected()
    {
        decimal Snap(decimal b) => decimal.Round(b * _project.GridSubdiv, 0, MidpointRounding.AwayFromZero) / _project.GridSubdiv;
        PutNotes(SelectedNotes().Select(n => n with
        {
            Beat = Snap(n.Beat),
            LengthBeats = n.Action == NoteAction.Tap ? 0
            : Math.Max(1m / _project.GridSubdiv, Snap(n.Beat + n.LengthBeats) - Snap(n.Beat))
        }));
    }
    public void SplitSelected(decimal beat)
    {
        var result = new List<ProjectNote>();
        foreach (var n in SelectedNotes().Where(n => n.Action == NoteAction.Hold && n.Beat < beat && n.Beat + n.LengthBeats > beat))
        {
            result.Add(n with { LengthBeats = beat - n.Beat });
            result.Add(n with { Id = Guid.NewGuid(), Beat = beat, LengthBeats = n.Beat + n.LengthBeats - beat, RetriggerOnOverlap = true });
        }
        if (result.Count > 0) { PutNotes(result); Select(result.Select(n => n.Id)); }
    }
    public Guid AddTrack(string name)
    {
        var track = new ProjectTrack { Name = name }; ReplaceProject(_project with { Tracks = _project.Tracks.Append(track).ToList() });
        SelectTrack(track.Id); return track.Id;
    }
    public void DeleteTrack(Guid id)
    {
        if (_project.Tracks.Count == 1) throw new InvalidOperationException("LastTrack");
        ReplaceProject(_project with { Tracks = _project.Tracks.Where(t => t.Id != id).ToList() });
    }
    public void UpdateTrack(ProjectTrack track) => ReplaceProject(_project with { FormatVersion = AllowUnsupportedEditing || track.Capability != null ? 2 : _project.FormatVersion,
        KeyInterpretationVersion = AllowUnsupportedEditing || track.Capability != null ? 1 : _project.KeyInterpretationVersion,
        Tracks = _project.Tracks.Select(t => t.Id == track.Id ? track with { Notes = t.Notes } : t).ToList() });
    public void SetGrid(int subdivisions)
    {
        if (subdivisions < 1) throw new ArgumentOutOfRangeException(nameof(subdivisions));
        _project = _project with { GridSubdiv = subdivisions }; Changed?.Invoke();
    }
}
