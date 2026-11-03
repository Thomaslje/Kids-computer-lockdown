using Microsoft.Win32;

namespace KioskAgent;

/// <summary>Reads the TeamViewer ID of this PC from the registry (TeamViewer / TeamViewer Host).</summary>
static class TeamViewerId
{
    static string _cache = "";
    static DateTime _at = DateTime.MinValue;

    public static string Get()
    {
        if (DateTime.UtcNow - _at < TimeSpan.FromMinutes(1)) return _cache;
        _cache = Read();
        _at = DateTime.UtcNow;
        return _cache;
    }

    static string Read()
    {
        try
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                foreach (var path in new[] { @"SOFTWARE\TeamViewer", @"SOFTWARE\WOW6432Node\TeamViewer" })
                {
                    using var k = hklm.OpenSubKey(path);
                    if (k == null) continue;
                    var id = FromKey(k);
                    if (id != "") return id;
                    foreach (var sub in k.GetSubKeyNames())   // older versions keep it under Version15, Version14 ...
                    {
                        using var sk = k.OpenSubKey(sub);
                        id = sk == null ? "" : FromKey(sk);
                        if (id != "") return id;
                    }
                }
            }
        }
        catch { }
        return "";
    }

    static string FromKey(RegistryKey k)
    {
        var v = k.GetValue("ClientID");
        if (v is int i && i > 0) return Format((uint)i);
        if (v is long l && l > 0) return Format((ulong)l);
        if (v is string s && ulong.TryParse(s, out var n) && n > 0) return Format(n);
        return "";
    }

    // 1234567890 -> "1 234 567 890"
    static string Format(ulong id)
    {
        var d = id.ToString();
        var parts = new List<string>();
        for (int end = d.Length; end > 0; end -= 3)
            parts.Insert(0, d[Math.Max(0, end - 3)..end]);
        return string.Join(" ", parts);
    }
}
