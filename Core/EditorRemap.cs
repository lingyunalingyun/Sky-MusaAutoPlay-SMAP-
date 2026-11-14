namespace SMAP_WPF.Core;

public sealed record EditorDifference(Guid TrackId, Guid NoteId, ProjectNote? Before, ProjectNote? After);
public sealed record EditorRemapPlan(SmapProject Project, IReadOnlyList<EditorDifference> Differences, int PreservedSources);

public sealed partial class EditorSession
{
    public EditorRemapPlan PreviewRemap(SmapProject remapped, bool preserveManual = true)
    {
        var conversion = _project.MidiConversion ?? throw new InvalidOperationException("NoMidiSource");
        if (remapped.MidiConversion?.Source.Sha256 != conversion.Source.Sha256 || remapped.ProjectId != _project.ProjectId)
            throw new InvalidOperationException("MidiSourceChanged");
        var source = conversion.Source.Notes.ToDictionary(n => n.Id);
        var linked = _project.Tracks.SelectMany(t => t.Notes).Where(n => n.SourceMidiNoteId != null).GroupBy(n => n.SourceMidiNoteId!.Value).ToDictionary(g => g.Key, g => g.ToArray());
        var edited = new HashSet<Guid>();
        var baselineCapabilities=conversion.Voices.ToDictionary(v=>v.VoiceId,v=>InstrumentCapabilities.FromProfile(v.Profile));
        var currentTracks=_project.Tracks.ToDictionary(t=>t.Id);
        foreach (var mapping in conversion.Notes.Where(n => n.Key != null))
        {
            var original = source[mapping.SourceId];
            if (!linked.TryGetValue(mapping.SourceId, out var values) || values.Length != 1) { edited.Add(mapping.SourceId); continue; }
            var n = values[0]; decimal duration = conversion.Source.Beat(original.EndTick - original.StartTick);
            var currentTrack=currentTracks[n.TrackId];
            int expected=currentTrack.KeyDomain==NoteKeyDomain.StableLogical
                ? baselineCapabilities[mapping.VoiceId].Resolve(mapping.Key!.Value,NoteKeyDomain.LegacyOrdinal)!.LogicalKey : mapping.Key!.Value;
            if (n.Key != expected || n.Beat != conversion.Source.Beat(original.StartTick) || n.LengthBeats != duration
                || n.Action != (duration > 0 ? NoteAction.Hold : NoteAction.Tap)) edited.Add(mapping.SourceId);
        }
        var tracks = new List<ProjectTrack>();
        foreach (var mappedTrack in remapped.Tracks)
        {
            var current = _project.Tracks.SingleOrDefault(t => t.Id == mappedTrack.Id);
            // 被用户删除的原声部保持删除；新增声部仍可从映射结果显式加入。
            if (current == null && preserveManual && conversion.Voices.Any(v => MidiSource.StableId($"{conversion.Source.Sha256}:track:{v.VoiceId}") == mappedTrack.Id)) continue;
            var capability=current?.Capability==null?null:InstrumentCapabilities.ForTrack(mappedTrack);
            var domain=current?.KeyDomain??NoteKeyDomain.LegacyOrdinal;
            var notes = mappedTrack.Notes.Where(n => !preserveManual || n.SourceMidiNoteId is not { } id || !edited.Contains(id))
                .Select(n=>domain==NoteKeyDomain.StableLogical && capability!=null
                    ?n with{Key=capability.Resolve(n.Key,NoteKeyDomain.LegacyOrdinal)!.LogicalKey}:n).ToList();
            if (current != null)
                notes.AddRange(current.Notes.Where(n => n.SourceMidiNoteId == null || (preserveManual && edited.Contains(n.SourceMidiNoteId.Value))));
            var track = mappedTrack with
            {
                Notes = notes,
                Capability=capability,KeyDomain=domain,
                Name = current?.Name ?? mappedTrack.Name,
                Solo = current?.Solo ?? mappedTrack.Solo,
                PreviewInstrument = preserveManual && current != null ? current.PreviewInstrument : mappedTrack.PreviewInstrument,
                PreviewSemitoneOffset = preserveManual && current != null ? current.PreviewSemitoneOffset : mappedTrack.PreviewSemitoneOffset
            };
            // 保留人工键位；新工程在演奏前单独检查目标兼容性。
            tracks.Add(track);
        }
        tracks.AddRange(_project.Tracks.Where(t => !remapped.Tracks.Any(r => r.Id == t.Id)));
        bool tempoEdited = !_project.Tempos.SequenceEqual(conversion.Source.Tempos.Select(t => new TempoPoint(conversion.Source.Beat(t.Tick), t.MicrosecondsPerQuarter)));
        bool meterEdited = !_project.Meters.SequenceEqual(conversion.Source.Meters.Select(m => new MeterPoint(conversion.Source.Beat(m.Tick), m.Numerator, m.Denominator)));
        var result = remapped with
        {
            Tracks = tracks,
            FormatVersion=Math.Max(_project.FormatVersion,tracks.Any(t=>t.Capability!=null)?2:1),
            KeyInterpretationVersion=_project.FormatVersion>=2||tracks.Any(t=>t.Capability!=null)?1:0,
            Metadata = _project.Metadata,
            GridSubdiv = _project.GridSubdiv,
            Tempos = preserveManual && tempoEdited ? _project.Tempos : remapped.Tempos,
            Meters = preserveManual && meterEdited ? _project.Meters : remapped.Meters
        };
        ProjectValidation.Validate(result);
        var old = _project.Tracks.SelectMany(t => t.Notes).ToDictionary(n => n.Id);
        var next = result.Tracks.SelectMany(t => t.Notes).ToDictionary(n => n.Id);
        var differences = old.Keys.Union(next.Keys).Select(id => new EditorDifference((next.GetValueOrDefault(id) ?? old[id]).TrackId, id, old.GetValueOrDefault(id), next.GetValueOrDefault(id)))
            .Where(d => d.Before != d.After).ToArray();
        return new(result, differences, preserveManual ? edited.Count : 0);
    }
}
