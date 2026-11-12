using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace KioskAgent;

/// <summary>Finds the network card the PC really uses, so the admin app can wake the PC with Wake-on-LAN later.</summary>
static class NicInfo
{
    static readonly string[] Virtual = { "virtual", "hyper-v", "vpn", "tap", "vmware", "vbox", "loopback", "bluetooth", "wan miniport" };

    public static (string Mac, string Broadcast) Primary()
    {
        try
        {
            var best = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.Wireless80211
                            && !Virtual.Any(v => n.Description.Contains(v, StringComparison.OrdinalIgnoreCase)))
                .Select(n => (Nic: n, Props: n.GetIPProperties()))
                .Where(x => x.Props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                .OrderBy(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 0)
                .FirstOrDefault();
            if (best.Nic == null) return ("", "");

            var bytes = best.Nic.GetPhysicalAddress().GetAddressBytes();
            if (bytes.Length != 6) return ("", "");
            var mac = string.Join("-", bytes.Select(b => b.ToString("X2")));

            var ua = best.Props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork && a.IPv4Mask != null);
            if (ua == null) return (mac, "");
            var ip = ua.Address.GetAddressBytes(); var mask = ua.IPv4Mask!.GetAddressBytes();
            var bc = new byte[4];
            for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~mask[i]);
            return (mac, new IPAddress(bc).ToString());
        }
        catch { return ("", ""); }
    }
}
