using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed class EditorAudioOutput : INoteOutput
{
    readonly INoteOutput _mapped, _samples;
    readonly HashSet<Guid> _mappedIds;
    public EditorAudioOutput(SmapProject project, IAudioVoices? sampleVoices = null, IMidiAuditionVoices? mappedVoices = null)
    {
        var sequence = PlaybackSequence.FromProject(project);
        var tracks = project.Tracks.Where(t => t.GameTarget is { KeyMidiPitches.Count: > 0 }).Select(t => t.Id).ToHashSet();
        _mappedIds = sequence.Notes.Where(n => tracks.Contains(n.TrackId)).Select(n => n.NoteId).ToHashSet();
        var plan = MidiAudition.Edited(project); if (mappedVoices == null) plan.PrepareSamples(); _mapped = plan.CreateOutput(mappedVoices);
        var legacy = sequence.Notes.Where(n => !tracks.Contains(n.TrackId)).ToArray();
        _samples = legacy.Length == 0 ? new SilentOutput() : new AudioNoteOutput(new PlaybackSequence(legacy, sequence.Preview), sampleVoices);
    }
    public void Apply(IReadOnlyList<NoteOutputEvent> changes)
    {
        var mapped = changes.Where(c => _mappedIds.Contains(c.Note.NoteId)).ToArray();
        var samples = changes.Where(c => !_mappedIds.Contains(c.Note.NoteId)).ToArray();
        if (mapped.Length > 0) _mapped.Apply(mapped);
        if (samples.Length > 0) _samples.Apply(samples);
    }
    public void ReleaseAll() { try { _mapped.ReleaseAll(); } finally { _samples.ReleaseAll(); } }
    public void StopAll() { try { _mapped.StopAll(); } finally { _samples.StopAll(); } }
    sealed class SilentOutput : INoteOutput
    {
        public void Apply(IReadOnlyList<NoteOutputEvent> changes) { }
        public void StopAll() { }
    }
}
