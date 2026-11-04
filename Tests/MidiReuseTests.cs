using System.Buffers.Binary;
using System.IO;
using SMAP_WPF;
using Xunit;

namespace SMAP.Tests;

public class MidiReuseTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(-1)]
    public void ExistingImportKeepsTopKeyTempoMapAndManualOctaveClamp(int octaveShift)
    {
        using var files = new TestProject(); string path = files.PathFor("source.mid");
        using (var file = File.Create(path))
        {
            file.Write("MThd"u8); WriteSize(file, 6); file.Write(new byte[] { 0, 1, 0, 2, 1, 224 });
            WriteTrack(file, new byte[]
            {
                0, 0xff, 0x51, 3, 7, 0xa1, 0x20,
                0x83, 0x60, 0xff, 0x51, 3, 0x0f, 0x42, 0x40,
                0, 0xff, 0x2f, 0
            });
            WriteTrack(file, new byte[]
            {
                0, 0xff, 3, 4, (byte)'l', (byte)'e', (byte)'a', (byte)'d',
                0, 0x90, 60, 100, 0x81, 0x70, 0x80, 60, 0,
                0x81, 0x70, 0x90, 84, 100, 0x83, 0x60, 0x80, 84, 0,
                0, 0x90, 72, 100, 0x83, 0x60, 0x80, 72, 0,
                0, 0xff, 0x2f, 0
            });
        }
        var importer = new MidiImporter(path); var track = Assert.Single(importer.AnalyzeTracks());
        Assert.Equal(1, track.Index); Assert.Equal("lead", track.Name); Assert.Equal(3, track.NoteCount);
        var selection = new HashSet<int> { 1 }; Assert.Equal(120, importer.InitialBpm());
        Assert.Equal(0, importer.SuggestShift(selection)); Assert.Equal(1, importer.WhiteRatioAfter(selection, 0));
        var notes = importer.Convert(selection, octaveShift, 0);
        Assert.Equal(new[] { (key: 0, ms: 0d), (key: 14, ms: 500d), (key: 7, ms: 1500d) }, notes);
    }

    static void WriteSize(Stream stream, int size)
    {
        Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, size); stream.Write(bytes);
    }

    static void WriteTrack(Stream stream, byte[] bytes)
    {
        stream.Write("MTrk"u8); WriteSize(stream, bytes.Length); stream.Write(bytes);
    }
}
