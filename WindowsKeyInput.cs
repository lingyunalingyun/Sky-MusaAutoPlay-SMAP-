using System.ComponentModel;
using System.Runtime.InteropServices;
using SMAP_WPF.Core;

namespace SMAP_WPF;

public sealed class WindowsKeyInput : IKeyInput
{
    public void Send(IReadOnlyList<KeyTransition> changes)
    {
        var inputs = changes.Select(change =>
        {
            uint flags = 0x0008u | (change.IsDown ? 0 : 0x0002u);
            if (change.Key is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0xA3 or 0xA5)
                flags |= 0x0001;
            return new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT
            { wScan = (ushort)MapVirtualKey(change.Key, 0), dwFlags = flags } } };
        }).ToArray();
        if (inputs.Length > 0 && SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "游戏按键批次未被完整接受。");
    }

    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT
    { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT
    { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
}
