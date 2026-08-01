using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Midi;

namespace SMAP_WPF;

/// <summary>MIDI → 光遇 15 键曲谱。自动移调对齐 C 大调 + 黑键方向吸附, 移植自 JavaFX 版。</summary>
public class MidiImporter
{
    // 光遇 15 白键对应 MIDI 音高: C4 D4 E4 F4 G4 A4 B4 C5 D5 E5 F5 G5 A5 B5 C6
    static readonly int[] SkyMidi = { 60, 62, 64, 65, 67, 69, 71, 72, 74, 76, 77, 79, 81, 83, 84 };
    static readonly HashSet<int> WhitePc = new() { 0, 2, 4, 5, 7, 9, 11 };   // C 大调白键音级

    public readonly record struct TrackInfo(int Index, string Name, int NoteCount);

    readonly MidiFile _mf;
    readonly int _res;

    public MidiImporter(string path)
    {
        _mf = new MidiFile(path, false);
        _res = _mf.DeltaTicksPerQuarterNote;
    }

    public List<TrackInfo> AnalyzeTracks()
    {
        var result = new List<TrackInfo>();
        for (int i = 0; i < _mf.Events.Tracks; i++)
        {
            string? name = null;
            int count = 0;
            foreach (var ev in _mf.Events[i])
            {
                if (ev is TextEvent { MetaEventType: MetaEventType.SequenceTrackName } te)
                    name = te.Text?.Trim();
                if (ev is NoteOnEvent { Velocity: > 0 }) count++;
            }
            if (count > 0)
                result.Add(new TrackInfo(i, string.IsNullOrEmpty(name) ? $"Track {i}" : name!, count));
        }
        return result;
    }

    public double InitialBpm()
    {
        for (int i = 0; i < _mf.Events.Tracks; i++)
            foreach (var ev in _mf.Events[i])
                if (ev is TempoEvent tempo)
                    return 60_000_000.0 / tempo.MicrosecondsPerQuarterNote;
        return 120.0;
    }

    public List<(int key, double ms)> Convert(HashSet<int> trackIndices, int octaveShift, int semitoneShift)
    {
        var tempos = BuildTempoMap();
        var notes = new List<(int key, double ms)>();

        foreach (int ti in trackIndices)
        {
            if (ti < 0 || ti >= _mf.Events.Tracks) continue;
            int prevRaw = int.MinValue;   // 该轨上一个音(含移调), 方向感知吸附用
            foreach (var ev in _mf.Events[ti])
            {
                if (ev is not NoteOnEvent { Velocity: > 0 } on) continue;
                double ms = TickToMs(ev.AbsoluteTime, tempos);
                int raw = on.NoteNumber + octaveShift * 12 + semitoneShift;
                int dir = prevRaw == int.MinValue ? 0 : Math.Sign(raw - prevRaw);
                notes.Add((ToSkyKey(raw, dir), ms));
                prevRaw = raw;
            }
        }

        notes.Sort((a, b) => a.ms.CompareTo(b.ms));
        var seen = new HashSet<string>();
        notes.RemoveAll(n => !seen.Add($"{n.key}@{(long)n.ms}"));
        return notes;
    }

    // 收集选中音轨的全部 NOTE_ON 音高
    List<int> PitchesOf(HashSet<int> trackIndices)
    {
        var ps = new List<int>();
        foreach (int ti in trackIndices)
        {
            if (ti < 0 || ti >= _mf.Events.Tracks) continue;
            foreach (var ev in _mf.Events[ti])
                if (ev is NoteOnEvent { Velocity: > 0 } on) ps.Add(on.NoteNumber);
        }
        return ps;
    }

    /// <summary>自动检测最佳移调(半音,含八度): 先让最多音落白键, 再把中位音居中到 C5 减少八度折叠。</summary>
    public int SuggestShift(HashSet<int> trackIndices)
    {
        var ps = PitchesOf(trackIndices);
        if (ps.Count == 0) return 0;
        int bestS = 0, bestW = -1;
        for (int s = 0; s < 12; s++)
        {
            int w = ps.Count(p => WhitePc.Contains(((p + s) % 12 + 12) % 12));
            if (w > bestW) { bestW = w; bestS = s; }
        }
        if (bestS > 6) bestS -= 12;   // 规范到最小移动方向
        var sorted = ps.OrderBy(x => x).ToList();
        int median = sorted[sorted.Count / 2];
        int oct = (int)Math.Round((72f - (median + bestS)) / 12f);   // 中位音居中到 C5(72)
        return bestS + oct * 12;
    }

    /// <summary>给定移调后落在 C 大调白键上的音符比例 0~1。</summary>
    public double WhiteRatioAfter(HashSet<int> trackIndices, int semitoneShift)
    {
        var ps = PitchesOf(trackIndices);
        if (ps.Count == 0) return 0;
        int w = ps.Count(p => WhitePc.Contains(((p + semitoneShift) % 12 + 12) % 12));
        return (double)w / ps.Count;
    }

    // MIDI 音高 → 光遇 15 白键索引: 折叠八度进 C4~C6; 白键直用; 黑键按方向吸附(上行取上白键,否则下白键)
    static int ToSkyKey(int n, int dir)
    {
        while (n < SkyMidi[0]) n += 12;
        while (n > SkyMidi[^1]) n -= 12;
        if (!WhitePc.Contains((n % 12 + 12) % 12))
        {
            int cand = dir > 0 ? n + 1 : n - 1;
            if (cand < SkyMidi[0] || cand > SkyMidi[^1] || !WhitePc.Contains((cand % 12 + 12) % 12))
                cand = n + 1 <= SkyMidi[^1] ? n + 1 : n - 1;   // 边界回退
            n = cand;
        }
        for (int i = 0; i < SkyMidi.Length; i++) if (SkyMidi[i] == n) return i;
        return 0;
    }

    // 全轨 tempo 变化点(tick, 微秒/四分), 按 tick 排序, 缺省起点补 500000(=120BPM)
    List<(long tick, long mpq)> BuildTempoMap()
    {
        var tempos = new List<(long tick, long mpq)>();
        for (int i = 0; i < _mf.Events.Tracks; i++)
            foreach (var ev in _mf.Events[i])
                if (ev is TempoEvent te)
                    tempos.Add((te.AbsoluteTime, te.MicrosecondsPerQuarterNote));
        tempos.Sort((a, b) => a.tick.CompareTo(b.tick));
        if (tempos.Count == 0 || tempos[0].tick > 0) tempos.Insert(0, (0, 500_000));
        return tempos;
    }

    double TickToMs(long tick, List<(long tick, long mpq)> tempos)
    {
        long us = 0, prevTick = 0, mpq = 500_000;
        foreach (var (t, m) in tempos)
        {
            if (t >= tick) break;
            us += (t - prevTick) * mpq / _res;
            prevTick = t;
            mpq = m;
        }
        us += (tick - prevTick) * mpq / _res;
        return us / 1000.0;
    }
}
