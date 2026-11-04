using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public partial class EditorWindow
{
    readonly TextBlock[] _keyLabels=new TextBlock[15];
    readonly Border[] _keyDiamonds=new Border[15];
    readonly HashSet<int> _keyHeld=new();
    readonly List<NoteOutputEvent> _keyPending=new();
    Dictionary<int,TimedProjectNote> _keyNotes=new();
    InputPreferences _keyPreferences=new();ushort[] _keyLegacy=SkyPlayer.DefaultVk;
    ushort?[] _keyBindings=new ushort?[15];
    INoteOutput? _keyOutput;SmapProject? _keyPreviewProject;
    int _keyGeneration;bool _keyPreparing;string _keySignature="";
    internal Func<SmapProject,INoteOutput> KeyOutputFactory {get;set;}=p=>new EditorAudioOutput(p);
    internal static ushort?[] EditorBindings(ProjectTrack track,InputPreferences inputs,IReadOnlyList<ushort> legacy)
    {
        var c=InstrumentCapabilities.ForTrack(track)??(track.GameTarget==null?InstrumentCapabilities.ForInstrument("Piano"):null);var result=new ushort?[15];
        if(c==null)return result;
        var profile=inputs.For(c,legacy);
        foreach(var key in c.Keys)result[c.NoteKey(key,track.KeyDomain)]=profile.Keys[key.Id];
        return result;
    }
    void BuildKeyboard()
    {
        if(_persistView){_keyPreferences=InputPreferences.Load();_keyLegacy=KeyConfig.Load();}
        for(int i=0;i<15;i++)
        {
            int key=i;var label=new TextBlock{FontSize=12,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            var diamond=new Border{Width=21,Height=21,CornerRadius=new CornerRadius(2),BorderThickness=new Thickness(1.5),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,RenderTransformOrigin=new Point(.5,.5),RenderTransform=new RotateTransform(45)};
            var cell=new Grid();cell.Children.Add(diamond);cell.Children.Add(label);
            var button=new Button{Content=cell,Width=48,Height=36,MinHeight=36,Margin=new Thickness(3,2,3,2),Padding=new Thickness(0),Tag=key};
            button.PreviewMouseLeftButtonDown+=(_,e)=>{QuickNote(key);button.CaptureMouse();e.Handled=true;};
            button.PreviewMouseLeftButtonUp+=(_,e)=>{EndKeyPreview(key);button.ReleaseMouseCapture();e.Handled=true;};
            button.MouseLeave+=(_,_)=>{EndKeyPreview(key);button.ReleaseMouseCapture();};
            button.LostMouseCapture+=(_,_)=>EndKeyPreview(key);
            _keys[i]=button;_keyLabels[i]=label;_keyDiamonds[i]=diamond;MapGrid.Children.Add(button);
        }
    }
    void RefreshEditorKeys()
    {
        var t=_session.ActiveTrack;_keyBindings=EditorBindings(t,_keyPreferences,_keyLegacy);
        string signature=$"{t.Id}:{t.PreviewInstrument}:{t.PreviewSemitoneOffset}:{t.GameTarget?.ProfileId}:{t.Capability?.Id}:{t.Capability?.Version}:{t.KeyDomain}:{string.Join(',',t.GameTarget?.KeyMidiPitches??new())}";
        if(signature!=_keySignature)
        {
            StopKeyPreview();_keySignature=signature;
            var notes=Enumerable.Range(0,15).Where(k=>PerformanceCompatibility.Evaluate(t,new(){Key=k}).Availability!=KeyAvailability.Unavailable)
                .Select(k=>new ProjectNote{Id=MidiSource.StableId($"key-preview:{t.Id}:{k}"),TrackId=t.Id,Key=k,Action=NoteAction.Hold,LengthBeats=1}).ToList();
            _keyPreviewProject=EditorSession.NewProject() with{FormatVersion=_session.Project.FormatVersion,KeyInterpretationVersion=_session.Project.KeyInterpretationVersion,Tracks=new(){t with{Muted=false,Solo=false,Notes=notes}}};
            _keyNotes=new ProjectTimeline(_keyPreviewProject.Tempos).GetNotes(_keyPreviewProject.Tracks[0]).ToDictionary(n=>n.Key);
        }
        for(int k=0;k<15;k++)
        {
            string label=_keyBindings[k] is {} vk?KeyConfig.Label(vk):$"K{k}";
            _keyLabels[k].Text=label;_keys[k].IsEnabled=k<_session.KeyCount;
            _keys[k].ToolTip=$"K{k} · {label}";System.Windows.Automation.AutomationProperties.SetName(_keys[k],$"K{k} · {label}");
        }
        RefreshCursorKeyColors();
    }
    void RefreshCursorKeyColors()
    {
        var keys=Roll.KeysAtPlayhead();keys.UnionWith(_keyHeld);
        for(int k=0;k<15;k++)
        {
            bool active=keys.Contains(k);
            _keys[k].Background=new SolidColorBrush(active?((SolidColorBrush)FindResource("Accent")).Color:Theme.KeySquare);
            _keys[k].BorderBrush=new SolidColorBrush(Theme.KeyBorder);_keyLabels[k].Foreground=new SolidColorBrush(active?Colors.White:Theme.KeyLetter);
            _keyDiamonds[k].BorderBrush=new SolidColorBrush(active?Colors.White:Theme.KeyDiamond);
        }
    }
    int KeyIndex(Key key)=>Array.IndexOf(_keyBindings,(ushort?)KeyInterop.VirtualKeyFromKey(key));
    void StartKeyPreview(int key)
    {
        if(!AudioEnabled||_closed||!_keyNotes.TryGetValue(key,out var note)||!_keyHeld.Add(key))return;
        _keyPending.Add(new(note,true));RefreshEditorKeys();if(_keyOutput!=null)FlushKeyEvents();else _=PrepareKeyOutput();
    }
    bool EndKeyPreview(int key)
    {
        if(!_keyHeld.Remove(key))return false;
        if(_keyNotes.TryGetValue(key,out var note))_keyPending.Add(new(note,false));
        FlushKeyEvents();RefreshEditorKeys();return true;
    }
    void FlushKeyEvents()
    {
        if(_keyOutput==null||_keyPending.Count==0)return;
        try{_keyOutput.Apply(_keyPending.ToArray());_keyPending.Clear();}
        catch(Exception error){StopKeyPreview();Logger.Exception("键位试听失败",error);}
    }
    async Task PrepareKeyOutput()
    {
        if(!AudioEnabled||_closed||_keyPreparing||_keyOutput!=null||_keyPreviewProject is not {} project)return;
        _keyPreparing=true;int generation=_keyGeneration;
        try
        {
            var output=await Task.Run(()=>KeyOutputFactory(project));
            if(_closed||generation!=_keyGeneration){output.StopAll();return;}
            _keyOutput=output;FlushKeyEvents();
        }
        catch(Exception error){if(generation==_keyGeneration)StopKeyPreview();Logger.Exception("键位试听准备失败",error);}
        finally{if(generation==_keyGeneration)_keyPreparing=false;}
    }
    void StopKeyPreview()
    {
        _keyGeneration++;_keyOutput?.StopAll();_keyOutput=null;_keyPreparing=false;_keyPending.Clear();_keyHeld.Clear();
    }
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        int key=KeyIndex(e.Key==Key.System?e.SystemKey:e.Key);
        if(key>=0&&EndKeyPreview(key)){e.Handled=true;return;}base.OnPreviewKeyUp(e);
    }
}
