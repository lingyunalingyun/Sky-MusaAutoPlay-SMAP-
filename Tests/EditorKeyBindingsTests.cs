using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class EditorKeyBindingsTests
{
    [Fact]public void PlainEditorUsesCustomizedPianoPerformanceBindings()
    {
        var c=InstrumentCapabilities.ForInstrument("Piano")!;var profile=PerformanceBindingProfile.FromLegacy(c,SkyPlayer.DefaultVk);
        profile.Keys["key-0"]=0x41;
        var inputs=new InputPreferences{Performance=new(){profile}};var t=new ProjectTrack();
        Assert.Equal((ushort)0x41,EditorWindow.EditorBindings(t,inputs,SkyPlayer.DefaultVk)[0]);
        Assert.Equal(SkyPlayer.DefaultVk[1],EditorWindow.EditorBindings(t,inputs,SkyPlayer.DefaultVk)[1]);
    }
    [Theory][InlineData(NoteKeyDomain.LegacyOrdinal)][InlineData(NoteKeyDomain.StableLogical)]
    public void SparseInstrumentsKeepTheCorrectNoteAndPhysicalBindingIdentity(NoteKeyDomain domain)
    {
        var c=InstrumentCapabilities.ForInstrument("Handpan")!;var track=new ProjectTrack{Capability=c,KeyDomain=domain};
        var profile=PerformanceBindingProfile.FromLegacy(c,SkyPlayer.DefaultVk);var inputs=new InputPreferences{Performance=new(){profile}};
        var keys=EditorWindow.EditorBindings(track,inputs,SkyPlayer.DefaultVk);
        Assert.Equal(8,keys.Count(k=>k!=null));
        foreach(var key in c.Keys)Assert.Equal(profile.Keys[key.Id],keys[c.NoteKey(key,domain)]);
    }
    [Fact]public void UnknownTargetDoesNotInventPhysicalBindings()
    {
        var keys=EditorWindow.EditorBindings(new(){GameTarget=new(){InstrumentId="Unknown"}},new(),SkyPlayer.DefaultVk);
        Assert.All(keys,k=>Assert.Null(k));
    }
}
