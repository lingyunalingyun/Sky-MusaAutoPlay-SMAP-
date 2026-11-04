using SMAP_WPF.Core;
using SMAP_WPF;
using Xunit;

namespace SMAP.Tests;

public class ShortcutTests
{
    sealed class Registrar : IShortcutRegistrar
    {
        public Dictionary<int, ShortcutChord> Active = new(); public int? FailKey;
        public bool Register(int id, ShortcutChord chord) { if (chord.Key == FailKey) return false; return Active.TryAdd(id, chord); }
        public bool Unregister(int id) => Active.Remove(id);
    }
    [Fact] public void DefaultsKeepAllEightExistingBindings()
    {
        var defaults = ShortcutRules.Defaults(); Assert.Null(ShortcutRules.Validate(defaults, SkyPlayer.DefaultVk));
        Assert.Equal(0x70, defaults.Single(s => s.Action == ShortcutAction.PlayStop).Chord!.Value.Key);
        Assert.Equal(0x75, defaults.Single(s => s.Action == ShortcutAction.Next).Chord!.Value.Key);
    }
    [Theory] [InlineData(ShortcutScope.Global, ShortcutScope.GameForeground, true)]
    [InlineData(ShortcutScope.Global, ShortcutScope.AppForeground, true)]
    [InlineData(ShortcutScope.AppForeground, ShortcutScope.GameForeground, false)]
    public void ConflictScopesUseIntersection(ShortcutScope a, ShortcutScope b, bool intersects) => Assert.Equal(intersects, ShortcutRules.Intersects(a,b));
    [Theory] [InlineData(0x7B, 0)] [InlineData(0x09, 1)] [InlineData(0x2E, 3)] [InlineData(0x5B, 0)]
    public void ReservedCombinationsRejected(ushort key, int modifiers)
    {
        var settings = ShortcutRules.Defaults(); settings[0] = settings[0] with { Chord = new(key, (ShortcutModifiers)modifiers) };
        Assert.Equal("ReservedShortcut", ShortcutRules.Validate(settings, SkyPlayer.DefaultVk));
    }
    [Fact] public void PerformanceConflictAlsoAppliesToModifiedChord()
    {
        var s = ShortcutRules.Defaults(); s[0] = s[0] with { Chord = new(0x59, ShortcutModifiers.Ctrl) };
        Assert.Equal("PerformanceKeyConflict", ShortcutRules.Validate(s, SkyPlayer.DefaultVk));
    }
    [Fact] public void FailedRegistrationRetainsPreviousLiveBindings()
    {
        var r = new Registrar(); using var registrations = new ShortcutRegistrations(r); var s = ShortcutRules.Defaults(); registrations.Apply(s);
        var before = r.Active.OrderBy(p => p.Key).ToArray(); r.FailKey = 0x78;
        s[0] = s[0] with { Chord = new(0x77) }; s[1] = s[1] with { Chord = new(0x78) };
        Assert.Throws<InvalidOperationException>(() => registrations.Apply(s)); Assert.Equal(before, r.Active.OrderBy(p => p.Key));
    }
    [Fact] public void MappingSuspendsRegistrationsAndRoutesOnePassThroughEvent()
    {
        var r = new Registrar(); using var registrations = new ShortcutRegistrations(r); registrations.Apply(ShortcutRules.Defaults()); registrations.Apply(ShortcutRules.Defaults(), true);
        Assert.Empty(r.Active); var router = new ShortcutRouter(); var c = new ShortcutContext(true, false, false, true);
        Assert.Equal(ShortcutAction.PlayStop, router.KeyEvent(0x70, 0, true, c)); Assert.Null(router.KeyEvent(0x70, 0, true, c));
        router.KeyEvent(0x70, 0, false, c); Assert.Equal(ShortcutAction.PlayStop, router.KeyEvent(0x70, 0, true, c));
    }
    [Fact] public void RegisteredAndHookBackendsCannotDoubleDispatch()
    {
        var router = new ShortcutRouter(); var c = new ShortcutContext(false, true, false, false);
        Assert.Null(router.KeyEvent(0x70, 0, true, c)); Assert.Equal(ShortcutAction.PlayStop, router.KeyEvent(0x70, 0, true, c, true));
        Assert.Null(router.KeyEvent(0x71, 0, true, c with { TextInput = true }, true));
        router.Capturing = true; Assert.Null(router.KeyEvent(0x72, 0, true, c, true));
    }
}
