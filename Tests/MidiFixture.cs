using System.Buffers.Binary;
using System.IO;
using SMAP_WPF.Core;

namespace SMAP.Tests;

internal sealed class MidiFixture
{
    readonly List<List<(long tick, byte[] data)>> _tracks = new();
    public int Resolution = 480;
    public int Format = 1;
    public MidiFixture(int tracks = 1) { for (int i = 0; i < tracks; i++) _tracks.Add(new()); }
    public MidiFixture Event(int track, long tick, params byte[] data) { _tracks[track].Add((tick, data)); return this; }
    public MidiFixture Note(int pitch, long start, long end, int channel = 1, int track = 0, int velocity = 100) =>
        Event(track, start, (byte)(0x90 + channel - 1), (byte)pitch, (byte)velocity).Event(track, end, (byte)(0x80 + channel - 1), (byte)pitch, 0);
    public MidiFixture Pedal(long tick, int value, int channel = 1, int track = 0) => Event(track, tick, (byte)(0xb0 + channel - 1), 64, (byte)value);
    public MidiFixture Tempo(long tick, int microseconds, int track = 0) => Event(track, tick, 0xff, 0x51, 3,
        (byte)(microseconds >> 16), (byte)(microseconds >> 8), (byte)microseconds);
    public byte[] Bytes(bool endTrack = true)
    {
        using var output = new MemoryStream(); output.Write("MThd"u8); Int32(output, 6);
        UInt16(output, Format); UInt16(output, _tracks.Count); UInt16(output, Resolution);
        foreach (var events in _tracks)
        {
            using var track = new MemoryStream(); long previous = 0;
            foreach (var ev in events.OrderBy(e => e.tick)) { Vlq(track, checked((int)(ev.tick - previous))); track.Write(ev.data); previous = ev.tick; }
            if (endTrack) track.Write(new byte[] { 0, 0xff, 0x2f, 0 });
            output.Write("MTrk"u8); Int32(output, (int)track.Length); track.Position = 0; track.CopyTo(output);
        }
        return output.ToArray();
    }
    public MidiSource Read() => MidiSourceReader.Read(Bytes());
    public static void Int32(Stream s, int n) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, n); s.Write(b); }
    static void UInt16(Stream s, int n) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)n); s.Write(b); }
    static void Vlq(Stream s, int n)
    {
        Span<byte> bytes = stackalloc byte[4]; int i = 3; bytes[i] = (byte)(n & 127);
        while ((n >>= 7) > 0) bytes[--i] = (byte)((n & 127) | 128);
        s.Write(bytes[i..]);
    }
    public static SapmVoiceOptions[] Options(MidiSource source, string instrument = "Piano", SapmMode mode = SapmMode.Melody, bool auto = false) =>
        source.Voices.Select(v => new SapmVoiceOptions { VoiceId = v.Id, Profile = GamePitchProfiles.Get(instrument), Mode = mode, AutoTranspose = auto }).ToArray();
}
