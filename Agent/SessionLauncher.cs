using System.Runtime.InteropServices;
using System.Text;
using KioskShared;

namespace KioskAgent;

/// <summary>
/// Starts a program as SYSTEM (full admin rights) on the screen of the person who is logged in. The agent is a service in
/// session 0, so it copies its own token, points the copy at the user's session and starts the program with it.
/// SYSTEM has none of the Kiosk account's restrictions (Task Manager, Registry Editor and so on work).
/// </summary>
static class SessionLauncher
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [StructLayout(LayoutKind.Sequential)]
    struct WTS_SESSION_INFO { public int SessionId; public IntPtr pWinStationName; public int State; }

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attrs, int impersonationLevel, int tokenType, out IntPtr newToken);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool SetTokenInformation(IntPtr token, int infoClass, ref int info, int length);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessAsUser(IntPtr token, string? application, StringBuilder commandLine, IntPtr processAttrs,
        IntPtr threadAttrs, bool inheritHandles, uint flags, IntPtr environment, string? currentDirectory,
        ref STARTUPINFO startup, out PROCESS_INFORMATION info);
    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);
    [DllImport("userenv.dll")]
    static extern bool DestroyEnvironmentBlock(IntPtr environment);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")]
    static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr info, out int count);
    [DllImport("wtsapi32.dll")]
    static extern void WTSFreeMemory(IntPtr memory);
    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool WTSQuerySessionInformation(IntPtr server, int session, int infoClass, out IntPtr buffer, out int bytes);

    const uint MaximumAllowed = 0x02000000;
    const uint CreateNewConsole = 0x10, CreateUnicodeEnvironment = 0x400;

    /// <summary>The session of the Kiosk account, else the one at the screen. -1 if nobody is logged in.</summary>
    static int FindSession()
    {
        var kioskUser = File.Exists(Paths.KioskUserFile) ? File.ReadAllText(Paths.KioskUserFile).Trim() : "";
        int best = -1;
        if (WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var list, out var count))
        {
            try
            {
                int size = Marshal.SizeOf<WTS_SESSION_INFO>();
                for (int i = 0; i < count; i++)
                {
                    var si = Marshal.PtrToStructure<WTS_SESSION_INFO>(list + i * size);
                    if (si.SessionId == 0 || si.State != 0) continue;   // 0 = Active
                    if (WTSQuerySessionInformation(IntPtr.Zero, si.SessionId, 5, out var name, out _))   // 5 = user name
                    {
                        var user = Marshal.PtrToStringUni(name) ?? "";
                        WTSFreeMemory(name);
                        if (user.Equals(kioskUser, StringComparison.OrdinalIgnoreCase)) return si.SessionId;
                        if (best < 0 && user != "") best = si.SessionId;
                    }
                }
            }
            finally { WTSFreeMemory(list); }
        }
        if (best >= 0) return best;
        var console = WTSGetActiveConsoleSessionId();
        return console == 0xFFFFFFFF || console == 0 ? -1 : (int)console;
    }

    /// <summary>Starts the program on the user's screen. Returns the process id.</summary>
    public static int Start(string path, string args)
    {
        int session = FindSession();
        if (session < 0) throw new InvalidOperationException("Nobody is logged in on that PC");

        // plain names like cmd.exe live in System32
        if (!path.Contains('\\') && !path.Contains('/'))
        {
            var sys = Path.Combine(Environment.SystemDirectory, path);
            if (File.Exists(sys)) path = sys;
        }

        IntPtr self = IntPtr.Zero, token = IntPtr.Zero, env = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), MaximumAllowed, out self))
                throw new InvalidOperationException("OpenProcessToken failed: " + Marshal.GetLastWin32Error());
            if (!DuplicateTokenEx(self, MaximumAllowed, IntPtr.Zero, 2, 1, out token))   // impersonation level 2, primary token
                throw new InvalidOperationException("DuplicateTokenEx failed: " + Marshal.GetLastWin32Error());
            if (!SetTokenInformation(token, 12, ref session, sizeof(int)))              // 12 = TokenSessionId
                throw new InvalidOperationException("SetTokenInformation failed: " + Marshal.GetLastWin32Error());
            CreateEnvironmentBlock(out env, token, false);

            var si = new STARTUPINFO { lpDesktop = @"winsta0\default", dwFlags = 1, wShowWindow = 5 };   // show the window
            si.cb = Marshal.SizeOf(si);
            var cmd = new StringBuilder($"\"{path}\" {args}");
            var cwd = Environment.SystemDirectory;
            if (!CreateProcessAsUser(token, null, cmd, IntPtr.Zero, IntPtr.Zero, false,
                    CreateNewConsole | CreateUnicodeEnvironment, env, cwd, ref si, out var pi))
                throw new InvalidOperationException("Could not start the program: Windows error " + Marshal.GetLastWin32Error());
            CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
            return pi.dwProcessId;
        }
        finally
        {
            if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
            if (token != IntPtr.Zero) CloseHandle(token);
            if (self != IntPtr.Zero) CloseHandle(self);
        }
    }
}
