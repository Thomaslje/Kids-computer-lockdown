using System.Diagnostics;
using System.Text;

namespace KioskAgent;

static class Proc
{
    /// <summary>Runs a program hidden and returns its output. Throws if it exits with an error and throwOnError is set.</summary>
    public static string Run(string file, bool throwOnError, params string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(120000);
        if (throwOnError && p.ExitCode != 0)
            throw new InvalidOperationException((output + err.Result).Trim() is { Length: > 0 } t ? t : $"{file} exit code {p.ExitCode}");
        return output;
    }

    public static string Ps(string script, bool throwOnError = false) =>
        Run("powershell.exe", throwOnError, "-NoProfile", "-ExecutionPolicy", "Bypass", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
}
