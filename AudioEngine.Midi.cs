using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SMAP_WPF;

internal sealed class ReferenceToneProvider(int pitch, int velocity) : ISampleProvider
{
    readonly double _step = Math.PI * 2 * (440 * Math.Pow(2, (pitch - 69) / 12.0)) / 44100;
    double _phase;
    int _attack, _release = 882;
    public volatile bool Stop;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    public int Read(float[] buffer, int offset, int count)
    {
        int written = 0;
        while (written + 1 < count)
        {
            if (Stop && _release == 0) break;
            float envelope = Math.Min(1, ++_attack / 220f) * (Stop ? _release-- / 882f : 1);
            float sample = (float)Math.Sin(_phase) * .12f * (velocity / 127f) * envelope;
            buffer[offset + written++] = sample; buffer[offset + written++] = sample;
            _phase += _step; if (_phase >= Math.PI * 2) _phase -= Math.PI * 2;
        }
        return written;
    }
}

internal sealed class AuditionSampleBank
{
    public const long MaxBytes = 128L * 1024 * 1024;
    public const int MaxSounds = 256;
    readonly Dictionary<(string instrument, int key, int shift), CachedSound> _sounds = new();
    public long Bytes { get; private set; }
    public int Count => _sounds.Count;
    static readonly Dictionary<string, int> SampleRoots = new(StringComparer.Ordinal)
    {
        ["Piano"] = 60, ["Flute"] = 60, ["Cello"] = 48, ["Harp"] = 48, ["Horn"] = 48,
        ["Contrabass"] = 48, ["Winter Piano"] = 72, ["Xylophone"] = 72, ["Guitar"] = 48,
        ["Violin"] = 48, ["Saxophone"] = 48, ["Ocarina"] = 48, ["Bugle"] = 60, ["Kalimba"] = 48,
        ["Ukulele"] = 60, ["Electric Guitar"] = 48
    };
    static readonly int[] Intervals = { 0, 2, 4, 5, 7, 9, 11, 12, 14, 16, 17, 19, 21, 23, 24 };
    // 本地资源的采样音区；与 Wiki 游戏配置分别维护，不能用于推断未核实的游戏音高。
    internal static int? SamplePitch(string instrument, int key) => key is >= 0 and < 15 && SampleRoots.TryGetValue(instrument, out int root) ? root + Intervals[key] : null;
    public AuditionSampleBank(IEnumerable<AuditionTone> tones)
    {
        var requests = tones.Where(t => !t.Reference).DistinctBy(t => (t.Instrument, t.SampleKey, t.SampleShift)).ToArray();
        if (requests.Length > MaxSounds) throw new InvalidDataException("试听采样种类超出限制，请减少声部或缩短区段。");
        foreach (var tone in requests)
        {
            if (Count == MaxSounds || tone.SampleShift is < -48 or > 48) throw new InvalidDataException("试听资源超出支持范围，请减少声部或缩短区段。");
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Instruments", tone.Instrument, tone.SampleKey + ".wav");
            if (SamplePitch(tone.Instrument, tone.SampleKey) == null || !File.Exists(path)) throw new InvalidDataException("缺少已校准的试听采样。");
            var sound = new CachedSound(path, tone.SampleShift);
            long bytes = ((long)sound.Data.Length + sound.LoopData.Length) * sizeof(float);
            if (bytes > MaxBytes - Bytes) throw new InvalidDataException("试听缓存超出 128 MiB，请减少声部或缩短区段。");
            _sounds.Add((tone.Instrument, tone.SampleKey, tone.SampleShift), sound); Bytes += bytes;
        }
    }
    public CachedSound Sound(AuditionTone tone) => _sounds[(tone.Instrument, tone.SampleKey, tone.SampleShift)];
}

internal sealed class PreparedMidiVoices : IMidiAuditionVoices
{
    readonly AuditionSampleBank _bank;
    public PreparedMidiVoices(AuditionSampleBank bank) { _bank = bank; AudioEngine.Init(); }
    public void Start(Guid owner, Guid voice, AuditionTone tone, bool sustain) => AudioEngine.StartMidiVoice(owner, voice, tone, sustain, _bank);
    public void Release(Guid voice) => AudioEngine.ReleaseVoice(voice);
    public void StopOwner(Guid owner) => AudioEngine.StopOwner(owner);
}

public static partial class AudioEngine
{
    internal static void StartMidiVoice(Guid owner, Guid voice, AuditionTone tone, bool sustain, AuditionSampleBank bank)
    {
        lock (_lock)
        {
            if (_out == null) Init();
            if (tone.Reference)
            {
                var reference = new ReferenceToneProvider(tone.Pitch, tone.Velocity);
                _voices!.Start(voice, owner, reference, () => reference.Stop = true);
            }
            else if (sustain && SustainInstruments.Contains(tone.Instrument))
            {
                var loop = new LoopProvider(bank.Sound(tone));
                _voices!.Start(voice, owner, new VolumeSampleProvider(loop) { Volume = tone.Velocity / 127f }, () => loop.Stop = true);
            }
            else _voices!.Start(voice, owner, new VolumeSampleProvider(new CachedSoundProvider(bank.Sound(tone))) { Volume = tone.Velocity / 127f });
        }
    }
}
