using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using KioskShared;

namespace KioskAgent;

/// <summary>Runs a PowerShell or CMD command as SYSTEM in the background (no window); the admin app reads the output as it grows.</summary>
static class ExecRunner
{
    sealed class Job
    {
        public readonly StringBuilder Output = new();
        public bool Done; public int ExitCode; public Process? Proc;
    }

    static readonly ConcurrentDictionary<string, Job> Jobs = new();
    const int MaxChars = 2_000_000;

    public static bool AnyRunning => Jobs.Values.Any(j => !j.Done);

    public static string Start(ExecRequest r)
    {
        var id = Guid.NewGuid().ToString("N");
        var job = new Job();
        Jobs[id] = job;

        ProcessStartInfo psi;
        if (string.Equals(r.Shell, "cmd", StringComparison.OrdinalIgnoreCase))
        {
            psi = new ProcessStartInfo("cmd.exe", "/d /c chcp 65001>nul & " + r.Command);
        }
        else
        {
            var script = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'; " + r.Command;
            psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        }
        psi.UseShellExecute = false; psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.RedirectStandardInput = true;
        psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8;
        psi.WorkingDirectory = Environment.SystemDirectory;

        void Add(string? line)
        {
            if (line == null) return;
            lock (job)
            {
                if (job.Output.Length < MaxChars) job.Output.AppendLine(line);
                else if (job.Output.Length < MaxChars + 100) job.Output.AppendLine("[output cut off]");
            }
        }

        try
        {
            var p = Process.Start(psi)!;
            job.Proc = p;
            p.OutputDataReceived += (_, e) => Add(e.Data);
            p.ErrorDataReceived += (_, e) => Add(e.Data);
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            p.StandardInput.Close();
            var seconds = Math.Clamp(r.TimeoutSeconds <= 0 ? 600 : r.TimeoutSeconds, 5, 3600);
            _ = Task.Run(() =>
            {
                if (!p.WaitForExit(seconds * 1000))
                {
                    try { p.Kill(true); } catch { }
                    Add($"[stopped after {seconds} seconds]");
                    job.ExitCode = -1;
                }
                else { p.WaitForExit(); job.ExitCode = p.ExitCode; }
                job.Done = true;
            });
        }
        catch (Exception ex) { Add("Could not run: " + ex.Message); job.ExitCode = -1; job.Done = true; }
        return id;
    }

    public static ExecStatus Poll(string id, int offset)
    {
        if (!Jobs.TryGetValue(id, out var job)) return new ExecStatus(true, -1, "[unknown job: did the agent restart?]", 0);
        lock (job)
        {
            offset = Math.Clamp(offset, 0, job.Output.Length);
            var text = job.Output.ToString(offset, job.Output.Length - offset);
            return new ExecStatus(job.Done, job.ExitCode, text, job.Output.Length);
        }
    }

    public static void Stop(string id)
    {
        if (Jobs.TryGetValue(id, out var job)) try { job.Proc?.Kill(true); } catch { }
    }
}
