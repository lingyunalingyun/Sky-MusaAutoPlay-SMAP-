using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SMAP_WPF;

public readonly record struct PhysicalRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
public sealed record GameWindow(IntPtr Handle, int ProcessId, long ProcessStarted, string Title, PhysicalRect Bounds, uint Dpi);

public static class GameWindows
{
    delegate bool EnumCallback(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder text, int count);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out PhysicalRect rect);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetStyle(IntPtr h,int index);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out PhysicalRect rect, int size);
    [DllImport("dwmapi.dll", EntryPoint="DwmGetWindowAttribute")] static extern int Cloaked(IntPtr h, int attribute, out int value, int size);
    public static IReadOnlyList<GameWindow> Find()
    {
        var result = new List<GameWindow>();
        EnumWindows((h, _) =>
        {
            try
            {
                GetWindowThreadProcessId(h, out uint id); using var p=Process.GetProcessById((int)id);
                if (p.ProcessName is not ("Sky" or "Sky_PC" or "SkyChildrenOfTheLight")) return true;
                if (Read(h) is { } window) result.Add(window);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return true;
        }, IntPtr.Zero);
        return result;
    }
    public static GameWindow? Read(IntPtr h)
    {
        if (!IsWindow(h) || !IsWindowVisible(h) || IsIconic(h) || Cloaked(h,14,out int cloaked,4)==0 && cloaked!=0) return null;
        GetWindowThreadProcessId(h,out uint pid);
        try
        {
            using var p=Process.GetProcessById((int)pid);
            var title=new StringBuilder(512);GetWindowText(h,title,title.Capacity);
            if(DwmGetWindowAttribute(h,9,out var bounds,16)!=0 && !GetWindowRect(h,out bounds))return null;
            if(bounds.Width<=0 || bounds.Height<=0)return null;
            return new(h,(int)pid,p.StartTime.ToUniversalTime().Ticks,title.ToString(),bounds,GetDpiForWindow(h));
        }
        catch(Exception error) when(error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){return null;}
    }
    public static bool SameIdentity(GameWindow a, GameWindow b) => a.Handle==b.Handle && a.ProcessId==b.ProcessId && a.ProcessStarted==b.ProcessStarted;
    public static bool IsForeground(GameWindow target) => GetForegroundWindow()==target.Handle && Read(target.Handle) is { } current && SameIdentity(target,current);
    public static bool IsKnownGame(IntPtr h)
    {
        try{GetWindowThreadProcessId(h,out uint pid);using var p=Process.GetProcessById((int)pid);return p.ProcessName is "Sky" or "Sky_PC" or "SkyChildrenOfTheLight";}
        catch(Exception error)when(error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception){return false;}
    }
    public static bool Occluded(GameWindow target,IntPtr overlay)
    {
        bool blocked=false;
        EnumWindows((h,_)=>
        {
            if(h==target.Handle)return false;
            if(h==overlay||!IsWindowVisible(h)||IsIconic(h)||Cloaked(h,14,out int value,4)==0&&value!=0)return true;
            if(!BlocksHints(GetStyle(h,-20).ToInt64()))return true;
            if(DwmGetWindowAttribute(h,9,out var b,16)!=0 && !GetWindowRect(h,out b))return true;
            var t=target.Bounds;
            if(b.Width>10&&b.Height>10&&b.Left<t.Right&&b.Right>t.Left&&b.Top<t.Bottom&&b.Bottom>t.Top){blocked=true;return false;}
            return true;
        },IntPtr.Zero);
        return blocked;
    }
    internal static bool BlocksHints(long extendedStyle)=>(extendedStyle&0x80020)!=0x80020;
}
