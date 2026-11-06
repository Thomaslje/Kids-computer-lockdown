using System.Reflection;

namespace KioskShared;

/// <summary>Version information. Revision is bumped by hand; ReleaseTag and SigningPublicKey are put in by the build (Build.ps1).</summary>
public static class BuildInfo
{
    /// <summary>Shown in the admin app's PC list so you can see which version each PC runs. Also used to know which features a PC has.</summary>
    public const string Revision = "2026-11-06-d";

    /// <summary>The GitHub repository the apps look at for new versions (owner/name). It must be public.</summary>
    public const string GitHubRepo = "Thomaslje/Kids-computer-lockdown";

    /// <summary>The release this build came from (for example build-7), or "dev" for a build made on your own PC.</summary>
    public static string ReleaseTag => Meta("ReleaseTag") ?? "dev";

    /// <summary>Public key (base64) that downloaded updates must be signed with. Empty = this build does not install downloads.</summary>
    public static string SigningPublicKey => Meta("SigningPublicKey") ?? "";

    static string? Meta(string key)
    {
        var v = typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value;
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
