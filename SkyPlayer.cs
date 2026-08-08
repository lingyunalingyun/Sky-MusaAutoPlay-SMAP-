using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace SMAP_WPF;

/// <summary>自动弹琴: 按曲谱时间线用 SendInput 模拟键盘按键(scan code, 游戏更认)。</summary>
public class SkyPlayer
{
    // 光遇 15 键默认物理键的 virtual-key code: Y U I O P / H J K L ; / N M , . /
    public static readonly ushort[] DefaultVk =
    {
        0x59, 0x55, 0x49, 0x4F, 0x50,   // Y U I O P
        0x48, 0x4A, 0x4B, 0x4C, 0xBA,   // H J K L ;
        0x4E, 0x4D, 0xBC, 0xBE, 0xBF    // N M , . /
    };

    public ushort[] Vk = (ushort[])DefaultVk.Clone();

    /// <summary>每个音符触发时回调(光遇键索引), 供 UI 同步亮起琴键。在后台线程调用, 订阅方自行 marshal。</summary>
    public Action<int>? NoteFired;

    public double SpeedFactor = 1.0;   // 实时倍速, 播放中可改
    public bool RandomSpeed;           // 随机模式: 每隔随机个音符随机变速(0.5~1.3)
    int _randCountdown;                // 距下次变速还剩几个音符
    public double PositionMs;          // 当前歌曲进度(ms)
    public double TotalMs;             // 曲谱总时长(ms)

    volatile bool _seek;
    double _seekMs;
    /// <summary>跳转到歌曲某毫秒位置(拖进度条); 播放线程下一帧生效, 跳过的音符不补发。</summary>
    public void Seek(double ms) { _seekMs = Math.Max(0, ms); _seek = true; }

    Thread? _thread;
    volatile bool _stop;
    volatile bool _paused;

    public bool IsPlaying => _thread is { IsAlive: true };
    public void Pause(bool p) => _paused = p;

    /// <summary>按曲谱时间线触发音符。noteAction 默认 SendInput 发按键(弹到游戏); 传 AudioEngine.Play 则走扬声器试听。</summary>
    public void Play(List<(int key, double ms)> notes, double speed, Action onDone, Action<int>? noteAction = null)
    {
        Stop();
        _stop = false; _paused = false;
        SpeedFactor = speed;
        var act = noteAction ?? PressKey;
        var list = notes.Where(n => n.key >= 0 && n.key < 15).OrderBy(n => n.ms).ToList();
        _thread = new Thread(() => Run(list, onDone, act)) { IsBackground = true };
        _thread.Start();
    }

    public void Stop()
    {
        _stop = true;
        _thread?.Join(200);
        _thread = null;
    }

    // 累积歌曲时间模型: 每帧 songMs += 真实dt × 实时倍速, 中途变速不错位。
    void Run(List<(int key, double ms)> notes, Action onDone, Action<int> noteAction)
    {
        TotalMs = notes.Count > 0 ? notes[^1].ms : 0;
        PositionMs = 0;
        _seek = false;
        _randCountdown = 0;
        double songMs = 0, last = 0;
        int idx = 0;
        var sw = Stopwatch.StartNew();
        while (!_stop && idx < notes.Count)
        {
            double now = sw.Elapsed.TotalMilliseconds;
            double dt = now - last; last = now;
            if (_paused)
            {
                if (_seek)   // 暂停时也能拖动进度条: 应用跳转(更新位置/音符指针), 但不发声
                {
                    songMs = _seekMs; _seek = false; idx = 0;
                    while (idx < notes.Count && notes[idx].ms < songMs) idx++;
                    PositionMs = songMs;
                }
                Thread.Sleep(10); continue;
            }
            if (_seek)
            {
                songMs = _seekMs; _seek = false;
                idx = 0;
                while (idx < notes.Count && notes[idx].ms < songMs) idx++;   // 跳过已过音符
            }
            else songMs += dt * SpeedFactor;
            while (idx < notes.Count && notes[idx].ms <= songMs)
            {
                noteAction(notes[idx].key);
                NoteFired?.Invoke(notes[idx].key);
                idx++;
                if (RandomSpeed && --_randCountdown <= 0)
                {
                    SpeedFactor = 0.5 + Random.Shared.NextDouble() * 0.8;   // 0.5~1.3
                    _randCountdown = Random.Shared.Next(2, 6);              // 隔 2~5 个音符再变
                }
            }
            PositionMs = songMs;
            Thread.Sleep(1);
        }
        // 仅自然播放结束才回调(手动 Stop 不触发, 否则会 停止→onDone→自动续播→再停止 无限级联卡死)
        if (!_stop) { PositionMs = TotalMs; onDone(); }
    }

    void PressKey(int lightKey)
    {
        ushort vk = Vk[lightKey];
        SendKey(vk, false);   // down
        Thread.Sleep(HoldMs); // 游戏按帧轮询, 瞬时按下抬起会漏, 保持一小段
        SendKey(vk, true);    // up
    }

    public int HoldMs = 18;   // 按住时长(ms)

    // ---- Win32 SendInput ----
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;   // 占位: 让 union 达到 MOUSEINPUT 大小, INPUT 才是正确的 40 字节(64位)
        [FieldOffset(0)] public KEYBDINPUT ki;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint uCode, uint uMapType);

    const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_SCANCODE = 0x0008, KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint MAPVK_VK_TO_VSC = 0;

    // 游戏(光遇)用 raw input, 只认扫描码; 虚拟键码只有记事本等普通程序认。故发扫描码。
    static void SendKey(ushort vk, bool up)
    {
        ushort scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        uint flags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0);
        // 扩展键(方向/Ins/Del/右Ctrl等 VK)需带扩展标志, 否则扫描码歧义
        if (vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0xA3 or 0xA5)
            flags |= KEYEVENTF_EXTENDEDKEY;

        var inp = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,          // 用扫描码时 wVk 置 0
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
        SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
    }
}
