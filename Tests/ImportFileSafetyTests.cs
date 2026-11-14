using System.IO;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

[Collection("Playback library")]
public sealed class ImportFileSafetyTests
{
    [Fact]
    public void ReimportNeverOverwritesLocalEditsAndExactRepeatsAreIdempotent()
    {
        using var files = new TestProject(); string original = SongLibrary.SongsDir;
        try
        {
            SongLibrary.SongsDir=files.PathFor("library");var p=TestProject.Sample();string source=files.PathFor("song.smap");SmapFile.Save(p,source);
            string first=SongLibrary.CopyImported(source);var edited=p with{Metadata=p.Metadata with{Name="local edit"}};SmapFile.Save(edited,first);byte[] before=File.ReadAllBytes(first);
            string second=SongLibrary.CopyImported(source);Assert.NotEqual(first,second);Assert.Equal(before,File.ReadAllBytes(first));
            Assert.Equal(PerformanceIdentity.ForProject(p),PerformanceIdentity.ForProject(SmapFile.Load(second)));
            Assert.Equal(second,SongLibrary.CopyImported(source));Assert.Equal(2,Directory.GetFiles(SongLibrary.SongsDir,"*.smap").Length);
        }
        finally { SongLibrary.SongsDir=original; }
    }
    [Theory]
    [InlineData("broken.txt")][InlineData("broken.json")][InlineData("broken.smap")]
    public void InvalidFilesDoNotEnterTheLibrary(string name)
    {
        using var files=new TestProject();string old=SongLibrary.SongsDir;
        try
        {
            SongLibrary.SongsDir=files.PathFor("library");string source=files.PathFor(name);File.WriteAllText(source,"{broken");
            Assert.ThrowsAny<Exception>(()=>SongLibrary.CopyImported(source));Assert.False(Directory.Exists(SongLibrary.SongsDir));
        }
        finally { SongLibrary.SongsDir=old; }
    }
    [Fact]
    public void OldArrayFilePreservesAllOriginalBytesAndMultipleSongs()
    {
        using var files=new TestProject();string old=SongLibrary.SongsDir;
        try
        {
            SongLibrary.SongsDir=files.PathFor("library");string source=files.PathFor("旧曲库.txt");
            File.WriteAllText(source,"[{\"name\":\"旧曲一\",\"bpm\":120,\"songNotes\":[{\"key\":\"1Key0\",\"time\":0}]},{\"name\":\"旧曲二\",\"songNotes\":[]}]");
            string imported=SongLibrary.CopyImported(source);Assert.Equal(File.ReadAllBytes(source),File.ReadAllBytes(imported));Assert.Equal(imported,SongLibrary.CopyImported(source));
        }
        finally { SongLibrary.SongsDir=old; }
    }
}
