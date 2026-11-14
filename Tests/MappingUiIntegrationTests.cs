using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SMAP.Tests;

[Collection("Editor UI")]
public class MappingUiIntegrationTests(ITestOutputHelper output)
{
    [Fact]public async Task ActualWpfLayoutsCaptureAndNonActivatingOverlayPass()
    {
        string result=await Run("verify");Assert.Contains("renders=48",result);Assert.Contains("capture=WGC",result);
        Assert.Contains("game_acceptance=False",result);
    }
    [Fact]public async Task KeysShortcutScopesAndMappingSelectorsHaveAccessibleNames()
    {
        Assert.Contains("MAPPING_ACCESSIBILITY names=True",await Run("accessibility"));
    }
    [Fact]public async Task DeferredWindowResizeRestoresRequestedPhysicalBounds()
    {
        Assert.Contains("MAPPING_PLACEMENT physical_bounds=True;hidden=True",await Run("placement"));
    }
    [Fact]public async Task StartAndPauseReportTheirActualPracticeState()
    {
        Assert.Contains("MAPPING_FEEDBACK uncalibrated=True;armed=True;paused=True",await Run("feedback"));
    }
    async Task<string> Run(string mode)
    {
        var start=new ProcessStartInfo("dotnet"){WorkingDirectory=TestProject.FindRepository(),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        string configuration=AppContext.BaseDirectory.Contains("Release")?"Release":"Debug";
        foreach(string arg in new[]{"run","--project","Tests/MappingUiHost/MappingUiHost.csproj","-c",configuration,"--",mode})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(entireProcessTree:true);throw new TimeoutException("Mapping window verification timed out");}
        string result=await stdout,error=await stderr;output.WriteLine(result);if(error.Length>0)output.WriteLine(error);
        Assert.True(process.ExitCode==0,result+error);return result;
    }
}
