using System.IO;

namespace SMAP_WPF.Core;

public readonly record struct TimedProjectNote(Guid TrackId, Guid NoteId, int Key,
    NoteAction Action, decimal StartMicroseconds, decimal LengthMicroseconds, bool RetriggerOnOverlap = false);

public sealed class ProjectTimeline
{
    readonly TempoPoint[] _points;
    readonly decimal[] _starts;

    public ProjectTimeline(IEnumerable<TempoPoint> tempos)
    {
        _points = tempos.OrderBy(p => p.Beat).ToArray();
        if (_points.Length == 0 || _points[0].Beat != 0
            || _points.Any(p => p.MicrosecondsPerBeat <= 0)
            || _points.Zip(_points.Skip(1)).Any(p => p.First.Beat >= p.Second.Beat))
            throw new InvalidDataException("速度时间线无效。");
        _starts = new decimal[_points.Length];
        try
        {
            for (int i = 1; i < _points.Length; i++)
                _starts[i] = checked(_starts[i - 1]
                    + (_points[i].Beat - _points[i - 1].Beat) * _points[i - 1].MicrosecondsPerBeat);
        }
        catch (OverflowException ex) { throw new InvalidDataException("速度时间线超出数值范围。", ex); }
    }

    public decimal BeatToMicroseconds(decimal beat)
    {
        if (beat < 0) throw new ArgumentOutOfRangeException(nameof(beat));
        int index = FindSegment(beat, i => _points[i].Beat);
        try { return checked(_starts[index] + (beat - _points[index].Beat) * _points[index].MicrosecondsPerBeat); }
        catch (OverflowException ex) { throw new InvalidDataException("音符时间超出数值范围。", ex); }
    }

    public decimal MicrosecondsToBeat(decimal microseconds)
    {
        if (microseconds < 0) throw new ArgumentOutOfRangeException(nameof(microseconds));
        int index = FindSegment(microseconds, i => _starts[i]);
        return _points[index].Beat + (microseconds - _starts[index]) / _points[index].MicrosecondsPerBeat;
    }

    public decimal LengthToMicroseconds(decimal beat, decimal lengthBeats)
    {
        if (beat < 0 || lengthBeats < 0) throw new ArgumentOutOfRangeException(nameof(lengthBeats));
        int index = FindSegment(beat, i => _points[i].Beat);
        decimal elapsed = 0;
        try
        {
            while (lengthBeats > 0)
            {
                decimal span = index + 1 < _points.Length ? Math.Min(lengthBeats, _points[index + 1].Beat - beat) : lengthBeats;
                elapsed = checked(elapsed + span * _points[index].MicrosecondsPerBeat);
                lengthBeats -= span;
                if (lengthBeats > 0) beat = _points[++index].Beat;
            }
        }
        catch (OverflowException ex) { throw new InvalidDataException("音符长度超出数值范围。", ex); }
        return elapsed;
    }

    int FindSegment(decimal value, Func<int, decimal> start)
    {
        int lo = 0, hi = _points.Length - 1;
        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            if (start(mid) <= value) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    public IEnumerable<TimedProjectNote> GetNotes(ProjectTrack track) => track.Notes
        .OrderBy(n => n.Beat).ThenBy(n => n.Key)
        .Select(n => new TimedProjectNote(track.Id, n.Id, n.Key, n.Action,
            BeatToMicroseconds(n.Beat), LengthToMicroseconds(n.Beat, n.LengthBeats), n.RetriggerOnOverlap));
}
