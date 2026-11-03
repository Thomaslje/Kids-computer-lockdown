using System.Runtime.InteropServices;

namespace KioskLauncher;

/// <summary>Low-level keyboard hook that swallows the keys kids could use to escape.</summary>
sealed class KeyboardBlocker : IDisposable
{
    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public UIntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    const int WH_KEYBOARD_LL = 13;
    const uint LLKHF_ALTDOWN = 0x20;

    readonly HookProc _proc; // keep a reference so the GC never collects the callback
    IntPtr _hook;

    public KeyboardBlocker()
    {
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int vk = (int)k.vkCode;
            bool alt = (k.flags & LLKHF_ALTDOWN) != 0;
            bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;

            bool block =
                vk == 0x5B || vk == 0x5C                    // left/right Windows key
                || (alt && (vk == 0x09 || vk == 0x1B))      // Alt+Tab, Alt+Esc
                || (ctrl && vk == 0x1B)                     // Ctrl+Esc, Ctrl+Shift+Esc
                || (alt && vk == 0x73 && LauncherHasFocus()); // Alt+F4 on the launcher itself

            if (block) return (IntPtr)1;
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    static bool LauncherHasFocus()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }
}
