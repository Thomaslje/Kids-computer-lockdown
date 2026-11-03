using System.IO;
using System.Diagnostics;
using KioskShared;
using Microsoft.Win32;

namespace KioskLauncher;

/// <summary>
/// Windows starts this exe as the shell. Without arguments it acts as a supervisor:
/// it runs the real UI (--ui) and restarts it if it ever crashes.
/// Exit code 100 = admin chose to leave kiosk mode -> hand over to the normal Windows desktop.
/// "KioskLauncher.exe --return" (the desktop icon) tells the supervisor to take the screen back from Explorer.
/// Other Windows accounts (not the kiosk user) get the normal desktop straight away.
/// Test on any PC with:  KioskLauncher.exe --ui --windowed
/// </summary>
public static class Program
{
    const int ExitToDesktop = 100;

    // Per-session event: the "Return to Kiosk" icon sets it, the supervisor waits for it while Explorer is showing
    const string ReturnEventName = @"Local\KioskSuiteReturnToKiosk";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--ui"))
            return RunUi(args.Contains("--windowed"));

        if (args.Contains("--return"))
        {
            if (EventWaitHandle.TryOpenExisting(ReturnEventName, out var h)) { h.Set(); h.Dispose(); }
            return 0;
        }

        if (!IsKioskUser())
        {
            RunExplorerAsShell(false, null);
            return 0;
        }

        var me = Environment.ProcessPath!;
        using var returnSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ReturnEventName);
        Log.Write("Supervisor started for " + Environment.UserName);

        while (true)
        {
            Lockdown.Apply();
            RunUiUntilDesktop(me);

            Lockdown.Remove();
            if (!RunExplorerAsShell(true, returnSignal)) return 0;   // Explorer was closed: end the session

            Log.Write("Returning to the kiosk");
            KillExplorer();
        }
    }

    // Runs the tile screen and restarts it if it crashes. Returns when the admin chose "leave kiosk mode".
    static void RunUiUntilDesktop(string me)
    {
        while (true)
        {
            using var p = Process.Start(new ProcessStartInfo(me, "--ui") { UseShellExecute = false })!;
            p.WaitForExit();
            Log.Write("UI exited with code " + p.ExitCode);
            if (p.ExitCode == ExitToDesktop) return;
            Thread.Sleep(1000); // crashed or killed: start again
        }
    }

    static int RunUi(bool windowed)
    {
        var app = new System.Windows.Application();
        app.DispatcherUnhandledException += (_, e) => { Log.Write("UI error: " + e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Fatal: " + e.ExceptionObject);
        return app.Run(new MainWindow(windowed));
    }

    static bool IsKioskUser()
    {
        try
        {
            if (!File.Exists(Paths.KioskUserFile)) return false;
            var name = File.ReadAllText(Paths.KioskUserFile).Trim();
            return name.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    const string WinlogonKey = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";

    static List<Process> SessionExplorers()
    {
        int session = Process.GetCurrentProcess().SessionId;
        return Process.GetProcessesByName("explorer").Where(p => p.SessionId == session).ToList();
    }

    static void KillExplorer()
    {
        foreach (var p in SessionExplorers())
        {
            try { p.Kill(); p.WaitForExit(3000); } catch (Exception e) { Log.Write("Could not close Explorer: " + e.Message); }
        }
    }

    // Explorer only becomes the real desktop/taskbar when this user's "Shell" setting is not our launcher.
    // So: remove the per-user Shell value, start Explorer, then put the value back once Explorer is up
    // (so the next login is a kiosk again). The session ends when this process ends, so stay alive
    // while Explorer runs. Returns true when the "Return to Kiosk" icon asked for the kiosk back,
    // false when Explorer ended on its own.
    static bool RunExplorerAsShell(bool isKioskAccount, EventWaitHandle? returnSignal)
    {
        string? saved = null;
        try
        {
            if (isKioskAccount)
            {
                using var k = Registry.CurrentUser.CreateSubKey(WinlogonKey);
                saved = k.GetValue("Shell") as string;
                k.DeleteValue("Shell", false);
            }
        }
        catch (Exception e) { Log.Write("Could not clear Shell value: " + e.Message); }

        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        Thread.Sleep(10000);

        try
        {
            if (saved != null)
            {
                using var k = Registry.CurrentUser.CreateSubKey(WinlogonKey);
                k.SetValue("Shell", saved, RegistryValueKind.String);
            }
        }
        catch (Exception e) { Log.Write("Could not restore Shell value: " + e.Message); }

        returnSignal?.Reset();   // ignore clicks from before the desktop was ready
        while (SessionExplorers().Count > 0)
        {
            if (returnSignal != null) { if (returnSignal.WaitOne(2000)) return true; }
            else Thread.Sleep(2000);
        }
        return false;
    }
}
