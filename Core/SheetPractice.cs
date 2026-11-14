namespace SMAP_WPF.Core;

public sealed record HoldHint(int Key, double RemainingMs, bool Release, bool AwaitingPress, double TotalMs=0);
public sealed record PracticeHintSnapshot(IReadOnlyList<int> Current, IReadOnlyList<int> Next, IReadOnlyList<HoldHint> Holds,
    PracticeJudgment? LastJudgment, bool Paused, bool Completed);

public sealed class SheetPractice
{
    readonly IMonotonicClock _clock;
    double _judgmentAt;
    public PracticeSession Session { get; }
    public long Generation { get; private set; }
    public bool Ready { get; private set; }
    public SheetPractice(PlaybackSequence sequence, IMonotonicClock clock, PracticeMode mode)
    { _clock=clock;Session=new(sequence,clock,mode);Session.Pause(true);Session.Judged+=_=>_judgmentAt=clock.Milliseconds; }
    public void Context(bool ready)
    {
        if (Ready==ready) return;
        Ready=ready; Generation++; Session.Pause(true);
    }
    public void Resume() { if(Ready){Generation++;Session.Pause(false);} }
    public void Pause() { Generation++; Session.Pause(true); }
    public void Input(int key, bool down, long generation)
    {
        if (!Ready || generation!=Generation || Session.Paused)return;
        if(down)Session.Press(key);else Session.Release(key);
    }
    public PracticeHintSnapshot Tick()
    {
        Session.Tick(); var hints=Session.Hints();return _clock.Milliseconds-_judgmentAt>500?hints with{LastJudgment=null}:hints;
    }
}
