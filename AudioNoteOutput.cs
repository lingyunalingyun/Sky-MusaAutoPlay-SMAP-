using SMAP_WPF.Core;

namespace SMAP_WPF;

public interface IAudioVoices
{
    void Start(Guid owner, Guid voice, int key, bool sustain, PreviewSettings? settings);
    void Release(Guid voice);
    void StopOwner(Guid owner);
}

public sealed class AudioNoteOutput : INoteOutput
{
    readonly Guid _owner = Guid.NewGuid();
    readonly PlaybackSequence _sequence;
    readonly IAudioVoices _audio;
    readonly Dictionary<Guid, Guid> _voices = new();
    public AudioNoteOutput(PlaybackSequence sequence, IAudioVoices? audio = null)
    {
        _sequence = sequence; _audio = audio ?? new EngineVoices();
        if (audio == null)
        {
            AudioEngine.Init();
            foreach (var settings in sequence.Preview.Values.Distinct()) AudioEngine.Prepare(settings.Instrument, settings.SemitoneOffset);
        }
    }
    public void Apply(IReadOnlyList<NoteOutputEvent> changes)
    {
        foreach (var change in changes)
            if (change.IsOn)
            {
                _sequence.Preview.TryGetValue(change.Note.TrackId, out var settings);
                var voice = Guid.NewGuid(); _voices[change.Note.NoteId] = voice;
                _audio.Start(_owner, voice, change.Note.Key, change.Note.Action == NoteAction.Hold, settings);
            }
            else if (_voices.Remove(change.Note.NoteId, out var voice)) _audio.Release(voice);
    }
    public void ReleaseAll() { foreach (var voice in _voices.Values) _audio.Release(voice); _voices.Clear(); }
    public void StopAll() { _audio.StopOwner(_owner); _voices.Clear(); }

    sealed class EngineVoices : IAudioVoices
    {
        public void Start(Guid owner, Guid voice, int key, bool sustain, PreviewSettings? settings) =>
            AudioEngine.StartVoice(owner, voice, key, sustain, settings?.Instrument, settings?.SemitoneOffset);
        public void Release(Guid voice) => AudioEngine.ReleaseVoice(voice);
        public void StopOwner(Guid owner) => AudioEngine.StopOwner(owner);
    }
}
