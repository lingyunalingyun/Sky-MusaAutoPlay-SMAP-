using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SMAP_WPF;

// 预加载一段 wav 到内存 (统一转 44100 立体声 float, 匹配混音器)
sealed class CachedSound
{
    public readonly float[] Data;
    public readonly WaveFormat WaveFormat;

    public CachedSound(string file)
    {
        using var reader = new AudioFileReader(file);
        ISampleProvider sp = reader;
        if (sp.WaveFormat.SampleRate != 44100)
            sp = new WdlResamplingSampleProvider(sp, 44100);
        if (sp.WaveFormat.Channels == 1)
            sp = new MonoToStereoSampleProvider(sp);
        WaveFormat = sp.WaveFormat;
        var list = new List<float>();
        var buf = new float[44100 * 2];
        int n;
        while ((n = sp.Read(buf, 0, buf.Length)) > 0)
            list.AddRange(buf.Take(n));
        Data = list.ToArray();
    }
}

// 一次性播放一个 CachedSound (播完自动被混音器移除)
sealed class CachedSoundProvider : ISampleProvider
{
    readonly CachedSound _s;
    int _pos;
    public CachedSoundProvider(CachedSound s) => _s = s;
    public WaveFormat WaveFormat => _s.WaveFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        int n = Math.Min(_s.Data.Length - _pos, count);
        if (n <= 0) return 0;
        Array.Copy(_s.Data, _pos, buffer, offset, n);
        _pos += n;
        return n;
    }
}

/// <summary>光遇 15 键音频引擎: 复用 Sky Studio 提取的乐器 wav, NAudio 混音复音播放。</summary>
public static class AudioEngine
{
    const int Keys = 15;
    static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Instruments");
    public static readonly string[] Instruments =
        { "Piano", "Harp", "Guitar", "Flute", "Ukulele", "Winter Piano", "Xylophone", "Electric Guitar", "Bassoon", "Orff" };

    static readonly object _lock = new();
    static IWavePlayer? _out;
    static MixingSampleProvider? _mixer;
    static Reverb? _reverb;
    static bool _cave;
    static readonly Dictionary<string, CachedSound?[]> _cache = new();
    static string _instrument = "Piano";

    /// <summary>洞穴音效(混响)开关。</summary>
    public static bool Cave
    {
        get => _cave;
        set { _cave = value; if (_reverb != null) _reverb.Enabled = value; }
    }

    public static void Init()
    {
        lock (_lock)
        {
            if (_out != null) return;
            _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)) { ReadFully = true };
            _reverb = new Reverb(_mixer) { Enabled = _cave };   // 洞穴混响接在混音器后
            _out = new WaveOutEvent { DesiredLatency = 100 };
            _out.Init(_reverb);
            _out.Play();
            Load(_instrument);
        }
    }

    static void Load(string name)
    {
        if (_cache.ContainsKey(name)) return;
        var arr = new CachedSound?[Keys];
        for (int i = 0; i < Keys; i++)
        {
            var f = Path.Combine(Dir, name, i + ".wav");
            try { if (File.Exists(f)) arr[i] = new CachedSound(f); } catch { }
        }
        _cache[name] = arr;
    }

    public static void SetInstrument(string name)
    {
        lock (_lock) { _instrument = name; if (_out != null) Load(name); }
    }

    public static void Play(int key)
    {
        if (key < 0 || key >= Keys) return;
        lock (_lock)
        {
            if (_out == null) Init();
            if (_cache.TryGetValue(_instrument, out var arr) && arr[key] is { } s)
                _mixer!.AddMixerInput(new CachedSoundProvider(s));
        }
    }

    /// <summary>立即静音: 移除所有正在播放的音符。</summary>
    public static void StopAll()
    {
        lock (_lock) { _mixer?.RemoveAllMixerInputs(); }
    }
}
