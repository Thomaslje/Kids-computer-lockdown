using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace KioskShared;

/// <summary>Pushed by the admin app: the joining password, as a salt and a derived key (the PC never sees the password). Empty = turn joining off.</summary>
public record JoinSetRequest(string SaltB64, string KeyB64);

/// <summary>
/// "Joining": a second admin app that does not have the pairing key can fetch it from a PC by proving it knows the joining password.
/// 1) hello (plain): the PC answers with its salt.  2) the admin app derives a key from password + salt and sends a "join" request
/// encrypted with it; the PC answers with the pairing key, encrypted with the same derived key. Wrong guesses lock joining for 10 minutes.
/// </summary>
public static class Join
{
    public static readonly byte[] Hello = Encoding.UTF8.GetBytes("KIOSK-JOIN-HELLO");
    public static readonly byte[] Prefix = Encoding.ASCII.GetBytes("KJ01");
    const int Iterations = 300_000;

    public static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);

    public static JoinSetRequest NewSecret(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return new JoinSetRequest(Convert.ToBase64String(salt), Convert.ToBase64String(Derive(password, salt)));
    }

    /// <summary>Asks one PC for the pairing key (base64). Throws InvalidOperationException with a readable reason.</summary>
    public static async Task<string> JoinAsync(string host, string password, int timeoutMs = 15000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        byte[] salt;
        try
        {
            using var c1 = new TcpClient();
            await c1.ConnectAsync(host, Net.Port, cts.Token);
            var ns = c1.GetStream();
            await Net.WriteFrameAsync(ns, Hello, cts.Token);
            var r = await Net.ReadFrameAsync(ns, cts.Token);
            if (r.Length == 17 && r[0] == 1) salt = r[1..];
            else if (r.Length >= 1 && r[0] == 0) throw new InvalidOperationException("This PC has no joining password set.");
            else throw new InvalidOperationException("Unexpected reply from the PC.");
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            throw new InvalidOperationException("The PC did not answer. Its software may be too old for joining (use the pairing key instead).");
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            throw new InvalidOperationException("Could not reach the PC (is it on, and is the address right?).");
        }

        var jk = Derive(password, salt);
        var req = new Request { Cmd = "join", Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Nonce = Guid.NewGuid().ToString("N") };
        var enc = Crypto.Encrypt(jk, Json.To(req));
        var frame = new byte[Prefix.Length + enc.Length];
        Prefix.CopyTo(frame, 0); enc.CopyTo(frame, Prefix.Length);

        byte[] reply;
        try
        {
            using var c2 = new TcpClient();
            await c2.ConnectAsync(host, Net.Port, cts.Token);
            var ns = c2.GetStream();
            await Net.WriteFrameAsync(ns, frame, cts.Token);
            reply = await Net.ReadFrameAsync(ns, cts.Token);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            throw new InvalidOperationException("Wrong joining password.");
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            throw new InvalidOperationException("Could not reach the PC.");
        }
        if (reply.Length == 1 && reply[0] == 2)
            throw new InvalidOperationException("Too many wrong attempts. This PC refuses joining for 10 minutes.");
        Response resp;
        try { resp = Json.From<Response>(Crypto.Decrypt(jk, reply))!; }
        catch { throw new InvalidOperationException("Wrong joining password."); }
        if (!resp.Ok || string.IsNullOrEmpty(resp.Data)) throw new InvalidOperationException(resp.Error ?? "The PC refused.");
        return resp.Data;
    }
}
