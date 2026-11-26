namespace KioskAgent;

/// <summary>Windows Recovery Environment (WinRE): check it and turn it on, so "restart to the recovery menu" works.</summary>
static class Recovery
{
    // reagentc prints its text in the Windows language, so look for the recovery location instead of the word "Enabled":
    // when WinRE is on, "Windows RE location" holds a path like \\?\GLOBALROOT\device\harddisk0\partition4\Recovery\WindowsRE
    public static bool IsEnabled()
    {
        var text = Proc.Run("reagentc.exe", false, "/info");
        return text.Contains("harddisk", StringComparison.OrdinalIgnoreCase) && text.Contains("WindowsRE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Turns WinRE on. Returns null when it worked, otherwise the message from Windows.</summary>
    public static string? Enable()
    {
        string output;
        try { output = Proc.Run("reagentc.exe", false, "/enable"); }
        catch (Exception ex) { return ex.Message; }
        if (IsEnabled()) return null;
        var t = output.Trim();
        return t.Length > 0 ? t : "Windows could not turn on the recovery environment (the recovery image may be missing).";
    }
}
