using SMAP_WPF.Core;
using Xunit;

namespace SMAP.Tests;

public class EditorIndexTests
{
    [Fact]
    public void LongHoldStartingFarOutsideViewportIsNotCulled()
    {
        var t = TestProject.Sample().Tracks[0]; var hold = t.Notes[1] with { Beat = 0, LengthBeats = 100000 };
        var notes = Enumerable.Range(0, 100000).Select(i => t.Notes[0] with { Id = Guid.NewGuid(), Beat = i, Key = i % 15 }).Append(hold).ToArray();
        var index = new NoteViewportIndex(notes); var result = index.Query(90000, 90004, tapWidth: .25m).ToArray();
        Assert.Contains(hold, result); Assert.Equal(6, result.Length); Assert.InRange(index.LastVisitedNodes, 1, 600);
    }
    [Fact]
    public void QueryMatchesBruteForceForOverlapsAndAllKeyRanges()
    {
        var random = new Random(42); var template = TestProject.Sample().Tracks[0].Notes[1];
        var notes = Enumerable.Range(0, 2000).Select(i => template with { Id = Guid.NewGuid(), Key = random.Next(15), Beat = random.Next(1000) / 7m, LengthBeats = random.Next(1, 100) / 3m }).ToArray();
        var index = new NoteViewportIndex(notes);
        for (int i = 0; i < 100; i++)
        {
            decimal left = random.Next(1000) / 7m, right = left + 7;
            Assert.Equal(notes.Where(n => n.Key is >= 3 and <= 8 && n.Beat <= right && n.Beat + n.LengthBeats >= left).Select(n => n.Id).Order(),
                index.Query(left, right, 3, 8).Select(n => n.Id).Order());
        }
    }
    [Fact]
    public void MeterChangesPreservePartialBarsAndQuarterBeatUnits()
    {
        var map = new EditorMeterMap(new[] { new MeterPoint(0, 4, 4), new MeterPoint(6, 3, 8), new MeterPoint(9, 3, 4) });
        var bars = map.Visible(0, 15).ToArray(); Assert.Equal(new decimal[] { 0, 4, 6, 7.5m, 9, 12, 15 }, bars.Select(b => b.Beat));
        Assert.True(bars[1].Partial); Assert.Equal(3, bars[2].Number); Assert.Equal(5, bars[4].Number);
    }
    [Fact]
    public void IterativeIndexPreservesLaneOrderTapWidthAndExactBoundaryHits()
    {
        var template = TestProject.SingleTap().Tracks[0].Notes[0];
        var notes = Enumerable.Range(0, 1000).Select(i => template with { Id = Guid.NewGuid(), Key = i % 15, Beat = i / 10m, Action = i % 3 == 0 ? NoteAction.Hold : NoteAction.Tap, LengthBeats = i % 3 == 0 ? 5 : 0 }).Reverse().ToArray();
        var index = new NoteViewportIndex(notes);
        foreach (decimal left in new[] { 0m, .25m, 10m, 19.75m, 100m })
        {
            var actual = index.Query(left, left + 1, tapWidth: .25m).ToArray();
            Assert.Equal(notes.Where(n => n.Beat <= left + 1 && n.Beat + (n.Action == NoteAction.Tap ? .25m : n.LengthBeats) >= left).Select(n => n.Id).Order(), actual.Select(n => n.Id).Order());
            Assert.Equal(actual.OrderBy(n => n.Key).ThenBy(n => n.Beat).Select(n => n.Id), actual.Select(n => n.Id));
        }
    }
}
