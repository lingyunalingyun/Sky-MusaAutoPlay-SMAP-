using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class PreviewTailTests
{
    sealed class Tone(bool sustain):ISampleProvider
    {
        int _remaining=8820;
        public bool Released;
        public WaveFormat WaveFormat {get;}=WaveFormat.CreateIeeeFloatWaveFormat(44100,2);
        public int Read(float[] buffer,int offset,int count)
        {
            int n=sustain&&!Released?count:Math.Min(count,_remaining);
            if(!sustain||Released)_remaining-=n;Array.Fill(buffer,.25f,offset,n);return n;
        }
    }
    sealed class Voices:IAudioVoices,IMidiAuditionVoices
    {
        readonly MixingSampleProvider _mix=new(WaveFormat.CreateIeeeFloatWaveFormat(44100,2)){ReadFully=true};
        readonly AudioVoiceMixer _voices;
        public Voices()=>_voices=new(_mix);
        public void Start(Guid owner,Guid id,int key,bool sustain,PreviewSettings? settings)=>Start(owner,id,sustain);
        public void Start(Guid owner,Guid id,AuditionTone tone,bool sustain)=>Start(owner,id,sustain);
        void Start(Guid owner,Guid id,bool sustain)
        {var tone=new Tone(sustain);_voices.Start(id,owner,tone,sustain?()=>tone.Released=true:null);}
        public void Release(Guid id)=>_voices.Release(id);
        public void StopOwner(Guid owner)=>_voices.StopOwner(owner);
        public float[] Read(){var data=new float[128];_mix.Read(data,0,data.Length);return data;}
        public void Drain(){for(int i=0;i<200;i++)Read();}
        public int Count=>_voices.Count;
    }
    [Fact]public void FinalTapCompletesSchedulingWithoutCuttingItsSample()
    {
        var sequence=new PlaybackSequence(new[]{PlaybackFixture.Note(0,0)});var voices=new Voices();var clock=new ManualClock();
        var session=new PlaybackSession(sequence,clock,new[]{new AudioNoteOutput(sequence,voices)});session.Pump();
        clock.Milliseconds=18;session.Pump();Assert.True(session.Completed);
        Assert.Contains(voices.Read(),n=>n!=0);voices.Drain();Assert.Equal(0,voices.Count);
        session.Stop();Assert.All(voices.Read(),n=>Assert.Equal(0,n));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void PauseReleasesAudioWithoutHardCutAndExplicitStopStillClears(bool mapped)
    {
        var p=TestProject.ChangeNote(TestProject.SingleTap(),n=>n with{Action=NoteAction.Hold,LengthBeats=4});
        if(mapped)p=p with{Tracks=new(){p.Tracks[0] with{GameTarget=GamePitchProfiles.Get("Flute").Target(),PreviewInstrument="Flute"}}};
        var sequence=PlaybackSequence.FromProject(p);var voices=new Voices();var clock=new ManualClock();
        INoteOutput output=mapped?new EditorAudioOutput(p,voices,voices):new AudioNoteOutput(sequence,voices);
        var session=new PlaybackSession(sequence,clock,new[]{output});session.Pump();clock.Milliseconds=100;session.Pause(true);
        Assert.True(session.Paused);Assert.Contains(voices.Read(),n=>n!=0);voices.Drain();Assert.Equal(0,voices.Count);
        session.Pause(false);Assert.Contains(voices.Read(),n=>n!=0);session.Stop();Assert.All(voices.Read(),n=>Assert.Equal(0,n));
    }
    [Fact]public void MappedFinalTapRetainsNaturalTail()
    {
        var p=TestProject.SingleTap();p=p with{Tracks=new(){p.Tracks[0] with{GameTarget=GamePitchProfiles.Get("Piano").Target()}}};
        var sequence=PlaybackSequence.FromProject(p);var voices=new Voices();var clock=new ManualClock();
        var session=new PlaybackSession(sequence,clock,new[]{new EditorAudioOutput(p,voices,voices)});session.Pump();clock.Milliseconds=18;session.Pump();
        Assert.True(session.Completed);Assert.Contains(voices.Read(),n=>n!=0);session.Stop();Assert.Equal(0,voices.Count);
    }
}
