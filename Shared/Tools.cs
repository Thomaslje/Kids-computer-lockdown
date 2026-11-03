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
