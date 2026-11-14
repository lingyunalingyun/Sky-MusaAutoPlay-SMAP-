using System.IO;
using SMAP_WPF;
using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public sealed class FinalImageRecoveryTests
{
    [Fact]
    public void EveryPageInBothDirectionsAndDensitiesRecoversAllTracksHoldsAndMeterEvents()
    {
        using var files=new TestProject();var source=TestProject.Sample();var melody=source.Tracks[0];
        melody.Notes.Add(new(){TrackId=melody.Id,Key=13,Beat=0,LengthBeats=100,Action=NoteAction.Hold});
        melody.Notes.AddRange(Enumerable.Range(1,96).Select(i=>new ProjectNote{TrackId=melody.Id,Key=i%10,Beat=8+i,Action=NoteAction.Tap}));
        source=source with{Meters=[new(0,4,4),new(8,6,8)]};
        Exception? error=null;var thread=new Thread(()=>
        {
            try
            {
                foreach(var direction in Enum.GetValues<ScoreOrientation>())foreach(var spacing in Enum.GetValues<ScoreSpacing>())
                {
                    var document=ScoreExport.Prepare(source,new ScoreExportOptions{TrackIds=source.Tracks.Select(t=>t.Id).ToArray(),Orientation=direction,Spacing=spacing});
                    Assert.True(document.Pages.Count>1);Assert.Contains(document.Project.Tracks.SelectMany(t=>t.Notes),n=>n.Beat>8);
                    foreach(var page in document.Pages)
                    {
                        string svg=files.PathFor($"{direction}-{spacing}-{page.Index}.svg"),png=Path.ChangeExtension(svg,".png");
                        File.WriteAllText(svg,ScoreImageExporter.Svg(document,page));File.WriteAllBytes(png,ScoreImageExporter.Png(document,page));
                        foreach(string image in new[]{svg,png})
                        {
                            var restored=ScoreImageData.Read(image);Assert.Equal(document.Payload,SmapFile.ToBytes(restored));Assert.Equal(2,restored.Tracks.Count);
                            Assert.Equal(document.Project.Meters,restored.Meters);Assert.Equal(document.Project.Tempos,restored.Tempos);
                            Assert.Contains(restored.Tracks.SelectMany(t=>t.Notes),n=>n.Action==NoteAction.Hold&&n.LengthBeats==100);
                            var editor=new EditorSession(restored);editor.Create(12,112);Assert.True(editor.IsDirty);editor.Undo();
                            Assert.Equal(document.PerformanceHash,PerformanceIdentity.ForProject(editor.Project));
                        }
                    }
                }
            }
            catch(Exception e){error=e;}
        }){IsBackground=true};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(120)),"图片恢复测试未在规定时间内完成。");
        if(error!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
