using System.IO;
using Microsoft.Win32;

namespace KioskLauncher;

public static class Log
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KioskLauncher.log");

    public static void Write(string message)
    {
        try { File.AppendAllText(FilePath, $"{DateTime.Now:s}  {message}{Environment.NewLine}"); }
        catch { }
    }
}

/// <summary>Per-user Windows policies that remove the Ctrl+Alt+Del options. Applied for the kiosk account only.</summary>
public static class Lockdown
{
    const string SysPolicy = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    const string ExplorerPolicy = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    static readonly string[] SysValues = { "DisableTaskMgr", "DisableLockWorkstation", "DisableChangePassword" };

    public static void Apply()
    {
        try
        {
            using var sys = Registry.CurrentUser.CreateSubKey(SysPolicy);
            foreach (var n in SysValues) sys.SetValue(n, 1, RegistryValueKind.DWord);
            using var ex = Registry.CurrentUser.CreateSubKey(ExplorerPolicy);
            ex.SetValue("NoLogoff", 1, RegistryValueKind.DWord);
            Log.Write("Lockdown applied");
        }
        catch (Exception e) { Log.Write("Lockdown.Apply failed: " + e); }
    }

    public static void Remove()
    {
        try
        {
            using var sys = Registry.CurrentUser.CreateSubKey(SysPolicy);
            foreach (var n in SysValues) sys.DeleteValue(n, false);
            using var ex = Registry.CurrentUser.CreateSubKey(ExplorerPolicy);
            ex.DeleteValue("NoLogoff", false);
            Log.Write("Lockdown removed");
        }
        catch (Exception e) { Log.Write("Lockdown.Remove failed: " + e); }
    }
}
