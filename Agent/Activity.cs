using System.Diagnostics;

namespace KioskAgent;

/// <summary>Remembers what the admin app started or transferred, so an automatic update never interrupts it.</summary>
static class Activity
{
    static readonly object Lock = new();
    static readonly List<int> Pids = new();
    static readonly List<string> Names = new();
    static DateTime _busyUntil = DateTime.MinValue;
    static DateTime _lastTransfer = DateTime.MinValue;

    /// <summary>Counts as busy for this long (used when the started program cannot be followed).</summary>
    public static void Touch(TimeSpan span)
    {
        lock (Lock) { var u = DateTime.UtcNow + span; if (u > _busyUntil) _busyUntil = u; }
    }

    public static void Transfer() { lock (Lock) _lastTransfer = DateTime.UtcNow; }
    public static void TrackPid(int pid) { lock (Lock) Pids.Add(pid); }

    /// <summary>A program name without .exe (only for programs that can be told apart by name).</summary>
    public static void TrackName(string name) { lock (Lock) { if (!Names.Contains(name, StringComparer.OrdinalIgnoreCase)) Names.Add(name); } }

    public static bool IsBusy(out string reason)
    {
        reason = "";
        lock (Lock)
        {
            if (DateTime.UtcNow < _busyUntil) { reason = "a program was just started from the admin app"; return true; }
            if (DateTime.UtcNow - _lastTransfer < TimeSpan.FromMinutes(3)) { reason = "a file transfer is in progress"; return true; }
            Pids.RemoveAll(pid => { try { using var p = Process.GetProcessById(pid); return p.HasExited; } catch { return true; } });
            if (Pids.Count > 0) { reason = "a tool started from the admin app is open"; return true; }
            Names.RemoveAll(n => Process.GetProcessesByName(n).Length == 0);
            if (Names.Count > 0) { reason = $"{Names[0]} (started from the admin app) is running"; return true; }
        }
        if (InstallRunner.AnyRunning || ExecRunner.AnyRunning) { reason = "an install or command from the admin app is running"; return true; }
        return false;
    }
}
