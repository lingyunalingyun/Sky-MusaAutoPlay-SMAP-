package org.example.smap;

import javax.sound.sampled.AudioFormat;
import javax.sound.sampled.AudioInputStream;
import javax.sound.sampled.AudioSystem;
import javax.sound.sampled.Clip;

import java.io.BufferedInputStream;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;

/**
 * 加载 Sky Studio 提取的乐器 wav (resources/instruments/<name>/0..14.wav).
 * 每键 POLYPHONY 个 Clip 复音池, round-robin 播放, 解决同键快速重按丢音.
 * 通过 setInstrument(name) 切换音色 (Piano / Harp / Flute / ...).
 */
public class ToneGenerator {

    public static final String[] INSTRUMENTS = {
            "Piano", "Harp", "Guitar", "Flute", "Ukulele",
            "Winter Piano", "Xylophone", "Electric Guitar", "Bassoon", "Orff"
    };

    private static final String RES_PATH = "/org/example/smap/instruments/";
    private static final int KEYS = 15;
    private static final int POLYPHONY = 4;

    private static final Clip[][] CLIPS = new Clip[KEYS][POLYPHONY];
    private static final int[] cursor = new int[KEYS];
    private static volatile String currentInstrument = "Piano";
    private static volatile boolean initialized = false;

    public static synchronized void init() {
        if (initialized) return;
        loadInstrument(currentInstrument);
        initialized = true;
    }

    public static synchronized void setInstrument(String name) {
        if (name == null) return;
        if (name.equals(currentInstrument) && initialized) return;
        disposeAll();
        currentInstrument = name;
        initialized = false; // 懒加载: 下次 play() / init() 时才载入
    }

    public static String getInstrument() {
        return currentInstrument;
    }

    private static void disposeAll() {
        for (int i = 0; i < KEYS; i++) {
            for (int p = 0; p < POLYPHONY; p++) {
                Clip c = CLIPS[i][p];
                if (c != null) { try { c.close(); } catch (Exception ignore) {} }
                CLIPS[i][p] = null;
            }
            cursor[i] = 0;
        }
    }

    private static void loadInstrument(String name) {
        String folder = RES_PATH + name + "/";
        for (int i = 0; i < KEYS; i++) {
            byte[] pcm;
            AudioFormat fmt;
            try {
                Loaded l = loadPcm(folder, i);
                if (l == null) continue;
                pcm = l.pcm;
                fmt = l.fmt;
            } catch (Exception e) {
                System.err.println("[ToneGenerator] load " + folder + i + " failed: " + e);
                continue;
            }
            for (int p = 0; p < POLYPHONY; p++) {
                try {
                    Clip c = AudioSystem.getClip();
                    c.open(fmt, pcm, 0, pcm.length);
                    CLIPS[i][p] = c;
                } catch (Exception e) {
                    System.err.println("[ToneGenerator] open clip " + i + "/" + p + ": " + e);
                }
            }
        }
    }

    public static void play(int index) {
        if (!initialized) init();
        if (index < 0 || index >= KEYS) return;
        int slot = cursor[index];
        cursor[index] = (slot + 1) % POLYPHONY;
        Clip c = CLIPS[index][slot];
        if (c == null) return;
        if (c.isRunning()) {
            for (int p = 0; p < POLYPHONY; p++) {
                Clip alt = CLIPS[index][(slot + p) % POLYPHONY];
                if (alt != null && !alt.isRunning()) { c = alt; break; }
            }
        }
        synchronized (c) {
            c.stop();
            c.setFramePosition(0);
            c.start();
        }
    }

    public static void stopAll() {
        for (Clip[] row : CLIPS) {
            for (Clip c : row) {
                if (c != null && c.isRunning()) c.stop();
            }
        }
    }

    private record Loaded(AudioFormat fmt, byte[] pcm) {}

    private static Loaded loadPcm(String folder, int index) throws Exception {
        String resource = folder + index + ".wav";
        try (InputStream raw = ToneGenerator.class.getResourceAsStream(resource);
             BufferedInputStream buf = raw == null ? null : new BufferedInputStream(raw);
             AudioInputStream ais = buf == null ? null : AudioSystem.getAudioInputStream(buf)) {
            if (ais == null) {
                System.err.println("[ToneGenerator] missing: " + resource);
                return null;
            }
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] tmp = new byte[8192];
            int n;
            while ((n = ais.read(tmp)) > 0) out.write(tmp, 0, n);
            return new Loaded(ais.getFormat(), out.toByteArray());
        }
    }
}
