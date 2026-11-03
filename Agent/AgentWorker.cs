using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using KioskShared;

namespace KioskAgent;

public class AgentWorker : BackgroundService
{
    readonly ConcurrentDictionary<string, long> _nonces = new();

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        NetworkSetup.StartLoop();
        AutoUpdater.StartLoop();
        _ = Task.Run(DefenderExclusions.Ensure);   // before any update can arrive
        ChromePolicy.Apply();
        Updater.CleanupOld();
        FileTransfer.CleanupAll();
        KioskShortcut.Ensure();

        var key = Convert.FromBase64String(File.ReadAllText(Paths.KeyFile).Trim());
        var listener = new TcpListener(IPAddress.Any, Net.Port);
        listener.Start();
        using var reg = stop.Register(() => listener.Stop());

        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(client, key, stop), stop);
        }
    }

    async Task HandleAsync(TcpClient client, byte[] key, CancellationToken stop)
    {
        using var _ = client;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
            cts.CancelAfter(TimeSpan.FromSeconds(60));
            using var ns = client.GetStream();

            var frame = await Net.ReadFrameAsync(ns, cts.Token);
            Request req;
            try { req = Json.From<Request>(Crypto.Decrypt(key, frame))!; }
            catch { return; } // wrong key or garbage: drop the connection silently

            Response resp;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (Math.Abs(now - req.Ts) > 120 || !RememberNonce(req.Nonce, now))
                resp = Fail("Rejected (clock differs by more than 2 minutes, or replayed message)");
            else
                resp = Execute(req);

            await Net.WriteFrameAsync(ns, Crypto.Encrypt(key, Json.To(resp)), cts.Token);
        }
        catch { /* connection problems are not fatal */ }
    }

    bool RememberNonce(string nonce, long now)
    {
        foreach (var kv in _nonces)
            if (now - kv.Value > 300) _nonces.TryRemove(kv.Key, out _);
        return _nonces.TryAdd(nonce, now);
    }

    static Response Ok(string? data = null) => new() { Ok = true, Data = data };
    static Response Fail(string error) => new() { Ok = false, Error = error };

    static Response Execute(Request req)
    {
        try
        {
            switch (req.Cmd)
            {
                case "ping":
                    return Ok(Json.To(new PingInfo(Environment.MachineName, CurrentVersion(), BuildInfo.Revision, TeamViewerId.Get())));

                case "scan":
                    return Ok(Json.To(SteamScanner.Scan()));

                case "push":
                    var cfg = Json.From<KioskConfig>(req.Data ?? "");
                    if (cfg == null) return Fail("Empty configuration");
                    Save(cfg);
                    ChromePolicy.Apply();
                    KioskShortcut.Ensure();
                    return Ok();

                case "uninstall":
                    Uninstaller.Start(Json.From<UninstallRequest>(req.Data ?? "{}")?.RemoveAccount == true);
                    return Ok();

                case "setoptions":
                    return Ok(Json.To(OptionsApplier.Apply(Json.From<KioskOptions>(req.Data ?? "")!)));

                case "tweaks":
                    return Ok(Json.To(TweakApplier.Apply(Json.From<TweaksRequest>(req.Data ?? "")!)));

                case "share-setup":
                    return Ok(Json.To(ShareSetup.Apply()));

                case "upload-start":
                    Activity.Transfer();
                    FileTransfer.Start(Json.From<UploadStart>(req.Data ?? "")!);
                    return Ok();

                case "upload-chunk":
                    Activity.Transfer();
                    FileTransfer.Chunk(Json.From<UploadChunk>(req.Data ?? "")!);
                    return Ok();

                case "upload-finish":
                    return Ok(FileTransfer.Finish(Json.From<UploadFinish>(req.Data ?? "")!.Id));

                case "install-start":
                    return Ok(Json.To(new InstallJob(InstallRunner.Start(Json.From<InstallRequest>(req.Data ?? "")!))));

                case "install-status":
                    return Ok(Json.To(InstallRunner.Status(Json.From<JobRef>(req.Data ?? "")!.JobId)));

                case "launch":
                {
                    var l = Json.From<LaunchRequest>(req.Data ?? "")!;
                    if (string.IsNullOrWhiteSpace(l.Path)) return Fail("No program given");
                    WriteCommand("launchapp", l.Path.Trim(), l.Args);   // started inside the kids' session by the launcher
                    if (l.Path.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        Activity.TrackName(Path.GetFileNameWithoutExtension(l.Path.Trim()));
                    else Activity.Touch(TimeSpan.FromMinutes(15));   // cannot follow a Store app by name
                    return Ok();
                }

                case "exec-start":
                    return Ok(Json.To(new InstallJob(ExecRunner.Start(Json.From<ExecRequest>(req.Data ?? "")!))));

                case "exec-poll":
                {
                    var q = Json.From<ExecPoll>(req.Data ?? "")!;
                    return Ok(Json.To(ExecRunner.Poll(q.JobId, q.Offset)));
                }

                case "exec-stop":
                    ExecRunner.Stop(Json.From<JobRef>(req.Data ?? "")!.JobId);
                    return Ok();

                case "launch-admin":
                {
                    var l = Json.From<LaunchAdminRequest>(req.Data ?? "")!;
                    if (string.IsNullOrWhiteSpace(l.Path)) return Fail("No program given");
                    Activity.TrackPid(SessionLauncher.Start(l.Path.Trim(), l.Args ?? ""));
                    WriteCommand("front");   // the kiosk screen steps back so the tool is visible
                    return Ok();
                }

                case "screenshot":
                {
                    var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
                        "kshot-" + Guid.NewGuid().ToString("N")[..8] + ".png");
                    WriteCommand("screenshot", file);
                    for (int i = 0; i < 48; i++)
                    {
                        Thread.Sleep(250);
                        if (!File.Exists(file)) continue;
                        try
                        {
                            var bytes = File.ReadAllBytes(file);
                            File.Delete(file);
                            return Ok(Convert.ToBase64String(bytes));
                        }
                        catch { /* still being written */ }
                    }
                    return Fail("No screenshot: nobody is logged in on the Kiosk account, or the launcher is not running");
                }

                case "unlock":
                    WriteCommand("unlock");   // the launcher in the kids' session picks this up
                    return Ok();

                case "restart":
                    RunShutdown("/r /t 10 /f /c \"Restart requested from Kiosk Admin\"");
                    return Ok();

                case "shutdown":
                    RunShutdown("/s /t 10 /f /c \"Shutdown requested from Kiosk Admin\"");
                    return Ok();

                case "filehashes":
                    return Ok(Json.To(Updater.Hashes()));

                case "update-start":
                    Activity.Transfer();
                    Updater.Start(Json.From<UpdateStart>(req.Data ?? "")!);
                    return Ok();

                case "update-chunk":
                    Activity.Transfer();
                    Updater.Chunk(Json.From<UpdateChunk>(req.Data ?? "")!);
                    return Ok();

                case "update-apply":
                {
                    var (agent, launcher) = Updater.Apply(Json.From<UpdateApply>(req.Data ?? "")!.Id);
                    KioskShortcut.Ensure();
                    if (launcher) WriteCommand("reloadui");   // kids' session restarts the tile screen with the new code
                    if (agent) RestartSoon();                 // service recovery brings the agent back in ~5 seconds
                    return Ok(Json.To(new { Agent = agent, Launcher = launcher }));
                }

                default:
                    return Fail("Unknown command (is the agent on this PC up to date?)");
            }
        }
        catch (Exception ex) { return Fail(ex.Message); }
    }

    static int CurrentVersion()
    {
        try
        {
            if (File.Exists(Paths.ConfigFile))
                return Json.From<KioskConfig>(File.ReadAllText(Paths.ConfigFile))?.Version ?? 0;
        }
        catch { }
        return 0;
    }

    static void Save(KioskConfig cfg)
    {
        Directory.CreateDirectory(Paths.DataDir);
        var tmp = Paths.ConfigFile + ".tmp";
        File.WriteAllText(tmp, Json.To(cfg));
        File.Move(tmp, Paths.ConfigFile, overwrite: true);
    }

    internal static void WriteCommand(string cmd, string? path = null, string? args = null)
    {
        Directory.CreateDirectory(Paths.DataDir);
        var tmp = Paths.CommandFile + ".tmp";
        var c = new KioskCommand(Guid.NewGuid().ToString("N"), cmd, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), path, args);
        File.WriteAllText(tmp, Json.To(c));
        File.Move(tmp, Paths.CommandFile, overwrite: true);
    }

    // 10 second delay so the reply reaches the admin app before the PC goes down
    static void RunShutdown(string args) =>
        Process.Start(new ProcessStartInfo("shutdown.exe", args) { CreateNoWindow = true, UseShellExecute = false });

    // Exiting with an error (not a clean stop) makes Windows restart the service, now running the new files.
    internal static void RestartSoon() => Task.Run(async () => { await Task.Delay(3000); Environment.Exit(1); });
}
