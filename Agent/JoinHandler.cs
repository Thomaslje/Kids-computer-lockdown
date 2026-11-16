using KioskShared;

namespace KioskAgent;

/// <summary>Lets another admin app fetch the pairing key by proving it knows the joining password (see Shared/Join.cs).</summary>
static class JoinHandler
{
    record Stored(string Salt, string Key);

    static readonly object Gate = new();
    static int _fails;
    static DateTime _lockedUntil = DateTime.MinValue;

    static Stored? Load()
    {
        try { return File.Exists(Paths.JoinFile) ? Json.From<Stored>(File.ReadAllText(Paths.JoinFile)) : null; }
        catch { return null; }
    }

    public static void Set(JoinSetRequest r)
    {
        if (string.IsNullOrEmpty(r.SaltB64) && string.IsNullOrEmpty(r.KeyB64))
        {
            try { File.Delete(Paths.JoinFile); } catch { }
            return;
        }
        if (Convert.FromBase64String(r.SaltB64).Length != 16 || Convert.FromBase64String(r.KeyB64).Length != 32)
            throw new InvalidOperationException("Invalid joining secret");
        Directory.CreateDirectory(Paths.PrivateDir);
        File.WriteAllText(Paths.JoinFile, Json.To(new Stored(r.SaltB64, r.KeyB64)));
        lock (Gate) { _fails = 0; _lockedUntil = DateTime.MinValue; }
    }

    public static bool IsHello(byte[] f) => f.AsSpan().SequenceEqual(Join.Hello);
    public static bool IsJoin(byte[] f) => f.Length > Join.Prefix.Length + 28 && f.AsSpan(0, Join.Prefix.Length).SequenceEqual(Join.Prefix);

    public static byte[] HelloReply()
    {
        var s = Load();
        if (s == null) return new byte[] { 0 };
        var salt = Convert.FromBase64String(s.Salt);
        var reply = new byte[1 + salt.Length];
        reply[0] = 1; salt.CopyTo(reply, 1);
        return reply;
    }

    /// <summary>Returns the encrypted answer, a single byte 2 while locked, or null (no answer) for a wrong password.</summary>
    public static byte[]? TryJoin(byte[] frame, byte[] pairingKey, Func<string, long, bool> remember)
    {
        var s = Load();
        if (s == null) return null;
        lock (Gate) { if (DateTime.UtcNow < _lockedUntil) return new byte[] { 2 }; }

        var jk = Convert.FromBase64String(s.Key);
        Request req;
        try { req = Json.From<Request>(Crypto.Decrypt(jk, frame[Join.Prefix.Length..]))!; }
        catch
        {
            lock (Gate) { if (++_fails >= 5) { _lockedUntil = DateTime.UtcNow.AddMinutes(10); _fails = 0; } }
            return null;
        }
        lock (Gate) _fails = 0;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Response resp = req.Cmd == "join" && Math.Abs(now - req.Ts) <= 120 && remember(req.Nonce, now)
            ? new Response { Ok = true, Data = Convert.ToBase64String(pairingKey) }
            : new Response { Ok = false, Error = "Rejected (clock differs by more than 2 minutes, or replayed message)" };
        return Crypto.Encrypt(jk, Json.To(resp));
    }
}
