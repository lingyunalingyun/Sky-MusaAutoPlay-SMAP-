using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;
[Collection("Playback library")]
public class LegacySharingTests
{
    const string Song = "{\"name\":\"Old\",\"bpm\":120,\"author\":\"Author\",\"custom\":{\"sourceId\":42},\"songNotes\":[{\"time\":0,\"key\":\"1Key0\",\"velocity\":99},{\"time\":500,\"key\":2}]}";
    static LegacyConversionEntry Read(string text) => LegacyConversion.Read(Encoding.UTF8.GetBytes(text))[0];
    [Theory]
    [InlineData(".txt", false)][InlineData(".json", false)][InlineData(".json", true)]
    public void SingleSongPreservesOldFingerprintSourceAndRecovery(string extension, bool array)
    {
        using var f = new TestProject(); string path = f.PathFor("source" + extension);
        File.WriteAllText(path, array ? "[" + Song + "]" : Song); byte[] original = File.ReadAllBytes(path);
        var previous = LegacyProjectAdapter.FromDocument(SongLibrary.LoadDocument(new SongInfo { File = path }));
        var current = LegacyProjectAdapter.Load(path);
        Assert.Equal(PerformanceIdentity.ForProject(previous), PerformanceIdentity.ForProject(current));
        var restored = SmapFile.FromBytes(SmapFile.ToBytes(current));
        Assert.Equal(99, restored.Tracks[0].LegacyFields!["smapLegacySource"]!["originalSong"]!["songNotes"]![0]!["velocity"]!.GetValue<int>());
        Assert.Equal(42, restored.Tracks[0].LegacyFields!["custom"]!["sourceId"]!.GetValue<int>());
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(PerformanceIdentity.ForProject(current), PerformanceIdentity.ForProject(restored));
    }
    [Theory][InlineData(4)][InlineData(8)][InlineData(15)]
    public void KeyCapabilitiesAndExplicitTimeUnitsArePreserved(int count)
    {
        var p = Read("{\"keyCount\":" + count + ",\"timeUnit\":\"s\",\"songNotes\":[{\"time\":1.5,\"key\":" + (count-1) + ",\"duration\":0.25,\"type\":\"hold\"}]}").Project!;
        Assert.Equal(count, p.Tracks[0].Capability?.PhysicalKeyCount ?? 15);
        var note = new ProjectTimeline(p.Tempos).GetNotes(p.Tracks[0]).Single();
        Assert.InRange(note.StartMicroseconds, 1499999.999m, 1500000.001m);
        Assert.Equal(NoteAction.Hold, note.Action);
        var restored = SmapFile.FromBytes(SmapFile.ToBytes(p)); Assert.Equal(PerformanceIdentity.ForProject(p), PerformanceIdentity.ForProject(restored));
    }
    [Theory]
    [InlineData("{\"songNotes\":[{\"time\":-1,\"key\":0}]}", "LEGACY_NOTE_TIME")]
    [InlineData("{\"songNotes\":[{\"time\":0,\"key\":\"brokenKey0\"}]}", "LEGACY_NOTE_KEY")]
    [InlineData("{\"keyCount\":8,\"songNotes\":[{\"time\":0,\"key\":8}]}", "LEGACY_NOTE_KEY")]
    [InlineData("{\"timeUnit\":\"ticks\",\"songNotes\":[]}", "LEGACY_TIME_UNIT")]
    [InlineData("{\"isEncrypted\":true,\"songNotes\":[]}", "LEGACY_ENCRYPTED")]
    [InlineData("{\"songNotes\":[{\"time\":0,\"key\":0,\"type\":\"hold\"}]}", "LEGACY_HOLD_AMBIGUOUS")]
    public void AmbiguousDataIsDiagnosedInsteadOfDiscardingNotes(string input, string expected)
    { var e=Read(input);Assert.Null(e.Project);Assert.Equal(expected,e.Error); }
    [Theory][InlineData("{")][InlineData("{\"songNotes\":[],\"songNotes\":[]}")]
    public void BrokenAndDuplicateJsonIsRejected(string text) => Assert.Throws<LegacyConversionException>(() => Read(text));
    [Fact]
    public void ArraysHaveIndependentOriginsAndRepeatImportDoesNotOverwriteEdits()
    {
        using var f = new TestProject(); var old = SongLibrary.SongsDir;
        try
        {
            SongLibrary.SongsDir=f.PathFor("library");Directory.CreateDirectory(SongLibrary.SongsDir);
            string file=Path.Combine(SongLibrary.SongsDir,"collection.json");File.WriteAllText(file,"["+Song+","+Song.Replace("Old","Second")+"]");var before=File.ReadAllBytes(file);
            var scanned=SongLibrary.Scan();Assert.Equal(2,scanned.Count);Assert.Equal(2,scanned.Select(s=>s.File).Distinct().Count());
            var first=SmapFile.Load(scanned[0].File);var second=SmapFile.Load(scanned[1].File);
            Assert.NotEqual(first.ProjectId,second.ProjectId);
            Assert.NotEqual(first.Tracks[0].LegacyFields!["smapLegacySource"]!["songIndex"]!.ToString(),second.Tracks[0].LegacyFields!["smapLegacySource"]!["songIndex"]!.ToString());
            SmapFile.Save(first with{Metadata=first.Metadata with{Name="edited"}},scanned[0].File);var edited=File.ReadAllBytes(scanned[0].File);
            Assert.Equal(2,SongLibrary.Scan().Count);Assert.Equal(edited,File.ReadAllBytes(scanned[0].File));Assert.Equal(before,File.ReadAllBytes(file));
            File.Delete(scanned[1].File);Assert.Single(SongLibrary.Scan());Assert.False(File.Exists(scanned[1].File));
        }
        finally { SongLibrary.SongsDir=old; }
    }
    [Fact]
    public void HistoricalLayerLabelsKeepExistingAudibleIdentityAndOriginalMetadata()
    {
        using var f=new TestProject();string path=f.PathFor("layers.txt");File.WriteAllText(path,Song.Replace("1Key0","2Key0"));
        var old=LegacyProjectAdapter.FromDocument(SongLibrary.LoadDocument(new SongInfo{File=path}));var current=LegacyProjectAdapter.Load(path);
        Assert.Equal(PerformanceIdentity.ForProject(old),PerformanceIdentity.ForProject(current));
        Assert.Equal("2Key0",current.Tracks[0].LegacyFields!["smapLegacySource"]!["originalSong"]!["songNotes"]![0]!["key"]!.GetValue<string>());
    }
    [Fact]
    public void PartialArraysKeepPerSongFailures()
    {
        var entries=LegacyConversion.Read(Encoding.UTF8.GetBytes("["+Song+",{\"songNotes\":[{\"time\":0,\"key\":99}]}]"));
        Assert.NotNull(entries[0].Project);Assert.Null(entries[1].Project);Assert.Equal(1,entries[1].SongIndex);
    }
    [Fact]
    public void SharingProjectionRetainsLegacyGateAndImageProjectionIsUnchanged()
    {
        var project=Read(Song).Project!;var options=new ScoreExportOptions{TrackIds=new[]{project.Tracks[0].Id}};
        var shared=NativeSharingClient.SharingSelection(project,options);
        Assert.NotNull(shared.Tracks[0].LegacyFields);
        Assert.Null(shared.Tracks[0].LegacyFields!["smapLegacySource"]!["originalSong"]);
        Assert.Null(shared.Tracks[0].LegacyFields!["custom"]);
        Assert.NotNull(project.Tracks[0].LegacyFields!["smapLegacySource"]!["originalSong"]);
        Assert.Equal(PerformanceIdentity.ForProject(project),PerformanceIdentity.ForProject(shared));
        Assert.Null(ScoreExport.ProjectSelection(project,options).Tracks[0].LegacyFields);
    }
    [Fact]
    public void GlobalBooleanCannotReplaceVerifiedCoverage()
    {
        var m=new SharingMatches(new(),new string('a',64),1,false,true);Assert.False(m.CanPublishLegacy);
        Assert.True((m with{LegacyCatalog=new("complete",1,"published-song-sheets",4,4,0)}).CanPublishLegacy);
        Assert.False((m with{LegacyCatalog=new("incomplete",1,"published-song-sheets",4,3,1)}).CanPublishLegacy);
        Assert.True((m with{LegacyItems=new(){new(1,0,new string('a',64),"Old",2,false,"public_legacy",null)}}).CanPublishLegacy);
    }
}
