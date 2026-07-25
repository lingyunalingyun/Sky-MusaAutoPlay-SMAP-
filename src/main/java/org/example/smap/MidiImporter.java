package org.example.smap;

import javax.sound.midi.*;
import java.io.File;
import java.util.*;

public class MidiImporter {

    // Sky C major scale: C4 D4 E4 F4 G4 A4 B4 C5 D5 E5 F5 G5 A5 B5 C6
    private static final int[] SKY_MIDI = {
        60, 62, 64, 65, 67, 69, 71, 72, 74, 76, 77, 79, 81, 83, 84
    };

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
            for (int i = 0; i < track.size(); i++) {
                MidiEvent ev = track.get(i);
                if (ev.getMessage() instanceof ShortMessage sm
                        && sm.getCommand() == ShortMessage.NOTE_ON
                        && sm.getData2() > 0) {
                    long ms = ppq
                            ? tickToMs(ev.getTick(), resolution, tempos)
                            : (long) (ev.getTick() * 1000.0 / (fps * resolution));
                    int skyKey = toSkyKey(sm.getData1(), octaveShift);
                    notes.add(new MusicNote("1Key" + skyKey, ms));
                }
            }
        }

        notes.sort(Comparator.comparingLong(MusicNote::getAbsoluteTime));
        Set<String> seen = new HashSet<>();
        notes.removeIf(n -> !seen.add(n.getKey() + "@" + n.getAbsoluteTime()));
        return notes;
    }

    private static int toSkyKey(int midiNote, int octaveShift) {
        int n = midiNote + octaveShift * 12;
        while (n < SKY_MIDI[0]) n += 12;
        while (n > SKY_MIDI[SKY_MIDI.length - 1]) n -= 12;
        int best = 0, bestDist = Math.abs(n - SKY_MIDI[0]);
        for (int i = 1; i < SKY_MIDI.length; i++) {
            int d = Math.abs(n - SKY_MIDI[i]);
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
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
