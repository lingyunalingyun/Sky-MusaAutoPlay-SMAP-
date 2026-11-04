using System.Globalization;
using System.Windows;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public partial class MidiImportDialog
{
    void IncludePart(int index, bool included)
    {
        if (_loading) return;
        _options[index] = _options[index] with { Included = included }; Dirty();
    }
    void LoadEditor()
    {
        _loading = true; var v = _options[_selected];
        _instrument.SelectedValue = v.Profile.Instrument; _key.SelectedValue = v.Profile.Tonic; _mode.SelectedValue = v.Mode;
        if (_advanced) { _auto.IsChecked = v.AutoTranspose; _semitone.Text = v.SemitoneShift.ToString(CultureInfo.InvariantCulture); _octave.Text = v.OctaveShift.ToString(CultureInfo.InvariantCulture); }
        _profileInfo.Text = ProfileDescription(v.Profile);
        if (_voiceCaption != null) _voiceCaption.Text = _source.Voices[_selected].Name;
        _formDirty = false; _loading = false;
    }
    bool ReadEditor()
    {
        if (_loading || !_formDirty) return true;
        if (_instrument.SelectedValue is not string instrument || _key.SelectedValue is not int tonic || _mode.SelectedValue is not SapmMode mode) return false;
        int semitone = 0, octave = 0;
        if (_advanced && (!int.TryParse(_semitone.Text, out semitone) || semitone is < -48 or > 48 || !int.TryParse(_octave.Text, out octave) || octave is < -8 or > 8))
        { _status.Text = S("shiftInvalid"); return false; }
        var selected = _options[_selected];
        var profile = selected.Profile.Instrument == instrument && selected.Profile.Tonic == tonic ? selected.Profile : GamePitchProfiles.Get(instrument, tonic);
        bool allow = selected.AllowUnverified;
        if (profile.Verification == PitchVerification.ConflictingSources && !allow)
        {
            if (!MsgBox.Choice(this, S("conflict"), S("target"), Lang.S("d.ok"), Lang.S("d.cancel"))) { LoadEditor(); return false; }
            allow = true;
        }
        for (int i = 0; i < _options.Count; i++) if ((_advanced && i == _selected) || (!_advanced && _options[i].Included))
        {
            var old = _options[i];
            if (old.Profile.KeyCount != profile.KeyCount) old = old with
            { ManualKeys = old.ManualKeys.ToDictionary(p => p.Key, p => p.Value >= profile.KeyCount ? null : p.Value) };
            _options[i] = old with { Profile = profile, Mode = mode, AutoTranspose = !_advanced || _auto.IsChecked == true,
                SemitoneShift = semitone, OctaveShift = octave, AllowUnverified = allow };
        }
        _profileInfo.Text = ProfileDescription(profile); _formDirty = false; return true;
    }
    static string ProfileDescription(GamePitchProfile profile) => F("profile", profile.Version, S("verification." + profile.Verification),
        profile.KeySource.StartsWith("sheet:", StringComparison.Ordinal) ? profile.KeySource[6..] : S("keyGuide"));
    void FormChanged()
    {
        if (_loading) return;
        try { _formDirty = true; if (ReadEditor()) Dirty(); }
        catch (Exception error) { LoadEditor(); ShowError(error); }
    }
    void Dirty()
    {
        _dirty = true; _result = null; _import.IsEnabled = false; InvalidatePlans(); _debounce.Stop(); _debounce.Start();
    }
    void SetBusy(bool busy)
    {
        _busy = busy; _work.IsEnabled = !busy; _toggle.IsEnabled = !busy; _import.IsEnabled = !busy && _result != null && !_dirty;
        _status.Text = busy ? S("calculating") : "";
    }
    SapmVoiceOptions[] Snapshot() => _options.Select(v => v with
    { Profile = v.Profile with { Pitches = v.Profile.Pitches.ToList(), Sources = v.Profile.Sources.ToList() }, ManualKeys = new(v.ManualKeys) }).ToArray();

    async Task Recalculate()
    {
        if (_closed || _busy || !ReadEditor()) return;
        _debounce.Stop(); StopAudition(); SetBusy(true); var options = Snapshot(); var scope = _scope;
        try
        {
            var result = await Task.Run(() => SapmMapper.Convert(_source, options, scope, _token), _token);
            if (_closed) return;
            _result = result; _dirty = false; _mappedPlan = null; RefreshResult();
        }
        catch (Exception error) { ShowError(error); }
        finally { if (!_closed) SetBusy(false); }
    }
    void RefreshResult()
    {
        _diagnostics.Text = _source.Issues.Count == 0 ? S("clean") : string.Join("\n", _source.Issues.Select(i =>
            $"{Lang.S("midi3.issue." + i.Code)} · Track {i.Track + 1} / Ch {i.Channel} / Tick {i.Tick} ×{i.Count}"));
        if (_result?.Quality is not { } q) return;
        _quality.Text = F("counts", q.Total, q.Exact, q.Octave, q.Approximate, q.Unplayable, q.Excluded) + "\n"
            + F("range", Pitch(q.OriginalLow), Pitch(q.OriginalHigh), Pitch(q.TargetLow), Pitch(q.TargetHigh)) + "\n"
            + F("metrics", q.IntervalPairs == 0 ? (object)"—" : q.MelodyFidelity, q.HarmonyPairs == 0 ? (object)"—" : q.HarmonyFidelity,
                q.IntervalPairs, q.HarmonyPairs, q.MeanAbsolutePitchError, q.MeanIntervalError);
        if (_mapping != null)
        {
            var originals = _source.Notes.ToDictionary(n => n.Id); var timeline = _source.Timeline();
            int optionIndex = _selected;
            string selectedVoice = _options[_selected].VoiceId;
            _mapping.ItemsSource = _result.Notes.Where(n => n.VoiceId == selectedVoice).Select(n =>
                new MappingRow(n, (double)(timeline.BeatToMicroseconds(_source.Beat(originals[n.SourceId].StartTick)) / 1000000), _options[_selected].Profile.KeyCount, originals[n.SourceId].Channel == 10,
                    key => { _options[optionIndex].ManualKeys[n.SourceId] = key; Dirty(); })).ToArray();
        }
    }
    sealed class MappingRow(MappedMidiNote note, double time, int keyCount, bool percussion, Action<int?> change)
    {
        int? _key = note.Key + 1;
        public string Time => time.ToString("0.000", CultureInfo.CurrentCulture);
        public string Original => percussion ? $"GM {note.OriginalPitch}" : Pitch(note.OriginalPitch);
        public string Final => Pitch(note.TargetPitch);
        public string Reason => S("cause." + note.Reason.Split(';')[0]);
        public string Detail => F("noteDetail", note.OriginalPitch, note.RequestedPitch, note.TargetPitch?.ToString(CultureInfo.CurrentCulture) ?? "—", note.RequestedPitch - note.OriginalPitch);
        public int? Key
        {
            get => _key;
            set
            {
                if (value is { } k && (k < 1 || k > keyCount)) throw new ArgumentOutOfRangeException(nameof(value));
                if (_key != value) { _key = value; change(value - 1); }
            }
        }
    }
    async Task CompareScopes()
    {
        if (_busy || !ReadEditor()) return;
        StopAudition(); SetBusy(true); var options = Snapshot();
        try
        {
            var pair = await Task.Run(() => (whole: SapmMapper.Convert(_source, options, cancellationToken: _token).Quality!, independent: SapmMapper.Convert(_source, options, SapmScope.IndependentVoices, _token).Quality!), _token);
            if (!_closed) MsgBox.Info(this, F("scopeResult", pair.whole.MelodyFidelity, pair.whole.HarmonyFidelity, pair.independent.MelodyFidelity, pair.independent.HarmonyFidelity), S("compare"));
        }
        catch (Exception error) { ShowError(error); }
        finally { if (!_closed) SetBusy(false); }
    }
    void InvalidatePlans()
    {
        if (_loading) return;
        StopAudition(); _originalPlan = null; _mappedPlan = null; _planRegion = "";
    }
    internal static bool TryTime(string text, out double milliseconds)
    {
        milliseconds = 0; string[] parts = text.Trim().Split(':');
        if (parts.Length is < 1 or > 3) return false;
        double seconds = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            if (!(double.TryParse(parts[i], NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
                || double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out value)) || !double.IsFinite(value) || value < 0
                || (parts.Length > 1 && i > 0 && value >= 60) || (i < parts.Length - 1 && value != Math.Truncate(value))) return false;
            seconds = seconds * 60 + value;
        }
        milliseconds = seconds * 1000; return double.IsFinite(milliseconds);
    }
    async Task Preview(bool original)
    {
        if (_busy) return;
        if (_dirty || _result == null) await Recalculate();
        if (_closed || _result == null) return;
        if (!TryTime(_from.Text, out double start) || (!string.IsNullOrWhiteSpace(_to.Text) && !TryTime(_to.Text, out _)))
        { _status.Text = S("regionInvalid"); return; }
        double? end = string.IsNullOrWhiteSpace(_to.Text) ? null : TryTime(_to.Text, out var e) ? e : null;
        if (end is { } last && last <= start) { _status.Text = S("regionInvalid"); return; }
        StopAudition(); SetBusy(true);
        try
        {
            string[] voices = _solo.IsChecked == true ? new[] { _options[_selected].VoiceId } : _options.Where(v => v.Included).Select(v => v.VoiceId).ToArray();
            string region = FormattableString.Invariant($"{start:R}:{end:R}:{string.Join('|', voices)}");
            if (_planRegion != region) { _originalPlan = null; _mappedPlan = null; _planRegion = region; }
            if (!original && _mappedPlan == null)
            {
                var project = await Task.Run(() => MidiProjectConverter.Create(_result));
                if (_closed) return; _mappedPlan = MidiAudition.Mapped(project, voices, start, end);
            }
            if (original && _originalPlan == null) _originalPlan = MidiAudition.Original(_source, voices, start, end);
            var plan = original ? _originalPlan! : _mappedPlan!;
            await Task.Run(plan.PrepareSamples); if (_closed) return;
            plan.Play(_audition, 1, () => { });
            SetBusy(false); _status.Text = original ? S("referenceNotice") : plan.Capabilities.Any(c => c.Contains("Fallback", StringComparison.Ordinal) || c.Contains("Unavailable", StringComparison.Ordinal)) ? S("fallback") : S("samples");
        }
        catch (Exception error) { ShowError(error); if (!_closed) SetBusy(false); }
    }
    void StopAudition() { try { _audition.Stop(); } catch (Exception error) { Logger.Exception("MIDI 试听停止失败", error); } }
    void ShowError(Exception error)
    {
        Logger.Exception("MIDI 转换失败", error);
        if (!_closed) { _status.Text = S("error"); MsgBox.Info(this, S("error"), S("title")); }
    }
    async Task Import()
    {
        if (_busy) return;
        if (_dirty || _result == null) await Recalculate();
        if (_result?.Quality is not { } q || _closed) return;
        if (_result.Notes.All(n => n.Key == null)) { MsgBox.Info(this, Lang.S("midi.noresult"), S("title")); return; }
        StopAudition();
        if ((q.Octave + q.Approximate + q.Unplayable > 0 || _source.Issues.Any(i => i.Severity == MidiIssueSeverity.Warning))
            && !MsgBox.Choice(this, S("lossy"), S("quality"), S(_previous == null ? "import" : "saveVersion"), Lang.S("d.cancel"))) return;
        string name = string.IsNullOrWhiteSpace(_nameBox.Text) ? Lang.S("midi.defname") : _nameBox.Text.Trim(); SetBusy(true);
        try
        {
            var project = await Task.Run(() => MidiProjectConverter.Create(_result, name, _previous));
            if (!_closed) { ResultProject = project; DialogResult = true; }
        }
        catch (Exception error) { ShowError(error); }
        finally { if (!_closed) SetBusy(false); }
    }
    protected override void OnClosed(EventArgs e)
    {
        _closed = true; _debounce.Stop(); _cancellation.Cancel(); _cancellation.Dispose();
        try { _audition.Dispose(); } finally { base.OnClosed(e); }
    }
}
