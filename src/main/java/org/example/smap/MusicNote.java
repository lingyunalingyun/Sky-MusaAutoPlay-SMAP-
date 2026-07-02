package org.example.smap;

public class MusicNote {
    private String key;
    private long absoluteTime;

    public MusicNote(String key, long absoluteTime) {
        this.key = key;
        this.absoluteTime = absoluteTime;
    }

    public String getKey() { return key; }
    public long getAbsoluteTime() { return absoluteTime; }

    public void setKey(String key) { this.key = key; }
    public void setAbsoluteTime(long absoluteTime) { this.absoluteTime = absoluteTime; }
}
