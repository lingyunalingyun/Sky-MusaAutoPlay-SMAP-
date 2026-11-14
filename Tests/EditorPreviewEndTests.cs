using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class EditorPreviewEndTests
{
    [Theory][InlineData(6.75,0,4000)][InlineData(8,0,6000)][InlineData(3,1,2000)][InlineData(3,2,4000)]
    public void PreviewStopsAtTheLastOccupiedBarBoundary(double beat,double length,double expected)
    {
        var p=TestProject.ChangeNote(TestProject.SingleTap(),n=>n with{Beat=(decimal)beat,Action=length==0?NoteAction.Tap:NoteAction.Hold,LengthBeats=(decimal)length});
        var raw=PlaybackSequence.FromProject(p);var preview=EditorWindow.PreviewSequence(p);
        Assert.Equal(expected,preview.DurationMs);Assert.Equal(raw.Notes,preview.Notes);Assert.Equal(length==0?NoteAction.Tap:NoteAction.Hold,preview.Notes[0].Action);
    }
    [Fact]public void MeterAndTempoChangesUseTheSameBoundariesAsTheRuler()
    {
        var p=TestProject.ChangeNote(TestProject.SingleTap(),n=>n with{Beat=5.5m});
        p=p with{Meters=new(){new(0,4,4),new(6,3,8)},Tempos=new(){new(0,500000),new(4,1000000)}};
        Assert.Equal(4000,EditorWindow.PreviewSequence(p).DurationMs);
        p=TestProject.ChangeNote(p,n=>n with{Beat=6.25m});Assert.Equal(5500,EditorWindow.PreviewSequence(p).DurationMs);
    }
    [Fact]public void GridChangesDoNotChangeTapDurationOrThePreviewBar()
    {
        var p=TestProject.ChangeNote(TestProject.SingleTap(),n=>n with{Beat=3.99m});var preview=EditorWindow.PreviewSequence(p);
        Assert.Equal(2000,preview.DurationMs);Assert.Equal(preview.DurationMs,EditorWindow.PreviewSequence(p with{GridSubdiv=64}).DurationMs);
        Assert.Equal(0,preview.Notes[0].LengthMicroseconds);Assert.Equal(1995,PlaybackSequence.FromProject(p).DurationMs);
    }
    [Fact]public void MutedTracksAndEmptyProjectsDoNotExtendPlayback()
    {
        var p=TestProject.SingleTap();var muted=new ProjectTrack{Muted=true};muted=muted with{Notes=new(){new(){TrackId=muted.Id,Key=1,Beat=100}}};
        Assert.Equal(2000,EditorWindow.PreviewSequence(p with{Tracks=new(){p.Tracks[0],muted}}).DurationMs);
        Assert.Equal(0,EditorWindow.PreviewSequence(p with{Tracks=new(){p.Tracks[0] with{Notes=new()}}}).DurationMs);
    }
    [Fact]public void SilentRemainderAdvancesWithoutKeepingGameKeysDown()
    {
        var raw=new PlaybackSequence(new[]{PlaybackFixture.Note(0,1000)});var sequence=new PlaybackSequence(raw.Notes,raw.Preview,2000000);
        var clock=new ManualClock();var input=new RecordingInput(clock);var game=new GameNoteOutput(input,SkyPlayer.DefaultVk);
        var session=new PlaybackSession(sequence,clock,new[]{game});clock.Milliseconds=1000;session.Pump();Assert.Single(input.Pressed);
        clock.Milliseconds=1018;session.Pump();Assert.Empty(input.Pressed);Assert.False(session.Completed);Assert.Equal(982,session.NextDelayMilliseconds);
        clock.Milliseconds=1999;session.Pump();Assert.False(session.Completed);clock.Milliseconds=2000;session.Pump();Assert.True(session.Completed);Assert.Equal(2000,session.PositionMs);
    }
    [Fact]public void PaddingRespectsLiveSpeedAndCannotTruncateAnExistingHold()
    {
        var note=PlaybackFixture.Note(0,0,1000);var longer=new PlaybackSequence(new[]{note},minimumDurationMicroseconds:500000);
        Assert.Equal(1000,longer.DurationMs);
        var padded=new PlaybackSequence(new[]{PlaybackFixture.Note(0,0)},minimumDurationMicroseconds:2000000);var clock=new ManualClock();var game=new GameNoteOutput(new RecordingInput(clock),SkyPlayer.DefaultVk);
        var session=new PlaybackSession(padded,clock,new[]{game});session.Pump();clock.Milliseconds=500;session.Pump();session.SetSpeed(2);
        clock.Milliseconds=1249;session.Pump();Assert.False(session.Completed);clock.Milliseconds=1250;session.Pump();Assert.True(session.Completed);
    }
}
