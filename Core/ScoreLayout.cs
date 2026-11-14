using System.Globalization;
using System.IO;

namespace SMAP_WPF.Core;

public sealed record ScoreBody(double X, double Y, double Width, double Height);
public sealed record ScoreReference(string Symbol, decimal BeatsPerPulse, decimal ExactBpm)
{
    public string Number => decimal.Round(ExactBpm, 0, MidpointRounding.ToEven).ToString(CultureInfo.InvariantCulture);
}

public static class ScoreLayout
{
    public const double KeySide = 35, KeyStep = 45.5;
    public static ScoreBody Body(ScoreOrientation orientation) => orientation == ScoreOrientation.Landscape
        ? new(40, 175, 2160, 969) : new(175, 40, 969, 2160);
    public static double RowHeight(ScoreSpacing spacing) => spacing == ScoreSpacing.Compact ? 192 : 224;
    public static MeterPoint Meter(SmapProject p, decimal beat) => p.Meters.Where(m => m.Beat <= beat).MaxBy(m => m.Beat)!;
    public static int DisplayFactor(SmapProject p, bool simplify)
    {
        if (!simplify || p.Tempos.Count != 1) return 1;
        decimal bpm = 60_000_000m / p.Tempos[0].MicrosecondsPerBeat;
        if (bpm <= 200) return 1;
        foreach (int f in new[] { 2, 4, 8 })
            if (bpm / f is >= 70 and <= 180 && p.Meters.All(m => m.Denominator <= 64 / f)) return f;
        return 1;
    }
    public static ScoreReference Reference(SmapProject p, decimal beat, int factor)
    {
        var m = Meter(p, beat);
        decimal pulse = factor;
        string symbol = "♩";
        if (factor == 1 && m.Denominator == 8 && m.Numerator >= 6 && m.Numerator % 3 == 0)
        { pulse = 1.5m; symbol = "♩·"; }
        var tempo = p.Tempos.Where(t => t.Beat <= beat).MaxBy(t => t.Beat)!;
        return new(symbol, pulse, 60_000_000m / tempo.MicrosecondsPerBeat / pulse);
    }
    public static string BeatLabel(SmapProject p, decimal start, decimal end, decimal beat, int factor)
    {
        if (beat == end) return "|";
        decimal pulse = Reference(p, beat, factor).BeatsPerPulse;
        decimal offset = (beat - start) / pulse;
        decimal whole = decimal.Round(offset, 0, MidpointRounding.ToEven);
        return whole < decimal.Ceiling((end - start) / pulse) && Math.Abs(offset - whole) < .01m
            ? (whole + 1).ToString(CultureInfo.InvariantCulture) : "·";
    }
    internal static IReadOnlyList<ScorePage> Paginate(IReadOnlyList<ScoreCell[]> groups, ScoreExportOptions options,
        int factor, CancellationToken cancellation)
    {
        var body = Body(options.Orientation); bool compact = options.Spacing == ScoreSpacing.Compact;
        var rows = new List<ScoreRow>(); var pending = new List<(ScoreCell[] Cells, double Width)>(); double used = 0;
        void Flush()
        {
            for (int i = 0; i < pending[0].Cells.Length; i++)
                rows.Add(new(pending.Select(g => new ScoreSlot(g.Cells[i], g.Width)).ToArray()));
            pending.Clear(); used = 0;
        }
        foreach (var group in groups)
        {
            cancellation.ThrowIfCancellationRequested();
            double gap = (double)Math.Min(1, group[0].Column.DurationBeats / factor);
            double width = 217 + (compact ? 14 : 28) + gap * (compact ? 10 : 40);
            if (pending.Count > 0 && used + width > body.Width) Flush();
            pending.Add((group, width)); used += width;
        }
        if (pending.Count > 0) Flush();
        int perPage = (int)(body.Height / RowHeight(options.Spacing));
        var pages = rows.Chunk(perPage).Select(r => r.ToList()).ToList();
        if (pages.Count > ScoreExport.MaxPages) throw new InvalidDataException("ScoreTooLarge");
        if (pages.Count > 1 && pages[^1].Sum(r => r.Slots.Count) < .4 * pages[^2].Sum(r => r.Slots.Count))
        {
            var tail = pages[^2].Concat(pages[^1]).ToArray(); int split = tail.Length / 2;
            pages[^2] = tail.Take(split).ToList(); pages[^1] = tail.Skip(split).ToList();
        }
        return pages.Select((r, i) => new ScorePage(i + 1, r)).ToArray();
    }
}
