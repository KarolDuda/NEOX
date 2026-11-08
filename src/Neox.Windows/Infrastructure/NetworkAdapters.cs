using System.Net.NetworkInformation;

namespace Neox.Windows.Infrastructure;

public sealed record AdapterInfo(string Name, string Guid, string Description, string Type, long SpeedMbps, string Status, string[] Dns, string[] Addresses);

public static class NetworkAdapters
{
    /// <summary>Aktywne karty fizyczne (Ethernet / Wi-Fi), bez pętli zwrotnej i tuneli. Bez PowerShella.</summary>
    public static IReadOnlyList<AdapterInfo> GetActive()
    {
        var list = new List<AdapterInfo>();
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus != OperationalStatus.Up) continue;
            if (n.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
                or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT)) continue;
            var props = n.GetIPProperties();
            list.Add(new AdapterInfo(n.Name, n.Id.Trim('{', '}'), n.Description, n.NetworkInterfaceType.ToString(),
                n.Speed > 0 ? n.Speed / 1_000_000 : 0, n.OperationalStatus.ToString(),
                props.DnsAddresses.Select(a => a.ToString()).ToArray(),
                props.UnicastAddresses.Select(a => a.Address.ToString()).ToArray()));
        }
        return list;
    }
}
