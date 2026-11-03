using System.Net;
using System.Net.Sockets;

namespace KioskShared;

public class Request
{
    public string Cmd { get; set; } = "";
    public long Ts { get; set; }
    public string Nonce { get; set; } = "";
    public string? Data { get; set; }
}

public class Response
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? Data { get; set; }
}

public static class Net
{
    public const int Port = 48650;
    const int MaxFrame = 32 * 1024 * 1024;

    public static async Task WriteFrameAsync(Stream s, byte[] payload, CancellationToken ct)
    {
        var len = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(payload.Length));
        await s.WriteAsync(len, ct);
        await s.WriteAsync(payload, ct);
        await s.FlushAsync(ct);
    }

    public static async Task<byte[]> ReadFrameAsync(Stream s, CancellationToken ct)
    {
        var lb = new byte[4];
        await s.ReadExactlyAsync(lb, ct);
        int len = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lb, 0));
        if (len < 0 || len > MaxFrame) throw new InvalidDataException("Bad frame size");
        var buf = new byte[len];
        await s.ReadExactlyAsync(buf, ct);
        return buf;
    }

    /// <summary>Sends one encrypted command to an agent and returns its encrypted reply.</summary>
    public static async Task<Response> CallAsync(string host, byte[] key, string cmd,
        string? data = null, int timeoutMs = 8000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        using var client = new TcpClient();
        await client.ConnectAsync(host, Port, cts.Token);
        using var ns = client.GetStream();
        var req = new Request
        {
            Cmd = cmd,
            Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Nonce = Guid.NewGuid().ToString("N"),
            Data = data
        };
        await WriteFrameAsync(ns, Crypto.Encrypt(key, Json.To(req)), cts.Token);
        var reply = await ReadFrameAsync(ns, cts.Token);
        return Json.From<Response>(Crypto.Decrypt(key, reply))!;
    }
}
