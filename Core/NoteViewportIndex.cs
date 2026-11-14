namespace SMAP_WPF.Core;

public sealed class NoteViewportIndex
{
    sealed class Lane
    {
        public readonly ProjectNote[] Notes;
        public readonly decimal[] Ends;
        public Lane(IEnumerable<ProjectNote> notes, bool ordered)
        {
            Notes = ordered ? notes.ToArray() : notes.OrderBy(n => n.Beat).ThenBy(n => n.Id).ToArray(); Ends = new decimal[Notes.Length];
            Build(0, Notes.Length);
        }
        decimal Build(int low, int high)
        {
            if (low >= high) return -1;
            int mid = (low + high) / 2;
            return Ends[mid] = Math.Max(Notes[mid].Beat + Notes[mid].LengthBeats, Math.Max(Build(low, mid), Build(mid + 1, high)));
        }
    }
    readonly Lane[] _lanes;
    public int LastVisitedNodes { get; private set; }
    public NoteViewportIndex(IEnumerable<ProjectNote> notes, bool ordered = false)
    {
        var lanes = Enumerable.Range(0, 15).Select(_ => new List<ProjectNote>()).ToArray();
        foreach (var note in notes) lanes[note.Key].Add(note);
        _lanes = lanes.Select(n => new Lane(n, ordered)).ToArray();
    }
    public IEnumerable<ProjectNote> Query(decimal left, decimal right, int minKey = 0, int maxKey = 14, decimal tapWidth = 0)
    {
        LastVisitedNodes = 0;
        var stack = new Stack<(int low, int high, bool emit)>();
        for (int key = Math.Max(0, minKey); key <= Math.Min(14, maxKey); key++)
        {
            var lane = _lanes[key]; stack.Push((0, lane.Notes.Length, false));
            while (stack.TryPop(out var frame))
            {
                if (frame.low >= frame.high) continue;
                int mid = (frame.low + frame.high) / 2; var note = lane.Notes[mid];
                if (frame.emit)
                {
                    if (note.Beat <= right && note.Beat + (note.Action == NoteAction.Tap ? tapWidth : note.LengthBeats) >= left) yield return note;
                    continue;
                }
                LastVisitedNodes++;
                if (lane.Ends[mid] + tapWidth < left || lane.Notes[frame.low].Beat > right) continue;
                if (note.Beat <= right) stack.Push((mid + 1, frame.high, false));
                stack.Push((frame.low, frame.high, true)); stack.Push((frame.low, mid, false));
            }
        }
    }
}
