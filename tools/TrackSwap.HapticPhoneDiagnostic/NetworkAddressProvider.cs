using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TrackSwap.HapticPhoneDiagnostic;

internal static class NetworkAddressProvider
{
    public static IReadOnlyList<IPAddress> GetLanAddresses(IPAddress? preferred)
    {
        var addresses = new List<(IPAddress Address, int Priority)>();
        if (preferred != null)
        {
            addresses.Add((preferred, -1));
        }

        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            int priority = adapter.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet
                ? 0
                : 1;
            if (adapter.GetIPProperties().GatewayAddresses.Count == 0)
            {
                priority += 2;
            }

            foreach (UnicastIPAddressInformation item in adapter.GetIPProperties().UnicastAddresses)
            {
                IPAddress address = item.Address;
                if (address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address) &&
                    !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                {
                    addresses.Add((address, priority));
                }
            }
        }

        return addresses
            .OrderBy(item => item.Priority)
            .Select(item => item.Address)
            .Distinct()
            .ToArray();
    }
}
