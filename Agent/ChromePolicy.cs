using Microsoft.Win32;

namespace KioskAgent;

/// <summary>
/// Locks Chrome down through its official machine policies (works in a normal window):
/// no downloads, no local files, no settings/flags/extensions pages, no dev tools, no sign-in.
/// </summary>
static class ChromePolicy
{
    public static void Apply()
    {
        try
        {
            using var k = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Google\Chrome");
            SetDword(k, "DownloadRestrictions", 3);           // block all downloads
            SetDword(k, "DeveloperToolsAvailability", 2);     // dev tools off
            SetDword(k, "IncognitoModeAvailability", 1);      // no incognito
            SetDword(k, "BrowserGuestModeEnabled", 0);
            SetDword(k, "AllowFileSelectionDialogs", 0);      // no file open/save dialogs
            SetDword(k, "BrowserSignin", 0);
            SetDword(k, "SyncDisabled", 1);
            SetDword(k, "PasswordManagerEnabled", 0);
            SetDword(k, "BackgroundModeEnabled", 0);         // Chrome really closes when its window is closed
            SetList(k, "ExtensionInstallBlocklist", new[] { "*" });
            SetList(k, "URLBlocklist", new[]
            {
                "file://*", "chrome://settings", "chrome://flags", "chrome://extensions",
                "chrome://downloads", "chrome://inspect", "view-source:*"
            });
        }
        catch { /* never crash the service over a policy */ }
    }

    /// <summary>Removes only the settings this program added (other Chrome policies on the PC are left alone).</summary>
    public static void Remove()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Google\Chrome", true);
            if (k == null) return;
            foreach (var n in new[]
                { "DownloadRestrictions", "DeveloperToolsAvailability", "IncognitoModeAvailability", "BrowserGuestModeEnabled",
                  "AllowFileSelectionDialogs", "BrowserSignin", "SyncDisabled", "PasswordManagerEnabled", "BackgroundModeEnabled" })
                k.DeleteValue(n, false);
            k.DeleteSubKeyTree("ExtensionInstallBlocklist", false);
            k.DeleteSubKeyTree("URLBlocklist", false);
            bool empty = k.ValueCount == 0 && k.SubKeyCount == 0;
            k.Close();
            if (empty) Registry.LocalMachine.DeleteSubKey(@"SOFTWARE\Policies\Google\Chrome", false);
        }
        catch { }
    }

    static void SetDword(RegistryKey key, string name, int value) =>
        key.SetValue(name, value, RegistryValueKind.DWord);

    static void SetList(RegistryKey parent, string name, string[] items)
    {
        parent.DeleteSubKeyTree(name, false);
        using var sub = parent.CreateSubKey(name);
        for (int i = 0; i < items.Length; i++)
            sub.SetValue((i + 1).ToString(), items[i]);
    }
}
