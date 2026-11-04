using System.IO;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

[Collection("Playback library")]
public class MidiNativeLibraryTests
{
    [Theory]
    [InlineData("Same name")] [InlineData("CON")] [InlineData("../bad:name")]
    public void NativeImportCreatesUniqueSafeFilesAndRetainsSourceAndPreviousFile(string name)
    {
        using var files = new TestProject(); string directory = SongLibrary.SongsDir;
        try
        {
            SongLibrary.SongsDir = files.DirectoryPath; var source = new MidiFixture().Note(60, 0, 480).Read();
            var project = MidiProjectConverter.Create(SapmMapper.Convert(source, MidiFixture.Options(source)), name);
            string first = SongLibrary.WriteNative(project); var before = File.ReadAllBytes(first); string second = SongLibrary.WriteNative(project);
            Assert.NotEqual(first, second); Assert.Equal(before, File.ReadAllBytes(first));
            Assert.StartsWith(Path.GetFullPath(files.DirectoryPath) + Path.DirectorySeparatorChar, Path.GetFullPath(second));
            Assert.Equal(".smap", Path.GetExtension(second)); Assert.Equal(2, SongLibrary.Scan().Count);
            Assert.Equal(project.OriginalMidi, SmapFile.Load(second).OriginalMidi);
        }
        finally { SongLibrary.SongsDir = directory; }
    }
}
