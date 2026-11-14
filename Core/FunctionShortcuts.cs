namespace SMAP_WPF.Core;

[Flags] public enum ShortcutModifiers { None = 0, Alt = 1, Ctrl = 2, Shift = 4 }
public enum ShortcutScope { Global, GameForeground, AppForeground }
public enum ShortcutAction { PlayStop, PauseResume, Previous, Next, Slower, Faster, SeekBack, SeekForward,
    TogglePreview, TogglePractice, ToggleMapping, PauseMapping, ToggleWindow }
public readonly record struct ShortcutChord(ushort Key, ShortcutModifiers Modifiers = 0);
public sealed record FunctionShortcut(ShortcutAction Action, ShortcutChord? Chord, ShortcutScope Scope);
public sealed record ShortcutContext(bool GameForeground, bool AppForeground, bool TextInput, bool Mapping);
public interface IShortcutRegistrar
{
    bool Register(int id, ShortcutChord chord);
    bool Unregister(int id);
}

public static class ShortcutRules
{
    public static List<FunctionShortcut> Defaults() => Enum.GetValues<ShortcutAction>().Select(a => new FunctionShortcut(a,
        a switch {
            ShortcutAction.PlayStop => new(0x70), ShortcutAction.PauseResume => new(0x71),
            ShortcutAction.Slower => new(0x72), ShortcutAction.Faster => new(0x73),
            ShortcutAction.Previous => new(0x74), ShortcutAction.Next => new(0x75),
            ShortcutAction.SeekBack => new(0x25), ShortcutAction.SeekForward => new(0x27),
            ShortcutAction.ToggleMapping => new(0x79, ShortcutModifiers.Ctrl | ShortcutModifiers.Shift), _ => null },
        a == ShortcutAction.ToggleMapping ? ShortcutScope.GameForeground : ShortcutScope.Global)).ToList();
    public static bool Intersects(ShortcutScope a, ShortcutScope b) => a == b || a == ShortcutScope.Global || b == ShortcutScope.Global;
    public static string? Validate(IReadOnlyList<FunctionShortcut> settings, IEnumerable<ushort> performanceKeys)
    {
        if (settings.Count != Enum.GetValues<ShortcutAction>().Length || settings.Select(s => s.Action).Distinct().Count() != settings.Count)
            return "IncompleteShortcuts";
        var performance = performanceKeys.ToHashSet();
        foreach (var setting in settings)
        {
            if (!Enum.IsDefined(setting.Action) || !Enum.IsDefined(setting.Scope)) return "InvalidShortcut";
            if (setting.Chord is not { } chord) continue;
            if (chord.Key is 0 or > 0xFE || ((int)chord.Modifiers & ~7) != 0
                || chord.Key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0x7B or 0xE7
                || chord.Key is >= 0xA0 and <= 0xA5
                || chord.Key is 0x01 or 0x02 or 0x04 or 0x05 or 0x06
                || chord.Key == 0x09 && chord.Modifiers.HasFlag(ShortcutModifiers.Alt)
                || chord.Key == 0x73 && chord.Modifiers.HasFlag(ShortcutModifiers.Alt)
                || chord.Key == 0x2E && chord.Modifiers.HasFlag(ShortcutModifiers.Ctrl | ShortcutModifiers.Alt)
                || chord.Key == 0x1B && chord.Modifiers != 0 || chord.Key == 0x20 && chord.Modifiers.HasFlag(ShortcutModifiers.Alt))
                return "ReservedShortcut";
            if (performance.Contains(chord.Key)) return "PerformanceKeyConflict";
            if (settings.Any(other => other.Action != setting.Action && other.Chord == chord && Intersects(other.Scope, setting.Scope))) return "DuplicateShortcut";
        }
        return null;
    }
    public static bool InScope(ShortcutScope scope, ShortcutContext c) => !c.TextInput && (scope == ShortcutScope.Global
        || scope == ShortcutScope.GameForeground && c.GameForeground || scope == ShortcutScope.AppForeground && c.AppForeground);
}

public sealed class ShortcutRegistrations(IShortcutRegistrar registrar) : IDisposable
{
    readonly Dictionary<ShortcutChord, int> _registered = new();
    int _next = 1200;
    public IReadOnlyDictionary<ShortcutChord, int> Registered => _registered;
    public void Apply(IEnumerable<FunctionShortcut> settings, bool mapping = false)
    {
        var required = settings.Where(s => FeatureAvailability.ShortcutAvailable(s.Action) && !mapping && s.Scope == ShortcutScope.Global && s.Chord != null).Select(s => s.Chord!.Value).ToHashSet();
        var added = new Dictionary<ShortcutChord, int>(); var removed = new Dictionary<ShortcutChord, int>();
        try
        {
            foreach (var chord in required.Except(_registered.Keys))
            {
                do { _next=_next>=0xBFFF?1200:_next+1; } while(_registered.Values.Contains(_next)||added.Values.Contains(_next));
                int id=_next;
                if (!registrar.Register(id, chord)) throw new InvalidOperationException("ShortcutRegistrationFailed");
                added.Add(chord, id);
            }
            foreach (var pair in _registered.Where(p => !required.Contains(p.Key)))
            {
                if (!registrar.Unregister(pair.Value)) throw new InvalidOperationException("ShortcutUnregistrationFailed");
                removed.Add(pair.Key, pair.Value);
            }
        }
        catch (Exception original)
        {
            var errors = new List<Exception> { original };
            foreach (var p in added) if (!registrar.Unregister(p.Value)) {_registered[p.Key]=p.Value;errors.Add(new InvalidOperationException("ShortcutRollbackCleanupFailed"));}
            foreach (var p in removed) if (!registrar.Register(p.Value, p.Key)) {_registered.Remove(p.Key);errors.Add(new InvalidOperationException("ShortcutRollbackRestoreFailed"));}
            if (errors.Count > 1) throw new AggregateException(errors);
            throw;
        }
        foreach (var p in removed) _registered.Remove(p.Key);
        foreach (var p in added) _registered.Add(p.Key, p.Value);
    }
    public void Dispose()
    {
        foreach (var p in _registered.ToArray()) if (registrar.Unregister(p.Value)) _registered.Remove(p.Key);
    }
}

public sealed class ShortcutRouter
{
    readonly HashSet<ushort> _down = new();
    public bool Capturing { get; set; }
    public List<FunctionShortcut> Settings { get; set; } = ShortcutRules.Defaults();
    public IReadOnlySet<ushort> PerformanceKeys { get; set; } = new HashSet<ushort>();
    public ShortcutAction? KeyEvent(ushort key, ShortcutModifiers modifiers, bool down, ShortcutContext context, bool registeredMessage = false)
    {
        if (!down) { _down.Remove(key); return null; }
        if (!registeredMessage && !_down.Add(key)) return null;
        if (Capturing || PerformanceKeys.Contains(key)) return null;
        var chord = new ShortcutChord(key, modifiers);
        return Settings.Where(s => FeatureAvailability.ShortcutAvailable(s.Action) && s.Chord == chord && ShortcutRules.InScope(s.Scope, context)
            && (context.Mapping || registeredMessage == (s.Scope == ShortcutScope.Global))).Select(s => (ShortcutAction?)s.Action).SingleOrDefault();
    }
    public void Clear() => _down.Clear();
}
