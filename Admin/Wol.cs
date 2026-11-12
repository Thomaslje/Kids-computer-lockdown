using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace KioskAdmin;

/// <summary>Wake-on-LAN: sends the "magic packet" (6 x FF, then the MAC address 16 times) as UDP broadcast.</summary>
static class Wol
{
    public static bool TryParseMac(string? text, out byte[] mac)
    {
        mac = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(text)) return false;
        var hex = new string(text.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 12) return false;
        mac = Convert.FromHexString(hex);
        return true;
    }

    public static async Task<bool> SendAsync(string macText, string? directedBroadcast)
    {
        if (!TryParseMac(macText, out var mac)) return false;
        var packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int i = 0; i < 16; i++) Array.Copy(mac, 0, packet, 6 + i * 6, 6);

        var targets = new List<IPAddress> { IPAddress.Broadcast };
        if (IPAddress.TryParse(directedBroadcast, out var directed) && !targets.Contains(directed)) targets.Add(directed);

        // one send per local network card, so the packet also leaves on the right card when there are several (VPN, Wi-Fi...)
        var locals = new List<IPAddress>();
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
                foreach (var a in n.GetIPProperties().UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork))
                    locals.Add(a.Address);
        }
        catch { }
        if (locals.Count == 0) locals.Add(IPAddress.Any);

        bool sent = false;
        foreach (var local in locals)
        {
            try
            {
                using var udp = new UdpClient(new IPEndPoint(local, 0)) { EnableBroadcast = true };
                foreach (var t in targets)
                    foreach (var port in new[] { 9, 7 })
                    {
                        await udp.SendAsync(packet, packet.Length, new IPEndPoint(t, port));
                        sent = true;
                    }
            }
            catch { }
        }
        return sent;
    }
}
