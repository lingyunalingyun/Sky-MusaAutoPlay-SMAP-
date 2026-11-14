using System.Diagnostics;
using SMAP_WPF.Core;
using Xunit;
using Xunit.Abstractions;

namespace SMAP.Tests;

[CollectionDefinition("SAPM metrics", DisableParallelization = true)]
public sealed class SapmMetricsCollection;

[Collection("SAPM metrics")]
public class SapmMetricsTests(ITestOutputHelper output)
{
    [Fact]
    public void DenseMultiTrackConversionIsBoundedAndRepeatableWithReusedParsedSource()
    {
        var fixture = new MidiFixture(16); int notes = 0;
        for (int t = 0; t < 16; t++) for (int i = 0; i < 500; i++) for (int k = 0; k < 3; k++)
        { fixture.Note(48 + (t % 3) * 12 + new[] { 0, 4, 7 }[k] + i % 7, i * 120 + t % 5, i * 120 + 110 + t % 5, channel: t % 9 + 1, track: t); notes++; }
        byte[] bytes = fixture.Bytes(); using var process = Process.GetCurrentProcess();
        long allocationStart = GC.GetTotalAllocatedBytes(precise: true); var watch = Stopwatch.StartNew();
        var source = MidiSourceReader.Read(bytes); watch.Stop(); double parseMs = watch.Elapsed.TotalMilliseconds;
        var settings = MidiFixture.Options(source, auto: true); watch.Restart(); var first = SapmMapper.Convert(source, settings); watch.Stop();
        double firstMs = watch.Elapsed.TotalMilliseconds; watch.Restart(); var second = SapmMapper.Convert(source, settings); watch.Stop();
        Assert.Equal(notes, source.Notes.Count); Assert.Equal(first.Notes, second.Notes); Assert.Equal(first.Quality, second.Quality);
        Assert.Equal(first.AutoShifts.OrderBy(p => p.Key), second.AutoShifts.OrderBy(p => p.Key));
        process.Refresh(); long allocations = GC.GetTotalAllocatedBytes(precise: true) - allocationStart;
        output.WriteLine(FormattableString.Invariant($"SAPM_METRICS tracks=16; voices={source.Voices.Count}; notes={notes}; midi_bytes={bytes.Length}; parse_ms={parseMs:F3}; map_ms={firstMs:F3}; repeat_map_ms={watch.Elapsed.TotalMilliseconds:F3}; allocated_bytes={allocations}; managed_bytes={GC.GetTotalMemory(false)}; process_peak_working_set_bytes={process.PeakWorkingSet64}; source_reused=True; deterministic=True; melody_fidelity={first.Quality!.MelodyFidelity:F6}; harmony_fidelity={first.Quality.HarmonyFidelity:F6}; diagnostics={source.Issues.Count}; warnings={source.Issues.Count(i => i.Severity == MidiIssueSeverity.Warning)}"));
        Assert.InRange(firstMs, 0, 60000); Assert.InRange(process.PeakWorkingSet64, 1, 2L * 1024 * 1024 * 1024);
    }
}
