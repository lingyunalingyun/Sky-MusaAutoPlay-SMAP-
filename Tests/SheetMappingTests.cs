using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class SheetMappingTests
{
    public static (CapturePixels frame,List<KeyLocation> keys) Fixture(InstrumentCapability c)
    {
        int w=1000,h=600;var bytes=new byte[w*h*4];
        for(int i=0;i<bytes.Length;i+=4){bytes[i]=140;bytes[i+1]=100;bytes[i+2]=55;bytes[i+3]=255;}
        var keys=new List<KeyLocation>();
        foreach(var slot in c.Slots.Where(s=>s.KeyId!=null))
        {
            int x=500-(c.Columns-1)*55+slot.Column*110,y=140+slot.Row*110;
            for(int yy=y-24;yy<y+24;yy++)for(int xx=x-24;xx<x+24;xx++)
            {int i=yy*w*4+xx*4;bytes[i]=bytes[i+1]=bytes[i+2]=75;int radius=(xx-x)*(xx-x)+(yy-y)*(yy-y);
                if(radius is >=100 and <=196){bytes[i]=190;bytes[i+1]=205;bytes[i+2]=225;}}
            keys.Add(new(slot.KeyId!,x/(double)w,y/(double)h,48.0/w,48.0/h));
        }
        return(new(bytes,w,h,w*4),keys);
    }
    static InstrumentCapability Cap(int count)=>count==4?InstrumentCapabilities.CustomFour():InstrumentCapabilities.ForInstrument(count==8?"Handpan":"Piano")!;
    static SheetCalibration Cal(InstrumentCapability c)=>new(){Confirmed=true,Width=1000,Height=600,Dpi=96,
        CapabilityFingerprint=SheetCalibration.Fingerprint(c),BindingFingerprint="bindings",Keys=Fixture(c).keys};
    [Theory][InlineData(15)][InlineData(8)][InlineData(4)]
    public void DetectsCompleteLayoutWithoutInventingAdditionalKeys(int count)
    {
        var c=Cap(count);var f=Fixture(c);var d=SheetKeyDetector.Detect(f.frame,c);
        Assert.Equal(count,d.Keys.Count);Assert.True(d.Confidence>=SheetKeyDetector.MinimumConfidence);
        Assert.True(SheetKeyDetector.Stable(d,SheetKeyDetector.Detect(f.frame,c)));
        Assert.All(d.Keys,k=>Assert.Contains(c.Keys,p=>p.Id==k.KeyId));
    }
    [Theory][InlineData(15,8)][InlineData(15,4)][InlineData(8,4)]
    public void LargerGridCannotBeAcceptedAsSmallerInstrument(int actual,int expected)
    {var d=SheetKeyDetector.Detect(Fixture(Cap(actual)).frame,Cap(expected));Assert.Empty(d.Keys);}
    [Fact]public void NoKeyboardAndCorruptPixelsFailExplicitly()
    {
        var c=Cap(15);Assert.Empty(SheetKeyDetector.Detect(new(new byte[1000*600*4],1000,600,4000),c).Keys);
        Assert.Throws<ArgumentException>(()=>SheetKeyDetector.Detect(new(new byte[4],1000,600,4000),c));
    }
    [Theory][InlineData(15)][InlineData(8)][InlineData(4)]
    public void ManualCalibrationRetainsAllPhysicalIdentitiesAndRejectsDuplicateCenters(int count)
    {
        var c=Cap(count);var keys=Fixture(c).keys;Assert.True(SheetCalibration.ValidLocations(c,keys));
        var bad=keys.ToList();bad[1]=bad[1] with{X=bad[0].X,Y=bad[0].Y};Assert.False(SheetCalibration.ValidLocations(c,bad));
    }
    [Theory][InlineData("closed")][InlineData("focus")][InlineData("dpi")][InlineData("size")][InlineData("reuse")][InlineData("occluded")]
    public void WindowChangesHideHintsAndInvalidateOnlyWhatChanged(string change)
    {
        var c=Cap(8);var target=new MappingWindowSnapshot(1,2,3,1000,600,96,true);var current=change switch{
            "closed"=>null,"focus"=>target with{Foreground=false},"dpi"=>target with{Dpi=144},"size"=>target with{Width=1200},
            "reuse"=>target with{ProcessStarted=4},_=>target with{Obscured=true}};
        var state=MappingWindowPolicy.Evaluate(target,current,Cal(c),c,"bindings");Assert.False(state.ShowHints);
        Assert.Equal(change is "dpi" or "size",state.NeedsCalibration);
        Assert.True(MappingWindowPolicy.Evaluate(target,target,Cal(c),c,"bindings").ShowHints);
    }
    [Fact]public void ChangingBindingsOrLayoutInvalidatesSavedCalibration()
    {
        var c=Cap(8);var saved=Cal(c);Assert.True(saved.Matches(c,"bindings",1000,600,96));
        Assert.False(saved.Matches(c,"new",1000,600,96));Assert.False(saved.Matches(c with{Version=2},"bindings",1000,600,96));
    }
    [Fact]public void VisualContextChangeDetectedForManualCalibration()
    {
        var c=Cap(4);var f=Fixture(c);var original=SheetKeyDetector.Signatures(f.frame,f.keys);
        Assert.True(SheetKeyDetector.SameVisualContext(original,SheetKeyDetector.Signatures(f.frame,f.keys)));
        Assert.False(SheetKeyDetector.SameVisualContext(original,SheetKeyDetector.Signatures(new(new byte[1000*600*4],1000,600,4000),f.keys)));
    }
    [Theory][InlineData(PracticeMode.Follow)][InlineData(PracticeMode.Rhythm)]
    public void MappingUsesPracticeJudgmentsAndRejectsStaleInputWithoutAnOutputDependency(PracticeMode mode)
    {
        var p=TestProject.ChangeNote(TestProject.SingleTap(),n=>n with{Key=1,Action=NoteAction.Hold,LengthBeats=2});var clock=new ManualClock();
        var practice=new SheetPractice(PlaybackSequence.FromProject(p),clock,mode);practice.Context(true);practice.Resume();
        long old=practice.Generation;practice.Input(1,true,old);Assert.Single(practice.Tick().Holds);Assert.False(practice.Session.IsCompleted);
        practice.Context(false);clock.Milliseconds=100;practice.Input(1,false,old);Assert.True(practice.Session.Paused);
        practice.Context(true);practice.Resume();practice.Input(1,true,old);Assert.True(practice.Tick().Holds[0].AwaitingPress);
        practice.Input(1,true,practice.Generation);clock.Milliseconds=1100;practice.Input(1,false,practice.Generation);
        Assert.True(practice.Session.IsCompleted);
        Assert.DoesNotContain(typeof(SheetPractice).GetConstructors().SelectMany(c=>c.GetParameters()),p=>typeof(IKeyInput).IsAssignableFrom(p.ParameterType)||typeof(INoteOutput).IsAssignableFrom(p.ParameterType));
    }
    [Fact]public void LegacyEightBindingUsesLocalKeyIdentityAndReleasesExactPhysicalKey()
    {
        var c=Cap(8);var p=TestProject.ChangeNote(TestProject.SingleTap(),n=>n with{Key=7});p=p with{Tracks=new(){p.Tracks[0] with{GameTarget=GamePitchProfiles.Get("Handpan").Target()}}};
        var resolve=GameOutputBindings.Resolve(p,cap=>PerformanceBindingProfile.FromLegacy(cap,SkyPlayer.DefaultVk));
        var clock=new ManualClock();var input=new RecordingInput(clock);var game=new GameNoteOutput(input,resolve);var sequence=PlaybackSequence.FromProject(p);
        var session=new PlaybackSession(sequence,clock,new[]{game});session.Pump();Assert.Contains((ushort)0x4B,input.Pressed);session.Stop();Assert.Empty(input.Pressed);
    }
    [Fact]public void SwitchingFifteenToEightKeepsOriginalKeysAndSupportsUndo()
    {
        var p=TestProject.Sample();var s=new EditorSession(p){AllowUnsupportedEditing=true};var before=s.ActiveTrack.Notes.ToArray();
        s.UpdateTrack(InstrumentCapabilities.WithTarget(s.ActiveTrack,GamePitchProfiles.Get("Handpan")));
        Assert.Equal(before,s.ActiveTrack.Notes);Assert.Equal(NoteKeyDomain.StableLogical,s.ActiveTrack.KeyDomain);
        Assert.Equal(KeyAvailability.Unavailable,PerformanceCompatibility.Evaluate(s.ActiveTrack,before[1]).Availability);
        s.Undo();Assert.Null(s.ActiveTrack.Capability);Assert.Equal(before,s.ActiveTrack.Notes);
    }
    [Fact]public void SparseMidiRemapPreservesManualChangesAndSourceBytes()
    {
        var source=new MidiFixture().Note(50,0,480).Note(57,480,960).Read();var options=MidiFixture.Options(source);
        options=options.Select(o=>o with{Profile=GamePitchProfiles.Get("Handpan")}).ToArray();
        var p=MidiProjectConverter.Create(SapmMapper.Convert(source,options));var s=new EditorSession(p){AllowUnsupportedEditing=true};
        s.UpdateTrack(InstrumentCapabilities.WithTarget(s.ActiveTrack,GamePitchProfiles.Get("Handpan")));
        var n=s.ActiveTrack.Notes[0];s.PutNotes(new[]{n with{Beat=6}});
        var next=MidiProjectConverter.Remap(s.Project,options);var plan=s.PreviewRemap(next);
        Assert.Equal(6,plan.Project.Tracks[0].Notes.Single(x=>x.Id==n.Id).Beat);Assert.Equal(p.OriginalMidi,plan.Project.OriginalMidi);
    }
    [Fact]public void StableMidiDomainDoesNotMistakeUntouchedKeysForManualEdits()
    {
        var source=new MidiFixture().Note(50,0,480).Note(57,480,960).Read();
        var options=MidiFixture.Options(source).Select(o=>o with{Profile=GamePitchProfiles.Get("Handpan")}).ToArray();
        var p=MidiProjectConverter.Create(SapmMapper.Convert(source,options));var cap=InstrumentCapabilities.ForInstrument("Handpan")!;
        p=p with{FormatVersion=2,KeyInterpretationVersion=1,Tracks=p.Tracks.Select(t=>t with{Capability=cap,KeyDomain=NoteKeyDomain.StableLogical,
            Notes=t.Notes.Select(n=>n with{Key=cap.Resolve(n.Key,NoteKeyDomain.LegacyOrdinal)!.LogicalKey}).ToList()}).ToList()};
        var s=new EditorSession(p);var first=s.ActiveTrack.Notes[0];s.PutNotes(new[]{first with{Beat=5}});
        var plan=s.PreviewRemap(MidiProjectConverter.Remap(s.Project,options));Assert.Equal(1,plan.PreservedSources);
        Assert.Equal(5,plan.Project.Tracks[0].Notes.Single(n=>n.Id==first.Id).Beat);
        using var files=new TestProject();SmapFile.Save(plan.Project,files.PathFor("stable.smap"));
        var loaded=SmapFile.Load(files.PathFor("stable.smap"));Assert.Equal(p.OriginalMidi,loaded.OriginalMidi);
    }
    [Fact]public void CapabilityOnlyTrackCannotBeSilentlyFlattenedToLegacyFormat()
    {
        var p=TestProject.SingleTap();p=p with{FormatVersion=2,KeyInterpretationVersion=1,Tracks=new(){p.Tracks[0] with{Capability=CapabilityTests.Four(),KeyDomain=NoteKeyDomain.StableLogical}}};
        Assert.Throws<InvalidOperationException>(()=>LegacyProjectAdapter.ToDocument(p));
    }
    [Fact]public void BlankCaptureCannotBeAcceptedAsManualCalibrationBackground()
        =>Assert.False(new CapturePixels(new byte[100*100*4],100,100,400).HasContent());
    [Theory][InlineData(15)][InlineData(8)][InlineData(4)]
    public void GlyphFallbackWorksWhenTransparentKeyBackgroundIsNotNeutral(int count)
    {
        var c=Cap(count);var fixture=Fixture(c);var bytes=fixture.frame.Bgra;
        foreach(var k in fixture.keys)
        {
            int x=(int)(k.X*1000),y=(int)(k.Y*600);
            for(int yy=y-24;yy<y+24;yy++)for(int xx=x-24;xx<x+24;xx++)
            {
                int p=yy*4000+xx*4;bytes[p]=30;bytes[p+1]=65;bytes[p+2]=40;
                int radius=(xx-x)*(xx-x)+(yy-y)*(yy-y);
                if(radius is >=196 and <=324){bytes[p]=170;bytes[p+1]=190;bytes[p+2]=205;}
            }
        }
        var detected=SheetKeyDetector.Detect(fixture.frame,c);Assert.Equal(count,detected.Keys.Count);
        Assert.All(detected.Keys,k=>{var expected=fixture.keys.Single(p=>p.KeyId==k.KeyId);Assert.InRange(Math.Abs(k.X-expected.X),0,.004);Assert.InRange(Math.Abs(k.Y-expected.Y),0,.004);});
    }
    [Fact]public void RhythmClockDoesNotAdvanceWhileWaitingForGameFocus()
    {
        var clock=new ManualClock();var practice=new SheetPractice(PlaybackSequence.FromProject(TestProject.SingleTap()),clock,PracticeMode.Rhythm);
        clock.Milliseconds=5000;practice.Tick();Assert.Equal(0,practice.Session.PositionMs);Assert.True(practice.Session.Paused);
        practice.Context(true);practice.Resume();Assert.Equal(0,practice.Session.PositionMs);
    }
    [Theory][InlineData(15)][InlineData(8)][InlineData(4)]
    public void PinkGlyphsOnColoredBackgroundRetainCompleteGridAndCenters(int count)
    {
        var c=Cap(count);var f=Fixture(c);
        for(int p=0;p<f.frame.Bgra.Length;p+=4){f.frame.Bgra[p]=80;f.frame.Bgra[p+1]=140;f.frame.Bgra[p+2]=40;}
        for(int index=0;index<f.keys.Count;index++)
        {
            var k=f.keys[index];int x=(int)(k.X*1000),y=(int)(k.Y*600);
            for(int yy=y-26;yy<=y+26;yy++)for(int xx=x-26;xx<=x+26;xx++)
            {
                int radius=(xx-x)*(xx-x)+(yy-y)*(yy-y),diamond=Math.Abs(xx-x)+Math.Abs(yy-y);
                bool outline=index%2==0?radius is >=196 and <=324:diamond is >=21 and <=25;
                if(!outline)continue;int p=yy*4000+xx*4;f.frame.Bgra[p]=210;f.frame.Bgra[p+1]=180;f.frame.Bgra[p+2]=245;
            }
        }
        var detected=SheetKeyDetector.Detect(f.frame,c);Assert.Equal(count,detected.Keys.Count);
        Assert.All(detected.Keys,k=>{var expected=f.keys.Single(p=>p.KeyId==k.KeyId);Assert.InRange(Math.Abs(k.X-expected.X),0,.004);Assert.InRange(Math.Abs(k.Y-expected.Y),0,.004);});
    }
    [Theory][InlineData(15,false,15)][InlineData(8,false,8)][InlineData(4,false,4)][InlineData(15,true,0)][InlineData(8,true,0)][InlineData(4,true,0)]
    public void BrightBackgroundRequiresEveryRealKeyOutline(int count,bool missingKey,int expectedCount)
    {
        var c=Cap(count);var f=Fixture(c);
        for(int p=0;p<f.frame.Bgra.Length;p+=4){f.frame.Bgra[p]=205;f.frame.Bgra[p+1]=212;f.frame.Bgra[p+2]=218;}
        for(int index=0;index<f.keys.Count;index++)
        {
            if(missingKey&&index==c.Columns+1)continue;
            var k=f.keys[index];int x=(int)(k.X*1000),y=(int)(k.Y*600);
            for(int yy=y-26;yy<=y+26;yy++)for(int xx=x-26;xx<=x+26;xx++)
            {
                int radius=(xx-x)*(xx-x)+(yy-y)*(yy-y),diamond=Math.Abs(xx-x)+Math.Abs(yy-y);
                if(!(index%2==0?radius is >=196 and <=324:diamond is >=21 and <=25))continue;
                int p=yy*4000+xx*4;f.frame.Bgra[p]=f.frame.Bgra[p+1]=f.frame.Bgra[p+2]=250;
            }
        }
        var result=SheetKeyDetector.Detect(f.frame,c);Assert.Equal(expectedCount,result.Keys.Count);
        Assert.Equal(!missingKey,SheetKeyDetector.VerifyLocations(f.frame,c,f.keys));
        Assert.All(result.Keys,k=>{var expected=f.keys.Single(p=>p.KeyId==k.KeyId);Assert.InRange(Math.Abs(k.X-expected.X),0,.004);Assert.InRange(Math.Abs(k.Y-expected.Y),0,.004);});
    }
    [Fact]public void MissingCalibrationDoesNotClaimThatDpiChanged()
    {
        var c=Cap(15);var current=new MappingWindowSnapshot(1,2,3,1000,600,96,true);
        var state=MappingWindowPolicy.Evaluate(current,current,null,c,"bindings");Assert.False(state.ShowHints);Assert.True(state.NeedsCalibration);Assert.Equal("uncalibrated",state.Reason);
    }
    [Fact]public void ExistingGameFocusIsDistinctFromClosedAndCoveredWindows()
    {
        var c=Cap(15);var window=new MappingWindowSnapshot(1,2,3,1000,600,96,true);
        var focus=MappingWindowPolicy.Evaluate(window,window with{Foreground=false},Cal(c),c,"bindings");
        Assert.False(focus.ShowHints);Assert.False(focus.NeedsCalibration);Assert.Equal("focus",focus.Reason);
        Assert.Equal("covered",MappingWindowPolicy.Evaluate(window,window with{Obscured=true},Cal(c),c,"bindings").Reason);
        Assert.Equal("inactive",MappingWindowPolicy.Evaluate(window,null,Cal(c),c,"bindings").Reason);
    }
    [Fact]public void BrightGridUsesCompleteAnchorDespiteAnUnrelatedShapeInTheFirstRow()
    {
        var c=Cap(15);var f=Fixture(c);
        for(int p=0;p<f.frame.Bgra.Length;p+=4){f.frame.Bgra[p]=205;f.frame.Bgra[p+1]=212;f.frame.Bgra[p+2]=218;}
        void Outline(int x,int y)
        {
            for(int yy=y-18;yy<=y+18;yy++)for(int xx=x-18;xx<=x+18;xx++)
            {
                int radius=(xx-x)*(xx-x)+(yy-y)*(yy-y);if(radius is <196 or >324)continue;
                int p=yy*4000+xx*4;f.frame.Bgra[p]=f.frame.Bgra[p+1]=f.frame.Bgra[p+2]=250;
            }
        }
        foreach(var k in f.keys)Outline((int)(k.X*1000),(int)(k.Y*600));Outline(80,140);
        int firstX=(int)(f.keys[c.Columns].X*1000),secondX=(int)(f.keys[c.Columns+1].X*1000),rowY=(int)(f.keys[c.Columns].Y*600);
        for(int yy=rowY-1;yy<=rowY+1;yy++)for(int xx=firstX+16;xx<=secondX-16;xx++)
        {int p=yy*4000+xx*4;f.frame.Bgra[p]=f.frame.Bgra[p+1]=f.frame.Bgra[p+2]=250;}
        var result=SheetKeyDetector.Detect(f.frame,c);Assert.Equal(15,result.Keys.Count);
        Assert.All(result.Keys,k=>{var expected=f.keys.Single(p=>p.KeyId==k.KeyId);Assert.InRange(Math.Abs(k.X-expected.X),0,.004);Assert.InRange(Math.Abs(k.Y-expected.Y),0,.004);});
        Assert.Empty(SheetKeyDetector.Detect(f.frame,Cap(8)).Keys);Assert.Empty(SheetKeyDetector.Detect(f.frame,Cap(4)).Keys);
    }
    [Fact]public void OcclusionPreservesOpaqueWindowsButIgnoresClickThroughLayeredHints()
    {
        Assert.False(GameWindows.BlocksHints(0x800A8));Assert.False(GameWindows.BlocksHints(0x82800A8));
        Assert.True(GameWindows.BlocksHints(0x100));Assert.True(GameWindows.BlocksHints(0x80000));Assert.True(GameWindows.BlocksHints(0x20));
    }
}
