namespace SMAP_WPF.Core;

public enum PracticeMode { Follow, Rhythm }
public enum PracticeNoteStatus { Pending, Holding, Completed, EarlyRelease, LateRelease, Missed, Skipped }
public enum PracticeJudgmentKind { Started, Completed, EarlyRelease, LateRelease, Missed, RepeatedPress, UnexpectedKey, Rearticulated }
public sealed record PracticeJudgment(PracticeJudgmentKind Kind, int Key, IReadOnlyList<Guid> Notes);
public sealed record PracticeStep(decimal StartMicroseconds, IReadOnlyList<TimedProjectNote> Notes);

public sealed class PracticeSession
{
    public const double OnsetToleranceMs = 120;
    public const double ReleaseToleranceMs = 80;
    public const double RetriggerGapMs = 50;
    sealed class Active(TimedProjectNote note, int group, decimal elapsed = 0)
    {
        public readonly TimedProjectNote Note = note;
        public readonly int Group = group;
        public decimal Elapsed = elapsed;
        public bool AwaitingPress;
    }
    readonly IMonotonicClock _clock;
    readonly Dictionary<Guid, Guid[]> _members = new();
    readonly Dictionary<Guid, PracticeNoteStatus> _statuses = new();
    readonly Dictionary<Guid, Active> _active = new();
    readonly HashSet<int> _held = new();
    readonly Dictionary<int, HashSet<int>> _fresh = new();
    readonly Dictionary<int, double> _gaps = new();
    decimal _lastClock;
    double _speed = 1;
    decimal _position;
    int _deadlineCursor;
    readonly decimal _lastEnd;
    public PracticeMode Mode { get; }
    public IReadOnlyList<PracticeStep> Steps { get; }
    public int CurrentStepIndex { get; private set; }
    public PracticeJudgment? LastJudgment { get; private set; }
    public event Action<PracticeJudgment>? Judged;
    public bool Paused { get; private set; }
    public double PositionMs => (double)(_position / 1000);
    public IEnumerable<int> HoldingKeys => _active.Values.Where(a => !a.AwaitingPress).Select(a => a.Note.Key).Distinct();
    public IReadOnlyDictionary<Guid, PracticeNoteStatus> Statuses => _statuses;
    public bool IsCompleted => CurrentStepIndex >= Steps.Count && _active.Count == 0;
    public PracticeHintSnapshot Hints()
    {
        IReadOnlyList<int> Keys(int index) => index < Steps.Count ? Steps[index].Notes.Where(n => _statuses[n.NoteId] == PracticeNoteStatus.Pending)
            .Select(n => n.Key).Distinct().ToArray() : Array.Empty<int>();
        return new(Keys(CurrentStepIndex), Keys(CurrentStepIndex + 1), _active.Values.Select(a => new HoldHint(a.Note.Key,
            (double)Math.Max(0, a.Note.LengthMicroseconds-a.Elapsed)/1000,
            a.Elapsed >= a.Note.LengthMicroseconds-ReleaseWindow(a.Note), a.AwaitingPress,(double)a.Note.LengthMicroseconds/1000)).ToArray(), LastJudgment, Paused, IsCompleted);
    }
    public double Speed
    {
        get => _speed;
        set
        {
            if (!double.IsFinite(value) || value <= 0 || value > 100) throw new ArgumentOutOfRangeException(nameof(value));
            Tick(); _speed = value;
        }
    }

    public PracticeSession(PlaybackSequence sequence, IMonotonicClock clock, PracticeMode mode = PracticeMode.Follow,
        double groupingToleranceMs = 0)
    {
        if (!Enum.IsDefined(mode) || !double.IsFinite(groupingToleranceMs) || groupingToleranceMs < 0)
            throw new ArgumentOutOfRangeException(nameof(mode));
        _clock = clock; Mode = mode; _lastClock = (decimal)clock.Milliseconds;
        var normalized = new List<TimedProjectNote>();
        foreach (var keyGroup in sequence.Notes.GroupBy(n => n.Key))
        {
            var regions = new List<(TimedProjectNote note, List<Guid> members)>();
            var active = new SortedSet<(decimal end, int index)>();
            foreach (var note in keyGroup.Where(n => n.Action == NoteAction.Hold))
            {
                while (active.Count > 0 && active.Min.end <= note.StartMicroseconds) active.Remove(active.Min);
                if (!note.RetriggerOnOverlap && active.Count > 0)
                {
                    var last = active.Max; var region = regions[last.index]; active.Remove(last);
                    region.note = region.note with { LengthMicroseconds = Math.Max(last.end, note.StartMicroseconds + note.LengthMicroseconds) - region.note.StartMicroseconds };
                    region.members.Add(note.NoteId); regions[last.index] = region;
                    active.Add((region.note.StartMicroseconds + region.note.LengthMicroseconds, last.index));
                }
                else { regions.Add((note, new() { note.NoteId })); active.Add((note.StartMicroseconds + note.LengthMicroseconds, regions.Count - 1)); }
            }
            foreach (var region in regions) { normalized.Add(region.note); _members[region.note.NoteId] = region.members.ToArray(); }
            foreach (var tap in keyGroup.Where(n => n.Action == NoteAction.Tap))
            { normalized.Add(tap); _members[tap.NoteId] = new[] { tap.NoteId }; }
        }
        var groups = new List<PracticeStep>(); var current = new List<TimedProjectNote>(); decimal start = 0;
        foreach (var note in normalized.OrderBy(n => n.StartMicroseconds).ThenBy(n => n.Key))
        {
            if (current.Count > 0 && (note.StartMicroseconds - start > (decimal)groupingToleranceMs * 1000
                || (note.StartMicroseconds != start && current.Any(n => n.Key == note.Key))))
            { groups.Add(new(start, current.ToArray())); current.Clear(); }
            if (current.Count == 0) start = note.StartMicroseconds;
            current.Add(note);
        }
        if (current.Count > 0) groups.Add(new(start, current.ToArray()));
        Steps = groups.AsReadOnly();
        _lastEnd = normalized.Select(n => n.StartMicroseconds + n.LengthMicroseconds).DefaultIfEmpty().Max();
        foreach (var note in sequence.Notes) _statuses[note.NoteId] = PracticeNoteStatus.Pending;
        if (Steps.Count > 0 && mode == PracticeMode.Follow) _position = Steps[0].StartMicroseconds;
    }

    public void Tick()
    {
        double raw = _clock.Milliseconds;
        if (!double.IsFinite(raw)) throw new InvalidOperationException("练习时钟必须单调递增。");
        decimal now = (decimal)raw;
        if (now < _lastClock) throw new InvalidOperationException("练习时钟必须单调递增。");
        decimal elapsed = (now - _lastClock) * 1000 * (decimal)_speed;
        _lastClock = now;
        if (Paused) return;
        _position += elapsed;
        foreach (var hold in _active.Values)
        {
            if (Mode == PracticeMode.Rhythm) hold.Elapsed = _position - hold.Note.StartMicroseconds;
            else if (!hold.AwaitingPress) hold.Elapsed += elapsed;
        }
        var remaining = _active.Values.Where(a => !a.AwaitingPress).GroupBy(a => a.Note.Key)
            .ToDictionary(g => g.Key, g => g.Max(a => a.Note.LengthMicroseconds - a.Elapsed));
        foreach (var pair in _active.ToArray())
        {
            var hold = pair.Value;
            if (!hold.AwaitingPress && _held.Contains(hold.Note.Key) && hold.Elapsed >= hold.Note.LengthMicroseconds
                && remaining.GetValueOrDefault(hold.Note.Key) > 0)
            {
                SetStatus(hold.Note, PracticeNoteStatus.Completed); _active.Remove(pair.Key); Judge(PracticeJudgmentKind.Completed, hold.Note); continue;
            }
            if (_gaps.TryGetValue(hold.Note.Key, out double deadline) && now > (decimal)deadline)
            { Fail(hold, PracticeNoteStatus.EarlyRelease, PracticeJudgmentKind.EarlyRelease); if (Mode == PracticeMode.Follow) return; }
            else if (hold.Elapsed > hold.Note.LengthMicroseconds + ReleaseWindow(hold.Note))
            { Fail(hold, PracticeNoteStatus.LateRelease, hold.AwaitingPress ? PracticeJudgmentKind.Missed : PracticeJudgmentKind.LateRelease); if (Mode == PracticeMode.Follow) return; }
        }
        if (Mode != PracticeMode.Rhythm) return;
        while (_deadlineCursor < Steps.Count && Steps[_deadlineCursor].StartMicroseconds + (decimal)OnsetToleranceMs * 1000 < _position)
        {
            foreach (var note in Steps[_deadlineCursor].Notes.Where(n => _statuses[n.NoteId] == PracticeNoteStatus.Pending))
            { SetStatus(note, PracticeNoteStatus.Missed); Judge(PracticeJudgmentKind.Missed, note); }
            _fresh.Remove(_deadlineCursor++);
        }
        while (CurrentStepIndex + 1 < Steps.Count && Steps[CurrentStepIndex + 1].StartMicroseconds <= _position) CurrentStepIndex++;
        if (_position > _lastEnd + (decimal)OnsetToleranceMs * 1000) CurrentStepIndex = Steps.Count;
    }

    public void Press(int key)
    {
        Tick(); if (Paused) return;
        if (!_held.Add(key)) { Judge(PracticeJudgmentKind.RepeatedPress, key); return; }
        if (_gaps.Remove(key)) Judge(PracticeJudgmentKind.Rearticulated, key);
        var waiting = _active.Values.Where(a => a.Note.Key == key && a.AwaitingPress).ToArray();
        foreach (var hold in waiting) { hold.AwaitingPress = false; Judge(PracticeJudgmentKind.Started, hold.Note); }
        int group = Mode == PracticeMode.Follow ? CurrentStepIndex : FindInputGroup(key);
        if (group >= Steps.Count || group < 0 || !Steps[group].Notes.Any(n => n.Key == key))
        { if (waiting.Length == 0) Judge(PracticeJudgmentKind.UnexpectedKey, key); return; }
        if (!_fresh.TryGetValue(group, out var fresh)) _fresh[group] = fresh = new();
        fresh.Add(key);
        if (Steps[group].Notes.Any(n => !fresh.Contains(n.Key) || !_held.Contains(n.Key))) return;
        foreach (var note in Steps[group].Notes)
        {
            if (_statuses[note.NoteId] != PracticeNoteStatus.Pending) continue;
            if (note.Action == NoteAction.Tap) { SetStatus(note, PracticeNoteStatus.Completed); Judge(PracticeJudgmentKind.Completed, note); }
            else
            {
                _active.Add(note.NoteId, new(note, group)); SetStatus(note, PracticeNoteStatus.Holding);
                Judge(PracticeJudgmentKind.Started, note);
            }
        }
        _fresh.Remove(group);
        if (Mode == PracticeMode.Follow) { _position = Steps[group].StartMicroseconds; CurrentStepIndex++; }
        else FinishRhythmInput();
    }

    int FindInputGroup(int key)
    {
        int lo = 0, hi = Steps.Count;
        decimal first = _position - (decimal)OnsetToleranceMs * 1000;
        while (lo < hi) { int mid = lo + (hi - lo) / 2; if (Steps[mid].StartMicroseconds < first) lo = mid + 1; else hi = mid; }
        for (int i = lo; i < Steps.Count && Steps[i].StartMicroseconds <= _position + (decimal)OnsetToleranceMs * 1000; i++)
            if (Steps[i].Notes.Any(n => n.Key == key && _statuses[n.NoteId] == PracticeNoteStatus.Pending)) return i;
        return -1;
    }

    public void Release(int key)
    {
        Tick(); _held.Remove(key);
        foreach (var fresh in _fresh.Values) fresh.Remove(key);
        if (Paused) return;
        foreach (var hold in _active.Values.Where(a => a.Note.Key == key).ToArray())
        {
            decimal remaining = hold.Note.LengthMicroseconds - hold.Elapsed;
            if (remaining > ReleaseWindow(hold.Note))
            {
                int next = Mode == PracticeMode.Follow ? CurrentStepIndex : FindInputGroup(key);
                if (next >= 0 && next < Steps.Count && Steps[next].Notes.Any(n => n.Key == key && (n.Action == NoteAction.Tap || n.RetriggerOnOverlap)))
                    _gaps[key] = _clock.Milliseconds + RetriggerGapMs;
                else { Fail(hold, PracticeNoteStatus.EarlyRelease, PracticeJudgmentKind.EarlyRelease); if (Mode == PracticeMode.Follow) return; }
            }
            else
            {
                SetStatus(hold.Note, PracticeNoteStatus.Completed); _active.Remove(hold.Note.NoteId);
                Judge(PracticeJudgmentKind.Completed, hold.Note);
            }
        }
        FinishRhythmInput();
    }

    void FinishRhythmInput()
    {
        if (Mode == PracticeMode.Rhythm && _active.Count == 0 && _position >= _lastEnd && _statuses.Values.All(s => s != PracticeNoteStatus.Pending))
            CurrentStepIndex = Steps.Count;
    }

    void Fail(Active hold, PracticeNoteStatus status, PracticeJudgmentKind judgment)
    {
        SetStatus(hold.Note, status); _active.Remove(hold.Note.NoteId); _gaps.Remove(hold.Note.Key);
        Judge(judgment, hold.Note);
        if (Mode == PracticeMode.Follow)
        {
            int step = Math.Min(hold.Group, _active.Values.Select(a => a.Group).DefaultIfEmpty(hold.Group).Min());
            _active.Clear(); _held.Clear(); _gaps.Clear(); _fresh.Clear(); CurrentStepIndex = step;
            for (int i = step; i < Steps.Count; i++) foreach (var note in Steps[i].Notes) SetStatus(note, PracticeNoteStatus.Pending);
            _position = Steps[step].StartMicroseconds;
        }
    }

    public void Pause(bool paused)
    {
        Tick(); Paused = paused; _held.Clear(); _fresh.Clear(); _gaps.Clear();
        foreach (var hold in _active.Values) hold.AwaitingPress = true;
    }

    public void Seek(double ms)
    {
        if (!double.IsFinite(ms) || ms < 0) throw new ArgumentOutOfRangeException(nameof(ms));
        _lastClock = (decimal)_clock.Milliseconds; _position = (decimal)ms * 1000;
        _active.Clear(); _held.Clear(); _fresh.Clear(); _gaps.Clear(); LastJudgment = null;
        CurrentStepIndex = 0;
        for (int i = 0; i < Steps.Count; i++)
        {
            if (Steps[i].StartMicroseconds < _position) CurrentStepIndex = i + 1;
            foreach (var note in Steps[i].Notes)
            {
                SetStatus(note, note.StartMicroseconds < _position ? PracticeNoteStatus.Skipped : PracticeNoteStatus.Pending);
                if (note.Action == NoteAction.Hold && note.StartMicroseconds < _position && note.StartMicroseconds + note.LengthMicroseconds > _position)
                {
                    _active[note.NoteId] = new(note, i, _position - note.StartMicroseconds) { AwaitingPress = true };
                    SetStatus(note, PracticeNoteStatus.Holding);
                }
            }
        }
        _deadlineCursor = CurrentStepIndex;
    }

    void SetStatus(TimedProjectNote note, PracticeNoteStatus status)
    { foreach (var id in _members[note.NoteId]) _statuses[id] = status; }
    static decimal ReleaseWindow(TimedProjectNote note) => Math.Min((decimal)ReleaseToleranceMs * 1000, note.LengthMicroseconds / 5);
    void Judge(PracticeJudgmentKind kind, TimedProjectNote note)
    { LastJudgment = new(kind, note.Key, _members[note.NoteId]); Judged?.Invoke(LastJudgment); }
    void Judge(PracticeJudgmentKind kind, int key)
    { LastJudgment = new(kind, key, Array.Empty<Guid>()); Judged?.Invoke(LastJudgment); }
}
