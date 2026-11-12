using System.ComponentModel;
using System.IO;
using KioskShared;

namespace KioskAdmin;

public class PcEntry
{
    public string Host { get; set; } = "";
    public string Mac { get; set; } = "";          // learned from the PC, used for Wake-on-LAN
    public string Broadcast { get; set; } = "";
}

public class SavedProgram
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Args { get; set; } = "";
    public override string ToString() => Name;
}

public class AdminData
{
    public string KeyBase64 { get; set; } = "";
    public List<PcEntry> Pcs { get; set; } = new();
    public KioskConfig Config { get; set; } = new();
    public string? UpdateFolder { get; set; }
    public string? LockHash { get; set; }             // start password of the admin app (salted hash); null = no lock
    public string? UserLockHash { get; set; }         // the friendly password: opens only Restart / Shut down
    public string? GitHubRepo { get; set; }           // "owner/repo"
    public string? GitHubToken { get; set; }          // encrypted for this Windows user
    public bool CheckForUpdates { get; set; } = true;   // look for a new admin app version at start
    public bool AutoUpdateApp { get; set; }             // install it without asking
    public string? SkippedTag { get; set; }             // "don't remind me about this version"
    public List<SavedProgram> Programs { get; set; } = new();
    public string InstallArgs { get; set; } = "";
    public string? InstallFolder { get; set; }
    public List<string>? Tweaks { get; set; }        // ticked tweaks; null = the defaults
    public bool OptBlockCtrlAltDel { get; set; }   // the password is deliberately NOT stored here

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KioskAdmin", "admin.json");

    public static AdminData Load()
    {
        AdminData d;
        try { d = File.Exists(FilePath) ? Json.From<AdminData>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch { d = new(); }
        if (string.IsNullOrEmpty(d.KeyBase64))
        {
            d.KeyBase64 = Convert.ToBase64String(Crypto.NewKey()); // the pairing key, created once
            d.Save();
        }
        return d;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, Json.To(this));
    }
}

/// <summary>One row in the PC list (updates live on screen).</summary>
public class PcItem : INotifyPropertyChanged
{
    string _name = "", _status = "Checking...", _version = "", _software = "", _teamViewer = "";
    public string Host { get; set; } = "";
    public string Mac { get; set; } = "";
    public string Broadcast { get; set; } = "";
    public string Name { get => _name; set { _name = value; Raise(nameof(Name)); } }
    public string Status { get => _status; set { _status = value; Raise(nameof(Status)); } }
    public string Software { get => _software; set { _software = value; Raise(nameof(Software)); } }
    public string TeamViewer { get => _teamViewer; set { _teamViewer = value; Raise(nameof(TeamViewer)); } }
    public string Version { get => _version; set { _version = value; Raise(nameof(Version)); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
