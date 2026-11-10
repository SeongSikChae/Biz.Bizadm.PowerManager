using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    /// <param name="HasIPv4">DHCP로 받은 IPv4가 있다(169.254 링크 로컬 제외).</param>
    /// <param name="Gateway">기본 게이트웨이 IPv4. 얻지 못하면 null.</param>
    public sealed record InterfaceAddressState(bool HasIPv4, string? Gateway);

    public interface INetworkInterfaceInspector
    {
        /// <param name="interfaceId"><see cref="HostWifiConnection.InterfaceId"/></param>
        /// <returns>인터페이스가 없으면 null.</returns>
        InterfaceAddressState? Inspect(string interfaceId);
    }

    public sealed class SystemNetworkInterfaceInspector : INetworkInterfaceInspector
    {
        public InterfaceAddressState? Inspect(string interfaceId)
        {
            NetworkInterface? networkInterface = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(ni =>
                string.Equals(ni.Id, interfaceId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ni.Name, interfaceId, StringComparison.Ordinal));
            if (networkInterface is null)
                return null;

            IPInterfaceProperties properties = networkInterface.GetIPProperties();
            bool hasIPv4 = properties.UnicastAddresses.Any(address =>
                address.Address.AddressFamily == AddressFamily.InterNetwork && !IsLinkLocal(address.Address));

            string? gateway;
            try
            {
                gateway = properties.GatewayAddresses
                    .Select(address => address.Address)
                    .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Any))
                    ?.ToString();
            }
            catch (PlatformNotSupportedException)
            {
                gateway = null;
            }
            return new InterfaceAddressState(hasIPv4, gateway);
        }

        private static bool IsLinkLocal(IPAddress address)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] == 169 && bytes[1] == 254;
        }
    }
}
