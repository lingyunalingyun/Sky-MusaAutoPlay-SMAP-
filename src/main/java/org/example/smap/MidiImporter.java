package org.example.smap;

import javax.sound.midi.*;
import java.io.File;
import java.util.*;

public class MidiImporter {

    // Sky C major scale: C4 D4 E4 F4 G4 A4 B4 C5 D5 E5 F5 G5 A5 B5 C6
    private static final int[] SKY_MIDI = {
        60, 62, 64, 65, 67, 69, 71, 72, 74, 76, 77, 79, 81, 83, 84
    };
    // C 大调白键的音级(pitch class)
    private static final Set<Integer> WHITE_PC = Set.of(0, 2, 4, 5, 7, 9, 11);

    public record TrackInfo(int index, String name, int noteCount) {}

    private final Sequence seq;

    public MidiImporter(File file) throws Exception {
        this.seq = MidiSystem.getSequence(file);
    }

    public List<TrackInfo> analyzeTracks() {
        List<TrackInfo> result = new ArrayList<>();
        Track[] tracks = seq.getTracks();
        for (int i = 0; i < tracks.length; i++) {
            Track t = tracks[i];
            String name = null;
            int noteCount = 0;
            for (int j = 0; j < t.size(); j++) {
                MidiMessage msg = t.get(j).getMessage();
                if (msg instanceof MetaMessage meta && meta.getType() == 0x03) {
                    name = new String(meta.getData()).trim();
                }
                if (msg instanceof ShortMessage sm
                        && sm.getCommand() == ShortMessage.NOTE_ON
                        && sm.getData2() > 0) {
                    noteCount++;
                }
            }
            if (noteCount > 0) {
                result.add(new TrackInfo(i,
                        name != null && !name.isEmpty() ? name : "Track " + i,
                        noteCount));
            }
        }
        return result;
    }

    public double getInitialBpm() {
        for (Track t : seq.getTracks()) {
            for (int i = 0; i < t.size(); i++) {
                if (t.get(i).getMessage() instanceof MetaMessage meta && meta.getType() == 0x51) {
                    byte[] d = meta.getData();
                    long mpq = ((d[0] & 0xFF) << 16) | ((d[1] & 0xFF) << 8) | (d[2] & 0xFF);
                    return 60_000_000.0 / mpq;
                }
            }
        }
        return 120.0;
    }

    public List<MusicNote> convert(Set<Integer> trackIndices, int octaveShift) {
        return convert(trackIndices, octaveShift, 0);
    }

    public List<MusicNote> convert(Set<Integer> trackIndices, int octaveShift, int semitoneShift) {
        int resolution = seq.getResolution();
        boolean ppq = seq.getDivisionType() == Sequence.PPQ;

        List<long[]> tempos = new ArrayList<>();
        if (ppq) {
            for (Track t : seq.getTracks()) {
                for (int i = 0; i < t.size(); i++) {
                    MidiEvent ev = t.get(i);
                    if (ev.getMessage() instanceof MetaMessage meta && meta.getType() == 0x51) {
                        byte[] d = meta.getData();
                        long mpq = ((d[0] & 0xFF) << 16) | ((d[1] & 0xFF) << 8) | (d[2] & 0xFF);
                        tempos.add(new long[]{ev.getTick(), mpq});
                    }
                }
            }
            tempos.sort(Comparator.comparingLong(a -> a[0]));
            if (tempos.isEmpty() || tempos.get(0)[0] > 0) {
                tempos.add(0, new long[]{0, 500_000});
            }
        }

        float fps = ppq ? 0 : seq.getDivisionType();
        List<MusicNote> notes = new ArrayList<>();
        Track[] tracks = seq.getTracks();

        for (int ti : trackIndices) {
            if (ti < 0 || ti >= tracks.length) continue;
            Track track = tracks[ti];
            int prevRaw = Integer.MIN_VALUE; // 该轨上一个音(含移调), 用于方向感知吸附
            for (int i = 0; i < track.size(); i++) {
                MidiEvent ev = track.get(i);
                if (ev.getMessage() instanceof ShortMessage sm
                        && sm.getCommand() == ShortMessage.NOTE_ON
                        && sm.getData2() > 0) {
                    long ms = ppq
                            ? tickToMs(ev.getTick(), resolution, tempos)
                            : (long) (ev.getTick() * 1000.0 / (fps * resolution));
                    int raw = sm.getData1() + octaveShift * 12 + semitoneShift;
                    int dir = (prevRaw == Integer.MIN_VALUE) ? 0 : Integer.compare(raw, prevRaw);
                    int skyKey = toSkyKey(raw, dir);
                    notes.add(new MusicNote("1Key" + skyKey, ms));
                    prevRaw = raw;
                }
            }
        }

        notes.sort(Comparator.comparingLong(MusicNote::getAbsoluteTime));
        Set<String> seen = new HashSet<>();
        notes.removeIf(n -> !seen.add(n.getKey() + "@" + n.getAbsoluteTime()));
        return notes;
    }

    /** 收集选中音轨的全部 NOTE_ON 音高。 */
    private List<Integer> pitchesOf(Set<Integer> trackIndices) {
        List<Integer> ps = new ArrayList<>();
        Track[] tracks = seq.getTracks();
        for (int ti : trackIndices) {
            if (ti < 0 || ti >= tracks.length) continue;
            Track t = tracks[ti];
            for (int i = 0; i < t.size(); i++) {
                if (t.get(i).getMessage() instanceof ShortMessage sm
                        && sm.getCommand() == ShortMessage.NOTE_ON && sm.getData2() > 0)
                    ps.add(sm.getData1());
            }
        }
        return ps;
    }

    /**
     * 自动检测最佳移调(半音, 含八度)。
     * 先选让最多音落在 C 大调白键上的音级对齐(最小移动), 再把中位音居中到 C5 附近以减少八度折叠。
     */
    public int suggestShift(Set<Integer> trackIndices) {
        List<Integer> ps = pitchesOf(trackIndices);
        if (ps.isEmpty()) return 0;
        int bestS = 0, bestW = -1;
        for (int s = 0; s < 12; s++) {
            int w = 0;
            for (int p : ps) if (WHITE_PC.contains(((p + s) % 12 + 12) % 12)) w++;
            if (w > bestW) { bestW = w; bestS = s; }
        }
        if (bestS > 6) bestS -= 12; // 规范到最小移动方向
        List<Integer> sorted = new ArrayList<>(ps);
        Collections.sort(sorted);
        int median = sorted.get(sorted.size() / 2);
        int oct = Math.round((72f - (median + bestS)) / 12f); // 中位音居中到 C5(72)
        return bestS + oct * 12;
    }

    /** 给定移调(半音)后, 落在 C 大调白键上的音符比例 0~1。 */
    public double whiteRatioAfter(Set<Integer> trackIndices, int semitoneShift) {
        List<Integer> ps = pitchesOf(trackIndices);
        if (ps.isEmpty()) return 0;
        int w = 0;
        for (int p : ps) if (WHITE_PC.contains(((p + semitoneShift) % 12 + 12) % 12)) w++;
        return (double) w / ps.size();
    }

    /**
     * 把 MIDI 音高映射到光遇 15 白键(索引 0~14)。
     * 先折叠八度进 C4~C6; 白键直接用; 黑键按旋律方向吸附:
     * 上行→上方白键, 下行/首音→下方白键(经过音听感更顺, 不再机械最近邻忽高忽低)。
     * @param n   已含 octaveShift 的 MIDI 音高
     * @param dir 相对上一个音的方向: >0 上行, <0 下行, 0 首音/同音
     */
    private static int toSkyKey(int n, int dir) {
        while (n < SKY_MIDI[0]) n += 12;
        while (n > SKY_MIDI[SKY_MIDI.length - 1]) n -= 12;
        if (!WHITE_PC.contains(((n % 12) + 12) % 12)) {
            int cand = (dir > 0) ? n + 1 : n - 1; // 上行取上白键, 否则取下白键
            if (cand < SKY_MIDI[0] || cand > SKY_MIDI[SKY_MIDI.length - 1]
                    || !WHITE_PC.contains(((cand % 12) + 12) % 12)) {
                cand = (n + 1 <= SKY_MIDI[SKY_MIDI.length - 1]) ? n + 1 : n - 1; // 边界回退
            }
            n = cand;
        }
        for (int i = 0; i < SKY_MIDI.length; i++) if (SKY_MIDI[i] == n) return i;
        return 0; // 理论不达
    }

    private static long tickToMs(long tick, int resolution, List<long[]> tempos) {
        long us = 0, prevTick = 0, mpq = 500_000;
        for (long[] tc : tempos) {
            if (tc[0] >= tick) break;
            us += (tc[0] - prevTick) * mpq / resolution;
            prevTick = tc[0];
            mpq = tc[1];
        }
        us += (tick - prevTick) * mpq / resolution;
        return us / 1000;
    }
}
