using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SMAP_WPF;

internal sealed class AudioVoiceMixer(MixingSampleProvider mixer)
{
    sealed class Voice(ISampleProvider source, Guid owner, Action? release) : ISampleProvider
    {
        public Guid Owner => owner;
        public volatile bool Finished;
        public volatile bool Stopped;
        public WaveFormat WaveFormat => source.WaveFormat;
        public void Release() => release?.Invoke();
        public int Read(float[] buffer, int offset, int count)
        {
            if (Stopped) { Finished = true; return 0; }
            int read = source.Read(buffer, offset, count);
            if (read < count) Finished = true;
            return read;
        }
    }

    readonly Dictionary<Guid, Voice> _voices = new();
    public int Count { get { Prune(); return _voices.Count; } }
    void Prune()
    {
        foreach (var id in _voices.Where(v => v.Value.Finished).Select(v => v.Key).ToArray()) _voices.Remove(id);
    }
    public void Start(Guid id, Guid owner, ISampleProvider source, Action? release = null)
    {
        Prune(); Stop(id);
        var voice = new Voice(source, owner, release);
        _voices.Add(id, voice);
        try { mixer.AddMixerInput(voice); }
        catch { _voices.Remove(id); voice.Stopped = true; throw; }
    }
    public void Release(Guid id) { if (_voices.TryGetValue(id, out var voice)) voice.Release(); }
    public void Stop(Guid id)
    {
        if (_voices.Remove(id, out var voice)) { voice.Stopped = true; mixer.RemoveMixerInput(voice); }
    }
    public void StopOwner(Guid owner)
    {
        foreach (var id in _voices.Where(v => v.Value.Owner == owner).Select(v => v.Key).ToArray()) Stop(id);
    }
    public void StopAll()
    {
        foreach (var voice in _voices.Values) voice.Stopped = true;
        _voices.Clear(); mixer.RemoveAllMixerInputs();
    }
}
