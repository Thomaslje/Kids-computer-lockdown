namespace KioskShared;

/// <summary>Dest: "share" (ends up in C:\Share) or "install" (kept for an install job).</summary>
public record UploadStart(string Id, string FileName, long TotalBytes, string Dest);
public record UploadChunk(string Id, long Offset, string B64);
public record UploadFinish(string Id);
/// <summary>Either UploadId (a file sent earlier) or Path (a file already on the PC).</summary>
public record InstallRequest(string? UploadId, string? Path, string Args, int TimeoutMinutes, bool DeleteAfter, bool OnScreen = false);
public record InstallJob(string JobId);
public record InstallStatus(bool Done, bool Success, bool RestartNeeded, string Message);
public record JobRef(string JobId);
public record LaunchRequest(string Path, string Args);

public record ExecRequest(string Shell, string Command, int TimeoutSeconds);
public record ExecPoll(string JobId, int Offset);
public record ExecStatus(bool Done, int ExitCode, string Output, int NextOffset);
public record LaunchAdminRequest(string Path, string Args);

/// <summary>Create (or update the password of) a local account. GroupSid must be one of the groups in AccountGroups.</summary>
public record CreateAccountRequest(string Name, string Password, string GroupSid, bool PasswordNeverExpires);

/// <summary>One local account on a PC, as listed by the agent.</summary>
public record AccountInfo(string Name, bool Enabled, bool IsAdmin);
public record DeleteAccountRequest(string Name, bool DeleteProfile);

public record AccountGroup(string Name, string Sid, string Description);

/// <summary>The built-in local groups of Windows (Microsoft "Well-known SIDs" list) that make sense for a person's account.</summary>
public static class AccountGroups
{
    public static readonly AccountGroup[] All =
    {
        new("Users", "S-1-5-32-545", "Standard user. Can use the PC but not change the system."),
        new("Administrators", "S-1-5-32-544", "Full control of the PC."),
        new("Guests", "S-1-5-32-546", "Very limited, temporary profile."),
        new("Power Users", "S-1-5-32-547", "Legacy group, kept for old programs. About the same as Users."),
        new("Backup Operators", "S-1-5-32-551", "Can back up and restore files regardless of permissions."),
        new("Remote Desktop Users", "S-1-5-32-555", "May log on with Remote Desktop."),
        new("Remote Management Users", "S-1-5-32-580", "May manage the PC with WinRM / PowerShell remoting."),
        new("Network Configuration Operators", "S-1-5-32-556", "May change network settings."),
        new("Performance Monitor Users", "S-1-5-32-558", "May watch performance counters."),
        new("Performance Log Users", "S-1-5-32-559", "May schedule performance logging."),
        new("Event Log Readers", "S-1-5-32-573", "May read the event logs."),
        new("Distributed COM Users", "S-1-5-32-562", "May start and use DCOM objects."),
        new("Hyper-V Administrators", "S-1-5-32-578", "Full control of Hyper-V."),
    };
}
