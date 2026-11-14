using System.IO;
using System.Text.Json;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class FeatureAvailabilityTests
{
    sealed class Registrar:IShortcutRegistrar
    {
        public readonly Dictionary<int,ShortcutChord> Active=new();
        public ushort? ThrowKey;
        public bool Register(int id,ShortcutChord chord)
        {if(chord.Key==ThrowKey)throw new System.ComponentModel.Win32Exception(1409);return Active.TryAdd(id,chord);}
        public bool Unregister(int id)=>Active.Remove(id);
    }
    [Fact] public void ProductionBuildCannotEnableMapping()
    {
        Assert.False(FeatureAvailability.SheetMappingEnabled);
        Assert.Equal("SheetMappingDeferredToV31",Assert.Throws<NotSupportedException>(()=>FeatureAvailability.RequireSheetMapping()).Message);
    }
    [Theory][InlineData(ShortcutAction.ToggleMapping)][InlineData(ShortcutAction.PauseMapping)]
    public void LegacyMappingShortcutsCannotRegisterOrDispatch(ShortcutAction action)
    {
        var settings=ShortcutRules.Defaults().Select(s=>s.Action==action?s with{Chord=new(0x78),Scope=ShortcutScope.Global}:s).ToList();
        var restored=JsonSerializer.Deserialize<InputPreferences>(JsonSerializer.Serialize(new InputPreferences{Functions=settings}))!;
        Assert.Equal(new ShortcutChord(0x78),restored.Functions.Single(s=>s.Action==action).Chord);
        var registrar=new Registrar();using var registrations=new ShortcutRegistrations(registrar);registrations.Apply(restored.Functions);
        Assert.DoesNotContain(registrar.Active.Values,c=>c.Key==0x78);
        foreach(var context in new[]{new ShortcutContext(false,true,false,false),new(true,false,false,false),new(true,false,false,true)})
        foreach(bool registered in new[]{false,true})
        {
            var router=new ShortcutRouter{Settings=restored.Functions};
            Assert.Null(router.KeyEvent(0x78,0,true,context,registered));
        }
    }
    [Fact] public void AllOrdinaryActionsRemainAvailable()
    {
        Assert.All(Enum.GetValues<ShortcutAction>().Except(new[]{ShortcutAction.ToggleMapping,ShortcutAction.PauseMapping}),a=>Assert.True(FeatureAvailability.ShortcutAvailable(a)));
        var registrar=new Registrar();using(var registrations=new ShortcutRegistrations(registrar))
        {registrations.Apply(ShortcutRules.Defaults());Assert.Equal(8,registrar.Active.Count);}
        Assert.Empty(registrar.Active);
    }
    [Fact] public void NativeRegistrationExceptionRetainsPreviousLiveBindings()
    {
        var registrar=new Registrar();using var registrations=new ShortcutRegistrations(registrar);var settings=ShortcutRules.Defaults();registrations.Apply(settings);
        var before=registrar.Active.OrderBy(p=>p.Key).ToArray();registrar.ThrowKey=0x78;
        settings[0]=settings[0] with{Chord=new(0x77)};settings[1]=settings[1] with{Chord=new(0x78)};
        Assert.Throws<System.ComponentModel.Win32Exception>(()=>registrations.Apply(settings));Assert.Equal(before,registrar.Active.OrderBy(p=>p.Key));
    }
    [Theory][InlineData(0x59,0)][InlineData(0x55,2)][InlineData(0x49,1)][InlineData(0x4F,4)][InlineData(0x50,0)]
    public void PerformanceKeysCannotBecomeCommandsEvenFromStaleSettings(ushort key,int modifiers)
    {
        var settings=ShortcutRules.Defaults();settings[0]=settings[0] with{Chord=new(key,(ShortcutModifiers)modifiers)};
        Assert.Equal("PerformanceKeyConflict",ShortcutRules.Validate(settings,SkyPlayer.DefaultVk));
        var router=new ShortcutRouter{Settings=settings,PerformanceKeys=SkyPlayer.DefaultVk.ToHashSet()};
        Assert.Null(router.KeyEvent(key,(ShortcutModifiers)modifiers,true,new(true,false,false,false),true));
        Assert.Null(router.KeyEvent(key,0,false,new(true,false,false,false)));
    }
    [Fact] public void MappingRestrictionDoesNotChangeOrdinaryPracticeRouting()
    {
        var router=new ShortcutRouter{PerformanceKeys=SkyPlayer.DefaultVk.ToHashSet()};var game=new ShortcutContext(true,false,false,false);
        Assert.Null(router.KeyEvent(0x70,0,true,game));
        Assert.Equal(ShortcutAction.PlayStop,router.KeyEvent(0x70,0,true,game,true));
        Assert.Null(router.KeyEvent(0x70,0,true,game));
        Assert.Null(router.KeyEvent(0x71,0,true,game with{TextInput=true},true));
        router.Capturing=true;Assert.Null(router.KeyEvent(0x72,0,true,game,true));
        router.Capturing=false;router.Clear();Assert.Equal(ShortcutAction.Slower,router.KeyEvent(0x72,0,true,game,true));
    }
    [Theory][InlineData("Piano")][InlineData("Handpan")][InlineData("Custom4")]
    public void DisabledMappingDoesNotChangeCapabilityProjectRoundTrip(string instrument)
    {
        var c=InstrumentCapabilities.ForInstrument(instrument)!;var t=new ProjectTrack{Capability=c,KeyDomain=NoteKeyDomain.StableLogical};
        t=t with{Notes=new(){new(){TrackId=t.Id,Key=c.Keys[0].LogicalKey,Beat=.125m,Action=NoteAction.Hold,LengthBeats=1.75m}}};
        var project=new SmapProject{FormatVersion=SmapProject.CapabilityFormatVersion,KeyInterpretationVersion=1,Tracks=new(){t}};
        string identity=PerformanceIdentity.ForProject(project),path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".smap");
        try
        {
            SmapFile.Save(project,path);var loaded=SmapFile.Load(path);
            Assert.Equal(identity,PerformanceIdentity.ForProject(loaded));Assert.Equal(t.Notes,loaded.Tracks[0].Notes);
            Assert.Equal(SheetCalibration.Fingerprint(c),SheetCalibration.Fingerprint(loaded.Tracks[0].Capability!));
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }
}
