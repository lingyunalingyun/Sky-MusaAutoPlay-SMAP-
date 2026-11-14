using System.Diagnostics;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace SMAP.Tests;

[CollectionDefinition("Editor UI", DisableParallelization = true)]
public sealed class EditorUiCollection;

[Collection("Editor UI")]
public class EditorUiIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ActualWpfWindowsPassThreeThemesFourLanguagesGesturesAndCompactLayout()
    {
        string root = TestProject.FindRepository(); string configuration = AppContext.BaseDirectory.Contains(Path.DirectorySeparatorChar + "Release" + Path.DirectorySeparatorChar) ? "Release" : "Debug";
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "run", "--project", Path.Combine(root, "Tests", "EditorUiHost", "EditorUiHost.csproj"), "-c", configuration, "--", "verify" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try { await process.WaitForExitAsync(deadline.Token); } catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new TimeoutException("编辑器窗口检查未在规定时间内完成。"); }
        string result = await stdout, error = await stderr; output.WriteLine(result); if (error.Length > 0) output.WriteLine(error);
        Assert.True(process.ExitCode == 0, result + error); Assert.Contains("renders=36", result); Assert.Contains("actual_windows=True", result);
    }
}
