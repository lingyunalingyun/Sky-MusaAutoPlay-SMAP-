using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed record ObservedKey(ushort Key, ShortcutModifiers Modifiers, bool Down, IntPtr Foreground, long Timestamp);
internal interface IKeyboardObserver : IDisposable
{
    event Action<ObservedKey>? Key;
    event Action? Overflow;
}
public sealed class GlobalKeyboardObserver : IKeyboardObserver
{
    [StructLayout(LayoutKind.Sequential)] struct Data { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
    delegate IntPtr Callback(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,Callback callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    readonly ConcurrentQueue<ObservedKey> _queue=new();
    readonly Callback _callback; readonly DispatcherTimer _drain=new(){Interval=TimeSpan.FromMilliseconds(15)};
    readonly Thread _thread;
    Dispatcher? _hookDispatcher;
    IntPtr _hook; int _queued,_overflow; volatile bool _disposed;
    public event Action<ObservedKey>? Key;
    public event Action? Overflow;
    public GlobalKeyboardObserver()
    {
        _callback=Observe;
        using var ready=new ManualResetEventSlim();Exception? failure=null;
        _thread=new Thread(()=>
        {
            try
            {
                _hookDispatcher=Dispatcher.CurrentDispatcher;
                _hook=SetWindowsHookEx(13,_callback,GetModuleHandle(null),0);
                if(_hook==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            catch(Exception error){failure=error;}
            finally{ready.Set();}
            try{if(failure==null)Dispatcher.Run();}
            finally{if(_hook!=IntPtr.Zero){UnhookWindowsHookEx(_hook);_hook=IntPtr.Zero;}}
        }){IsBackground=true,Name="Keyboard observation"};
        _thread.Start();ready.Wait();
        if(failure!=null){_thread.Join();throw failure;}
        _drain.Tick+=Drain;_drain.Start();
    }
    void Drain(object? sender,EventArgs args)
    {
        if(Interlocked.Exchange(ref _overflow,0)!=0)
        {while(_queue.TryDequeue(out _))Interlocked.Decrement(ref _queued);Overflow?.Invoke();return;}
        while(!_disposed&&_queue.TryDequeue(out var e)){Interlocked.Decrement(ref _queued);Key?.Invoke(e);}
    }
    IntPtr Observe(int code,IntPtr message,IntPtr pointer)
    {
        if(!_disposed && code>=0 && message.ToInt32() is 0x100 or 0x101 or 0x104 or 0x105)
        {
            var d=Marshal.PtrToStructure<Data>(pointer);
            if((d.Flags&0x12)==0)
            {
                bool down=message.ToInt32() is 0x100 or 0x104;
                ShortcutModifiers mods=0;
                if(GetAsyncKeyState(0x11)<0 || down&&d.Key is 0x11 or 0xA2 or 0xA3)mods|=ShortcutModifiers.Ctrl;
                if(GetAsyncKeyState(0x12)<0 || down&&d.Key is 0x12 or 0xA4 or 0xA5)mods|=ShortcutModifiers.Alt;
                if(GetAsyncKeyState(0x10)<0 || down&&d.Key is 0x10 or 0xA0 or 0xA1)mods|=ShortcutModifiers.Shift;
                if(Interlocked.Increment(ref _queued)<=256)_queue.Enqueue(new((ushort)d.Key,mods,down,GameWindows.GetForegroundWindow(),Stopwatch.GetTimestamp()));
                else {Interlocked.Decrement(ref _queued);Interlocked.Exchange(ref _overflow,1);}
            }
        }
        return CallNextHookEx(_hook,code,message,pointer);
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;_drain.Stop();_drain.Tick-=Drain;
        _hookDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        if(!_thread.Join(TimeSpan.FromSeconds(3)))throw new TimeoutException("KeyboardObserverShutdownTimedOut");
        while(_queue.TryDequeue(out _)){}Interlocked.Exchange(ref _queued,0);Key=null;Overflow=null;
    }
}
