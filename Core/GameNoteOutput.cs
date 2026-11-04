namespace SMAP_WPF.Core;

public readonly record struct KeyTransition(ushort Key, bool IsDown);

public interface IKeyInput
{
    void Send(IReadOnlyList<KeyTransition> changes);
}

public sealed class GameNoteOutput : INoteOutput
{
    readonly IKeyInput _input;
    readonly ushort[] _keys;
    readonly Func<TimedProjectNote, ushort>? _resolve;
    ushort Key(TimedProjectNote n) => _resolve?.Invoke(n) ?? _keys[n.Key];
    readonly Dictionary<Guid, TimedProjectNote> _owners = new();
    readonly HashSet<ushort> _pressed = new();
    public IReadOnlyCollection<ushort> PendingKeys => _pressed.ToArray();

    public GameNoteOutput(IKeyInput input, IEnumerable<ushort> keys)
    {
        _input = input; _keys = keys.ToArray();
        if (_keys.Length != 15 || _keys.Any(k => k == 0)) throw new ArgumentException("需要十五个有效键位。", nameof(keys));
    }
    public GameNoteOutput(IKeyInput input, Func<TimedProjectNote, ushort> resolver)
    { _input=input;_keys=Array.Empty<ushort>();_resolve=resolver; }

    public void Apply(IReadOnlyList<NoteOutputEvent> changes)
    {
        foreach (var change in changes.Where(c => !c.IsOn)) _owners.Remove(change.Note.NoteId);
        var remaining = _owners.Values.Select(Key).ToHashSet();
        var transitions = new List<KeyTransition>();
        var presses = new List<KeyTransition>();
        var released = new HashSet<ushort>();
        foreach (ushort key in _pressed.Order())
            if (!remaining.Contains(key)) { transitions.Add(new(key, false)); released.Add(key); }
        foreach (var group in changes.Where(c => c.IsOn).GroupBy(c => Key(c.Note)).OrderBy(g => g.Key))
        {
            bool held = remaining.Contains(group.Key);
            bool merge = held && group.All(c => c.Note.Action == NoteAction.Hold)
                && !group.Any(c => c.Note.RetriggerOnOverlap && !c.IsResume)
                && _owners.Values.Any(n => Key(n) == group.Key && n.Action == NoteAction.Hold);
            if (!merge)
            {
                if (held) { transitions.Add(new(group.Key, false)); released.Add(group.Key); }
                presses.Add(new(group.Key, true));
            }
            foreach (var change in group) _owners.Add(change.Note.NoteId, change.Note);
        }
        // 即使系统只接受了部分批次，也将所有可能已按下的键纳入清理集合。
        transitions.AddRange(presses);
        var down = presses.Select(c => c.Key).ToHashSet();
        _pressed.UnionWith(down);
        if (transitions.Count > 0) _input.Send(transitions);
        _pressed.ExceptWith(released.Except(down));
    }

    public void StopAll()
    {
        _owners.Clear(); var failures = new List<Exception>();
        foreach (ushort key in _pressed.Order().ToArray())
        {
            Exception? failure = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { _input.Send(new[] { new KeyTransition(key, false) }); _pressed.Remove(key); failure = null; break; }
                catch (Exception error) { failure = error; }
            }
            if (failure != null) failures.Add(failure);
        }
        if (failures.Count > 0) throw new AggregateException("存在未确认释放的游戏按键。", failures);
    }
}
