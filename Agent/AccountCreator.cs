using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;
using KioskShared;

namespace KioskAgent;

/// <summary>Creates a local account and puts it in one group. The password goes straight to Windows (no command line, no log).</summary>
static class AccountCreator
{
    const uint UserExists = 2224, NotFound = 2221, AlreadyMember = 1378;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct USER_INFO_1
    {
        public string name; public string password; public uint password_age; public uint priv;
        public string home_dir; public string comment; public uint flags; public string script_path;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct USER_INFO_1003 { public string password; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct USER_INFO_1008 { public uint flags; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct LOCALGROUP_MEMBERS_INFO_3 { public string domainandname; }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    static extern uint NetUserAdd(string? server, uint level, ref USER_INFO_1 buf, out uint parmErr);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    static extern uint NetUserGetInfo(string? server, string user, uint level, out IntPtr buf);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    static extern uint NetUserSetInfo(string? server, string user, uint level, ref USER_INFO_1003 buf, out uint parmErr);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    static extern uint NetUserSetInfo(string? server, string user, uint level, ref USER_INFO_1008 buf, out uint parmErr);
    [DllImport("netapi32.dll")]
    static extern uint NetApiBufferFree(IntPtr buf);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    static extern uint NetLocalGroupAddMembers(string? server, string group, uint level, ref LOCALGROUP_MEMBERS_INFO_3 buf, uint count);

    static readonly string[] Protected = { "administrator", "guest", "defaultaccount", "wdagutilityaccount" };

    static string GroupName(string sid) =>
        ((NTAccount)new SecurityIdentifier(sid).Translate(typeof(NTAccount))).Value.Split('\\').Last();

    static string Explain(uint code) => code switch
    {
        5 => "access denied",
        2245 => "the password does not meet this PC's password rules (too short or too simple)",
        2202 => "invalid user name",
        2226 => "operation not allowed on this account",
        _ => $"Windows error {code}"
    };

    public static List<AccountInfo> List()
    {
        var text = Proc.Ps("""
$admins = @(Get-LocalGroupMember -SID S-1-5-32-544 -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
Get-LocalUser | ForEach-Object { $n = $_.Name; $a = (@($admins | Where-Object { $_ -like ('*\' + $n) })).Count -gt 0; $n + '|' + $_.Enabled + '|' + $a }
""");
        var list = new List<AccountInfo>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Trim().Split('|');
            if (p.Length == 3) list.Add(new AccountInfo(p[0], p[1] == "True", p[2] == "True"));
        }
        return list;
    }

    public static string Delete(DeleteAccountRequest r)
    {
        var name = (r.Name ?? "").Trim();
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9 ._-]{0,19}$")) throw new InvalidOperationException("Invalid account name");
        if (Protected.Contains(name.ToLowerInvariant())) throw new InvalidOperationException($"'{name}' is a built-in account and is not deleted here");
        try
        {
            if (string.Equals(File.ReadAllText(Paths.KioskUserFile).Trim(), name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("That is the kiosk account (use Uninstall kiosk... for that)");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }

        var all = List();
        var me = all.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                 ?? throw new InvalidOperationException($"No account named '{name}' on this PC");
        if (me.IsAdmin && all.Count(a => a.IsAdmin && a.Enabled && !string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) == 0)
            throw new InvalidOperationException("That is the last administrator on this PC and is not deleted");

        string profileNote = "";
        if (r.DeleteProfile)
        {
            var o = Proc.Ps($"$sid = (Get-LocalUser -Name '{me.Name}').SID.Value; $p = Get-CimInstance Win32_UserProfile | Where-Object {{ $_.SID -eq $sid }}; if ($p) {{ try {{ $p | Remove-CimInstance -ErrorAction Stop; 'removed' }} catch {{ 'failed: ' + $_.Exception.Message }} }} else {{ 'none' }}").Trim();
            profileNote = o.StartsWith("failed") ? $" (files not deleted: {o})" : o == "removed" ? ", files deleted" : ", no profile folder";
        }
        Proc.Run("net.exe", true, "user", me.Name, "/delete");
        return $"{me.Name}: account deleted{profileNote}";
    }

    public static string Create(CreateAccountRequest r)
    {
        var name = (r.Name ?? "").Trim();
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9 ._-]{0,19}$") || name.EndsWith('.') || name.EndsWith(' '))
            throw new InvalidOperationException("Account name: 1-20 letters, digits, space . _ - (not ending in a dot or space)");
        if (Protected.Contains(name.ToLowerInvariant())) throw new InvalidOperationException($"'{name}' is a built-in account and is not changed here");
        try
        {
            if (string.Equals(File.ReadAllText(Paths.KioskUserFile).Trim(), name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("That is the kiosk account");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        var group = AccountGroups.All.FirstOrDefault(g => g.Sid == r.GroupSid) ?? throw new InvalidOperationException("Unknown group");
        if (string.IsNullOrEmpty(r.Password)) throw new InvalidOperationException("Password is empty");

        bool existed = NetUserGetInfo(null, name, 1, out var buf) == 0;
        if (existed) NetApiBufferFree(buf);
        uint rc, perr;
        if (!existed)
        {
            var u = new USER_INFO_1 { name = name, password = r.Password, priv = 1, flags = 1 | (r.PasswordNeverExpires ? 0x10000u : 0), home_dir = "", comment = "", script_path = "" };
            rc = NetUserAdd(null, 1, ref u, out perr);
            if (rc != 0) throw new InvalidOperationException("Could not create the account: " + Explain(rc));
        }
        else
        {
            var pw = new USER_INFO_1003 { password = r.Password };
            rc = NetUserSetInfo(null, name, 1003, ref pw, out perr);
            if (rc != 0) throw new InvalidOperationException("Could not set the password: " + Explain(rc));
        }

        void AddTo(string sid)
        {
            var m = new LOCALGROUP_MEMBERS_INFO_3 { domainandname = Environment.MachineName + "\\" + name };
            var c = NetLocalGroupAddMembers(null, GroupName(sid), 3, ref m, 1);
            if (c != 0 && c != AlreadyMember) throw new InvalidOperationException($"Could not add to {group.Name}: " + Explain(c));
        }
        AddTo(group.Sid);
        if (group.Sid != "S-1-5-32-546" && group.Sid != "S-1-5-32-545") AddTo("S-1-5-32-545");   // like a normal account, also a member of Users
        return (existed ? $"{name}: password updated" : $"{name}: account created") + $", member of {group.Name}.";
    }
}
