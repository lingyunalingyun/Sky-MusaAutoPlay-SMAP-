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
    public readonly float[] LoopData;   // 无缝循环单元(稳定段+首尾交叉淡化), 持续音长按循环用

    public CachedSound(string file, int semitone = 0)
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
        var data = list.ToArray();
        Data = semitone == 0 ? data : PitchShift(data, semitone);
        LoopData = BuildLoop(Data);
    }

    // 生成无缝循环单元: 起音后的有声全段, 音量平坦化 + 长交叉淡化, 循环连续不顿(持续音长按用)
    static float[] BuildLoop(float[] d)
    {
        const int sr = 44100;
        int frames = d.Length / 2;
        if (frames < sr / 4) return d;                          // <0.25s 太短, 整段循环
        int pk = 0; float pv = 0;
        for (int f = 0; f < frames; f++) { float a = Math.Abs(d[f * 2]); if (a > pv) { pv = a; pk = f; } }
        float thr = pv * 0.15f;
        int end = frames;                                       // 去尾部静音
        while (end > 2 && Math.Abs(d[(end - 1) * 2]) < thr) end--;
        int start = Math.Min(pk + sr / 20, Math.Max(0, end - sr / 4));   // 峰值后~50ms 起
        int len = end - start;
        if (len < sr / 5) { start = 0; len = end; }             // 有声段太短 -> 从头到有声末
        int cf = Math.Min(len / 3, (int)(sr * 0.15));           // 交叉淡化~150ms
        if (len <= cf * 2) return d;
        int outLen = len - cf;
        var loop = new float[outLen * 2];
        for (int f = 0; f < outLen; f++)
        {
            float l = d[(start + f) * 2], r = d[(start + f) * 2 + 1];
            if (f < cf)                                         // 头部混入尾后段 -> 首尾无缝
            {
                float t = f / (float)cf;
                l = l * t + d[(start + outLen + f) * 2] * (1 - t);
                r = r * t + d[(start + outLen + f) * 2 + 1] * (1 - t);
            }
            loop[f * 2] = l; loop[f * 2 + 1] = r;
        }
        Flatten(loop);                                          // 音量平坦化, 消除段内周期强弱
        return loop;
    }

    // 用滑动 RMS 把整段音量拉平(消除颤音/衰减导致的周期强弱), 循环才连续优美
    static void Flatten(float[] loop)
    {
        int frames = loop.Length / 2;
        const int win = 2205;                                   // 50ms 窗口
        var ps = new double[frames + 1];                        // 左声道平方前缀和
        for (int f = 0; f < frames; f++) ps[f + 1] = ps[f] + loop[f * 2] * (double)loop[f * 2];
        float target = (float)Math.Sqrt(ps[frames] / Math.Max(1, frames));
        if (target < 1) return;
        for (int f = 0; f < frames; f++)
        {
            int a = Math.Max(0, f - win / 2), b = Math.Min(frames, f + win / 2);
            float rms = (float)Math.Sqrt((ps[b] - ps[a]) / Math.Max(1, b - a));
            float g = rms > 1 ? Math.Clamp(target / rms, 0.3f, 3f) : 1f;
            loop[f * 2] *= g; loop[f * 2 + 1] *= g;
        }
    }

    // 立体声交错整体重采样: 移调(半音), 顺带略改时长; 用于每乐器移调
    static float[] PitchShift(float[] src, int semitone)
    {
        double ratio = Math.Pow(2, semitone / 12.0);   // >1 升调变短, <1 降调变长
        int frames = src.Length / 2;
        int outFrames = Math.Max(1, (int)(frames / ratio));
        var outp = new float[outFrames * 2];
        for (int i = 0; i < outFrames; i++)
        {
            double pos = i * ratio;
            int i0 = (int)pos; double frac = pos - i0;
            int i1 = Math.Min(i0 + 1, frames - 1);
            outp[i * 2]     = (float)(src[i0 * 2]     * (1 - frac) + src[i1 * 2]     * frac);
            outp[i * 2 + 1] = (float)(src[i0 * 2 + 1] * (1 - frac) + src[i1 * 2 + 1] * frac);
        }
        return outp;
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

// 循环播放一个 CachedSound (持续音乐器长按用): 循环采样直到 Stop, Stop 后短淡出避免爆音
sealed class LoopProvider : ISampleProvider
{
    readonly CachedSound _s;
    int _pos;
    public volatile bool Stop;
    int _fade = -1;                    // >=0: 淡出剩余帧数
    const int FadeFrames = 1500;       // ~34ms @44100 淡出
    public LoopProvider(CachedSound s) => _s = s;
    public WaveFormat WaveFormat => _s.WaveFormat;
    public int Read(float[] buffer, int offset, int count)
    {
        if (Stop && _fade < 0) _fade = FadeFrames;
        int written = 0;
        while (written < count)
        {
            if (_pos >= _s.LoopData.Length) _pos = 0;   // 无缝循环
            int n = Math.Min(_s.LoopData.Length - _pos, count - written);
            Array.Copy(_s.LoopData, _pos, buffer, offset + written, n);
            _pos += n; written += n;
        }
        if (_fade >= 0)                              // 淡出(按帧递减增益)
        {
            for (int i = 0; i + 1 < written; i += 2)
            {
                float g = Math.Max(0, _fade / (float)FadeFrames);
                buffer[offset + i] *= g;
                buffer[offset + i + 1] *= g;
                if (--_fade <= 0) return i + 2;      // 淡出完 -> 结束(mixer 移除)
            }
        }
        return written;
    }
}

/// <summary>光遇 15 键音频引擎: 复用 Sky Studio 提取的乐器 wav, NAudio 混音复音播放。</summary>
public static class AudioEngine
{
    const int Keys = 15;
    static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Instruments");
    public static readonly string[] Instruments =
        { "Piano", "Harp", "Guitar", "Flute", "Ukulele", "Winter Piano", "Xylophone", "Electric Guitar", "Bassoon", "Orff", "Kalimba", "Ocarina", "Cello", "Violin", "Saxophone", "Pipa", "Quena", "Bugle", "Glock", "LightGuitar", "GoldPiano", "Horn", "Handpan", "GoldHandpan", "Dundun", "APBell1", "APBell2",
          "Harmonica", "AP18Ocarina", "AP29Piccolo", "GoldBugle", "APPiano", "4thAnnivArp", "4thAnnivLead",
          "Contrabass", "4thAnnivBass", "GoldDundun" };

    static readonly object _lock = new();
    static IWavePlayer? _out;
    static MixingSampleProvider? _mixer;
    static Reverb? _reverb;
    static bool _cave;
    static readonly Dictionary<string, CachedSound?[]> _cache = new();
    static readonly Dictionary<int, LoopProvider> _activeVoices = new();   // 持续音: 正在长按的键 -> 循环voice
    static readonly HashSet<string> SustainInstruments = new()             // 可长音的乐器(管乐/弓弦), 长按持续
    { "Flute", "Ocarina", "AP18Ocarina", "Saxophone", "Harmonica", "Horn", "Quena", "Bugle", "GoldBugle", "AP29Piccolo", "Bassoon", "Violin", "Cello", "Contrabass" };
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
            try { if (File.Exists(f)) arr[i] = new CachedSound(f, PitchConfig.Get(name)); } catch { }
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

    /// <summary>按下琴键: 可长音乐器起循环长音(松开才停), 其余乐器短音一次。</summary>
    public static void NoteOn(int key)
    {
        if (key < 0 || key >= Keys) return;
        if (!SustainInstruments.Contains(_instrument)) { Play(key); return; }   // 短音乐器
        lock (_lock)
        {
            if (_out == null) Init();
            if (_activeVoices.TryGetValue(key, out var old)) { old.Stop = true; _activeVoices.Remove(key); }
            if (_cache.TryGetValue(_instrument, out var arr) && arr[key] is { } s)
            {
                var v = new LoopProvider(s);
                _activeVoices[key] = v;
                _mixer!.AddMixerInput(v);
            }
        }
    }

    /// <summary>松开琴键: 停该键的持续长音(短音乐器无操作)。</summary>
    public static void NoteOff(int key)
    {
        lock (_lock)
        {
            if (_activeVoices.TryGetValue(key, out var v)) { v.Stop = true; _activeVoices.Remove(key); }
        }
    }

    /// <summary>立即静音: 移除所有正在播放的音符。</summary>
    public static void StopAll()
    {
        lock (_lock) { _mixer?.RemoveAllMixerInputs(); }
    }

    /// <summary>当前选中的乐器名。</summary>
    public static string CurrentInstrument => _instrument;

    /// <summary>读取某乐器的移调(半音)。</summary>
    public static int GetOffset(string name) => PitchConfig.Get(name);

    /// <summary>设置某乐器移调并立即生效(清该乐器缓存, 按新音高重载)。</summary>
    public static void SetOffset(string name, int semitone)
    {
        PitchConfig.Set(name, semitone);   // 同步存值, 便于 UI 立即读回刷新
        System.Threading.Tasks.Task.Run(() =>   // 采样重载较重, 放后台
        {
            lock (_lock)
            {
                _cache.Remove(name);
                if (_out != null) Load(name);
            }
        });
    }

    /// <summary>清空所有音色缓存并重载当前乐器(音调重置后调用)。</summary>
    public static void ClearCache()
    {
        lock (_lock)
        {
            _cache.Clear();
            if (_out != null) Load(_instrument);
        }
    }
}
