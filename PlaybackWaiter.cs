using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SMAP_WPF;

internal sealed class PlaybackWaiter : IDisposable
{
    sealed class TimerHandle : WaitHandle
    {
        public TimerHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }
    readonly TimerHandle? _timer;
    readonly WaitHandle[]? _handles;
    public bool HighResolution { get; }

    public PlaybackWaiter(WaitHandle wake)
    {
        var handle = CreateWaitableTimerExW(IntPtr.Zero, null, 2, 0x00100002);
        HighResolution = !handle.IsInvalid;
        if (handle.IsInvalid) { handle.Dispose(); handle = CreateWaitableTimerExW(IntPtr.Zero, null, 0, 0x00100002); }
        if (handle.IsInvalid) { handle.Dispose(); return; }
        _timer = new(handle); _handles = new[] { wake, _timer };
    }

    public void Wait(WaitHandle wake, double milliseconds)
    {
        if (double.IsPositiveInfinity(milliseconds)) { wake.WaitOne(); return; }
        if (_timer != null)
        {
            long due = -Math.Max(1, (long)Math.Ceiling(Math.Clamp(milliseconds, 0, int.MaxValue) * 10000));
            if (SetWaitableTimer(_timer.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            { WaitHandle.WaitAny(_handles!); return; }
        }
        wake.WaitOne((int)Math.Clamp(Math.Ceiling(milliseconds), 1, int.MaxValue));
    }
    public void Dispose() => _timer?.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period,
        IntPtr completionRoutine, IntPtr completionArgument, [MarshalAs(UnmanagedType.Bool)] bool resume);
}
