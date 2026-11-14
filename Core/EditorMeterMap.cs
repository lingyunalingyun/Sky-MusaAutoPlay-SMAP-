namespace SMAP_WPF.Core;

public readonly record struct EditorBar(decimal Beat, long Number, MeterPoint Meter, bool Partial);

public sealed class EditorMeterMap
{
    readonly MeterPoint[] _meters;
    readonly long[] _numbers;
    public EditorMeterMap(IEnumerable<MeterPoint> meters)
    {
        _meters = meters.OrderBy(m => m.Beat).ToArray(); _numbers = new long[_meters.Length];
        for (int i = 1; i < _meters.Length; i++)
        {
            decimal bars = decimal.Ceiling((_meters[i].Beat - _meters[i - 1].Beat) / Length(_meters[i - 1]));
            _numbers[i] = checked(_numbers[i - 1] + (long)Math.Min(bars, long.MaxValue - _numbers[i - 1]));
        }
    }
    static decimal Length(MeterPoint m) => m.Numerator * 4m / m.Denominator;
    public decimal EndOfBar(decimal beat, bool includeFollowingBar)
    {
        if (beat < 0) throw new ArgumentOutOfRangeException(nameof(beat));
        int i = 0;
        while (i + 1 < _meters.Length && _meters[i + 1].Beat <= beat) i++;
        var m = _meters[i]; decimal length = Length(m), offset = (beat - m.Beat) / length;
        decimal end = m.Beat + (includeFollowingBar ? decimal.Floor(offset) + 1 : decimal.Ceiling(offset)) * length;
        return i + 1 < _meters.Length ? Math.Min(end, _meters[i + 1].Beat) : end;
    }
    public IEnumerable<EditorBar> Visible(decimal left, decimal right)
    {
        int emitted = 0;
        for (int i = 0; i < _meters.Length; i++)
        {
            var m = _meters[i]; decimal length = Length(m), end = i + 1 < _meters.Length ? _meters[i + 1].Beat : decimal.MaxValue;
            if (end <= left || m.Beat > right) continue;
            decimal offset = Math.Max(0, decimal.Floor((left - m.Beat) / length));
            for (decimal b = m.Beat + offset * length; b < end && b <= right && emitted < 5000; b += length, offset++)
            {
                long number = _numbers[i] + (long)Math.Min(offset, long.MaxValue - _numbers[i] - 1) + 1; emitted++;
                yield return new(b, number, m, end != decimal.MaxValue && end - b < length);
            }
        }
    }
}
