using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SMAP.Tests;

[Collection("Editor UI")]
public class VersionUiIntegrationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("boundaries","VERSION_BOUNDARIES windows=0;capture_entry=blocked")]
    [InlineData("lifecycle","VERSION_LIFECYCLE practice=True;game_input_factory_calls=0")]
    [InlineData("observer","VERSION_OBSERVER dedicated_thread=True;cycles=3;remaining_hooks=0")]
    [InlineData("windows","VERSION_WINDOWS renders=24;themes=3;languages=4")]
    [InlineData("localization","VERSION_TRACK_LOCALIZATION dialogs=12;languages=4;identities=preserved")]
    [InlineData("editor-fixes","EDITOR_FIXES layouts=12;overview=cache;keys=polyphonic;menus=bounded")]
    [InlineData("library-practice","LIBRARY_PRACTICE sort=mouse-up;initial_follow=True;repeat=ignored;wrong_key=ignored")]
    [InlineData("registration-rollback","REGISTRATION_ROLLBACK native_error=1409;previous=retained;released=True")]
    [InlineData("choice-reuse","CHOICE_REUSE independent=True;selection=True;reopen=True;cleanup=True")]
    [InlineData("choice-layout","CHOICE_LAYOUT themes=3;languages=4;widths=2;aligned=True")]
    [InlineData("joined-choice","JOINED_CHOICE clipping=True;anchor=True;reopen=True;reduced=True")]
    [InlineData("cursor-keys","CURSOR_KEYS chord=True;hold=True;seek=True;undo=True;audio=0")]
    [InlineData("choice-lifecycle","CHOICE_LIFECYCLE same_tree=True;cycles=5;move=True;resize=True;selection=True")]
    [InlineData("cloud-choice","CLOUD_CHOICE sort=3;difficulty=6;independent=True;languages=4;themes=3")]
    [InlineData("settings-choice","SETTINGS_CHOICE controls=4;languages=4;themes=3;effects=True;bindings=True;prefs_unchanged=True")]
    [InlineData("choice-motion","CHOICE_MOTION header=fixed;text=fixed;reveal=elastic;themes=3;heights=2")]
    [InlineData("instrument-pill","INSTRUMENT_PILL fixed_width=True;joined=True;reopen=True;motion=True;selection=True")]
    [InlineData("instrument-scroll","INSTRUMENT_SCROLL pixels=True;intermediate=True;reverse=True;reopen=True")]
    [InlineData("pitch-pill","PITCH_PILL original_options=True;joined=True;fixed_width=True;exclusive=True;prefs_unchanged=True")]
    [InlineData("speed-pill","SPEED_PILL original_options=True;selection=True;random=True;joined=True;exclusive=True")]
    [InlineData("practice-cover","PRACTICE_COVER passed")]
    [InlineData("practice-timing","PRACTICE_TIMING passed")]
    [InlineData("practice-return","PRACTICE_RETURN passed")]
    [InlineData("practice-rebound","PRACTICE_REBOUND passed")]
    [InlineData("editor-choices","EDITOR_CHOICES snap=True;tools=True;keyboard=True;history=True;joined=True")]
    [InlineData("editor-track-choice","EDITOR_TRACK_CHOICE identity=True;switch=True;keyboard=True;busy=True;history=True")]
    [InlineData("editor-command-menu","EDITOR_COMMAND_MENU joined=True;track=True;commands=True;palette=True;lifecycle=True;motion=True")]
    public async Task ProductionEntryAndLifecycleBoundariesPassInActualWpf(string mode,string expected)
    {
        var start=new ProcessStartInfo("dotnet"){WorkingDirectory=TestProject.FindRepository(),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        string configuration=AppContext.BaseDirectory.Contains("Release")?"Release":"Debug";
        foreach(string arg in new[]{"run","--project","Tests/VersionUiHost/VersionUiHost.csproj","-c",configuration,"--",mode})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(entireProcessTree:true);throw new TimeoutException("Version isolation verification timed out");}
        string result=await stdout,error=await stderr;output.WriteLine(result);if(error.Length>0)output.WriteLine(error);
        Assert.True(process.ExitCode==0,result+error);Assert.Contains(expected,result);
    }
}
