using SMAP_WPF.Core;
using System.IO;
using System.Windows.Threading;

namespace SMAP_WPF;

public partial class MainWindow
{
    PlaybackSequence _sequence = PlaybackSequence.Empty;
    SmapProject? _midiProject;
    PracticeSession? _practiceSession;
    readonly IMonotonicClock _practiceClock = new StopwatchClock();
    long _playVersion;
    long _practiceInputGeneration;
    readonly HashSet<string> _acceptedGameCapabilities = new();
    internal Func<IKeyInput> GameInputFactory {get;set;} = ()=>new WindowsKeyInput();

    static bool IsNative(SongInfo? song) => song != null && Path.GetExtension(song.File).Equals(".smap", StringComparison.OrdinalIgnoreCase);
    bool TryRemapMidiProject(SongInfo? song)
    {
        if (!IsNative(song)) return false;
        try
        {
            var project = SmapFile.Load(song!.File);
            if (project.MidiConversion == null) return false;
            if (_playing || _previewing) StopPlaying();
            if (_gamePracticeOn) GamePracticeSwitch.IsChecked = false;
            var dialog = new MidiImportDialog(project) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.ResultProject is { } result)
            {
                var review=new EditorRemapDialog(new EditorSession(project){AllowUnsupportedEditing=true},result){Owner=this};
                if(review.ShowDialog()==true && review.Plan is {} plan){SongLibrary.WriteNative(plan.Project);RefreshLibrary();}
            }
        }
        catch (Exception error)
        { Logger.Exception("MIDI 工程重新映射失败", error); ShowToast(Lang.S("midi3.error")); }
        return true;
    }
    bool RequireLegacyDocument(SongInfo? song)
    {
        if (!IsNative(song)) return true;
        ShowToast(Lang.S("native.playOnly")); return false;
    }
    internal static INoteOutput CreatePreviewOutput(PlaybackSequence sequence, SmapProject? project,
        IAudioVoices? samples = null, IMidiAuditionVoices? mapped = null)
        => project != null && sequence.Preview.Count > 0 ? new EditorAudioOutput(project, samples, mapped) : new AudioNoteOutput(sequence, samples);

    void PlaySequence(bool preview, double startMs = 0)
    {
        if (_mapping != null) { ResetPlayUi(); ShowToast(Lang.S("map.readonly")); return; }
        long version = ++_playVersion;
        _practiceSession?.Pause(true);
        AudioEngine.StopManual();
        _player.Failed = error => Dispatcher.BeginInvoke(() =>
        {
            if (version != _playVersion) return;
            Logger.Exception("演奏输出失败", error); ResetPlayUi(); ShowToast(Lang.S("playback.failed"));
        });
        try
        {
            _player.Stop();
            INoteOutput output;
            if (preview)
            {
                output = CreatePreviewOutput(_sequence, _midiProject);
                if (_midiProject != null && _sequence.Preview.Count > 0 && MidiAudition.Edited(_midiProject).Capabilities.Any(c => c.Contains("Fallback", StringComparison.Ordinal) || c.Contains("Unavailable", StringComparison.Ordinal)))
                    ShowToast(Lang.S("midi3.fallback"));
            }
            else if (!preview && _midiProject is { } project && project.AudibleTracks().Any(t => t.GameTarget != null || t.Capability != null))
            {
                PerformanceCompatibility.RequirePlayable(project, allowUnknown:true);
                var unknown=project.AudibleTracks().Where(t=>t.Notes.Any(n=>PerformanceCompatibility.Evaluate(t,n).Availability==KeyAvailability.Unknown)).ToArray();
                string fingerprint=PerformanceIdentity.ForProject(project);
                if(unknown.Length>0 && !_acceptedGameCapabilities.Contains(fingerprint))
                {
                    if(!MsgBox.Choice(this,Lang.S("map.unknownConfirm"),Lang.S("map.title"),Lang.S("d.ok"),Lang.S("d.cancel"))){ResetPlayUi();return;}
                    _acceptedGameCapabilities.Add(fingerprint);
                }
                output=new GameNoteOutput(GameInputFactory(),GameOutputBindings.Resolve(project,c=>_inputs.For(c,_player.Vk)));
            }
            else output = preview ? new AudioNoteOutput(_sequence) : new GameNoteOutput(GameInputFactory(), _player.Vk);
            _player.Play(_sequence, _speed, () => Dispatcher.BeginInvoke(() =>
            {
                if (version == _playVersion) OnPlayDone();
            }), output, startMs);
        }
        catch (Exception error) { _player.Failed(error); }
    }

    void ResetPracticeSession(double? startMs = null)
    {
        _practiceInputGeneration++;
        AudioEngine.StopManual();
        _practiceSession = new(_sequence, _practiceClock, _metroOn ? PracticeMode.Rhythm : PracticeMode.Follow,
            IsNative(_practiceSong) ? 0 : 20) { Speed = _speed };
        _practiceSession.Judged += judgment =>
        {
            string? key = judgment.Kind switch
            {
                PracticeJudgmentKind.Started => "practice.holding",
                PracticeJudgmentKind.EarlyRelease => "practice.earlyRelease",
                PracticeJudgmentKind.LateRelease => "practice.lateRelease",
                PracticeJudgmentKind.Missed => "practice.missed",
                _ => null
            };
            if (key != null) StatusText.Text = Lang.S(key);
        };
        if (startMs is { } ms) _practiceSession.Seek(ms);
        _practiceSession.Pause(_practicePaused || _playing || _previewing);
        _flashTimer ??= CreateFlashTimer(); _flashTimer.Start();
    }

    void TickPractice()
    {
        if (_practiceSession == null || _playing || _previewing) return;
        _practiceSession.Tick(); SyncPracticeSession();
    }

    void SyncPracticeSession()
    {
        if (_practiceSession == null || _practiceSteps.Count == 0) return;
        if (_practiceSession.IsCompleted)
        {
            ShowToast(Lang.S("t.practiceDone")); ResetPracticeSession();
        }
        var session = _practiceSession!;
        double time = session.CurrentStepIndex < session.Steps.Count
            ? (double)(session.Steps[session.CurrentStepIndex].StartMicroseconds / 1000) : _practiceTotalMs;
        _practiceStep = NearestStep(time);
        RenderPracticeHints(); UpdateProgUi();
    }

    void PracticeRelease(int key)
    {
        _practiceHeld.Remove(key);
        if (_playing || _previewing || _practicePaused) return;
        _practiceSession?.Release(key); SyncPracticeSession();
    }

    void SeekPracticeSession()
    {
        _practiceInputGeneration++;
        if (_practiceStep < _practiceStepMs.Count)
            _practiceSession?.Seek(_practiceStepMs[_practiceStep]);
        AudioEngine.StopManual();
    }
}
