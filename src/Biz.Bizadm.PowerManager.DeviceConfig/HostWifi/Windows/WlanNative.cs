using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Windows
{
    /// <summary>
    /// wlanapi.dll P/Invoke. 구조체 전체 마샬링 대신 문서화된 고정 오프셋에서 필요한 필드만 읽는다.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static partial class WlanNative
    {
        private const string Library = "wlanapi.dll";

        public const uint ClientVersion = 2;
        public const int ErrorInvalidState = 5023;          // 연결되지 않은 인터페이스에 current_connection 조회
        public const int OpcodeCurrentConnection = 7;       // wlan_intf_opcode_current_connection
        public const int ConnectionModeProfile = 0;         // wlan_connection_mode_profile
        public const int BssTypeInfrastructure = 1;         // dot11_BSS_type_infrastructure
        public const uint AvailableNetworkConnected = 1;    // WLAN_AVAILABLE_NETWORK_CONNECTED

        // WLAN_INTERFACE_INFO_LIST / WLAN_AVAILABLE_NETWORK_LIST: dwNumberOfItems(4) + dwIndex(4) + items
        public const int ListHeaderSize = 8;
        // WLAN_INTERFACE_INFO: Guid(16) + WCHAR[256](512) + state(4)
        public const int InterfaceInfoSize = 532;
        // WLAN_AVAILABLE_NETWORK: 628 bytes
        public const int AvailableNetworkSize = 628;
        public const int AvailableNetworkSsidOffset = 512;       // DOT11_SSID { uSSIDLength(4), ucSSID[32] }
        public const int AvailableNetworkSignalOffset = 604;     // wlanSignalQuality (0~100)
        public const int AvailableNetworkFlagsOffset = 620;      // dwFlags
        // WLAN_CONNECTION_ATTRIBUTES: isState(4) + mode(4) + strProfileName WCHAR[256] + WLAN_ASSOCIATION_ATTRIBUTES(DOT11_SSID ...)
        public const int ConnectionStateOffset = 0;
        public const int InterfaceStateConnected = 1;       // wlan_interface_state_connected
        public const int ConnectionProfileNameOffset = 8;
        public const int ConnectionSsidOffset = 520;

        [StructLayout(LayoutKind.Sequential)]
        public struct WlanConnectionParameters
        {
            public int ConnectionMode;
            public nint Profile;
            public nint Dot11Ssid;
            public nint DesiredBssidList;
            public int BssType;
            public uint Flags;
        }

        [LibraryImport(Library)]
        public static partial int WlanOpenHandle(uint dwClientVersion, nint pReserved, out uint pdwNegotiatedVersion, out nint phClientHandle);

        [LibraryImport(Library)]
        public static partial int WlanCloseHandle(nint hClientHandle, nint pReserved);

        [LibraryImport(Library)]
        public static partial void WlanFreeMemory(nint pMemory);

        [LibraryImport(Library)]
        public static partial int WlanEnumInterfaces(nint hClientHandle, nint pReserved, out nint ppInterfaceList);

        [LibraryImport(Library)]
        public static partial int WlanScan(nint hClientHandle, ref Guid pInterfaceGuid, nint pDot11Ssid, nint pIeData, nint pReserved);

        [LibraryImport(Library)]
        public static partial int WlanGetAvailableNetworkList(nint hClientHandle, ref Guid pInterfaceGuid, uint dwFlags, nint pReserved, out nint ppAvailableNetworkList);

        [LibraryImport(Library)]
        public static partial int WlanQueryInterface(nint hClientHandle, ref Guid pInterfaceGuid, int opCode, nint pReserved,
            out uint pdwDataSize, out nint ppData, out int pWlanOpcodeValueType);

        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf16)]
        public static partial int WlanSetProfile(nint hClientHandle, ref Guid pInterfaceGuid, uint dwFlags, string strProfileXml,
            string? strAllUserProfileSecurity, [MarshalAs(UnmanagedType.Bool)] bool bOverwrite, nint pReserved, out uint pdwReasonCode);

        [LibraryImport(Library)]
        public static partial int WlanConnect(nint hClientHandle, ref Guid pInterfaceGuid, ref WlanConnectionParameters pConnectionParameters, nint pReserved);

        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf16)]
        public static partial int WlanDeleteProfile(nint hClientHandle, ref Guid pInterfaceGuid, string strProfileName, nint pReserved);

        public static void ThrowIfFailed(int result, string operation)
        {
            if (result != 0)
                throw new HostWifiException($"{operation} failed: {result}", new Win32Exception(result));
        }

        /// <summary>
        /// DOT11_SSID { uSSIDLength, ucSSID[32] }를 UTF-8 문자열로 읽는다.
        /// </summary>
        public static string ReadSsid(nint dot11Ssid)
        {
            int length = Math.Clamp(Marshal.ReadInt32(dot11Ssid), 0, 32);
            byte[] bytes = new byte[length];
            Marshal.Copy(dot11Ssid + 4, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
    }

    /// <summary>
    /// wlanapi 클라이언트 핸들.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class WlanClient : IDisposable
    {
        private readonly nint handle;

        private WlanClient(nint handle) => this.handle = handle;

        public static WlanClient Open()
        {
            WlanNative.ThrowIfFailed(WlanNative.WlanOpenHandle(WlanNative.ClientVersion, 0, out _, out nint handle), "WlanOpenHandle");
            return new WlanClient(handle);
        }

        public IReadOnlyList<Guid> GetInterfaces()
        {
            WlanNative.ThrowIfFailed(WlanNative.WlanEnumInterfaces(handle, 0, out nint list), "WlanEnumInterfaces");
            try
            {
                int count = Marshal.ReadInt32(list);
                List<Guid> result = [];
                for (int i = 0; i < count; i++)
                    result.Add(Marshal.PtrToStructure<Guid>(list + WlanNative.ListHeaderSize + i * WlanNative.InterfaceInfoSize));
                return result;
            }
            finally
            {
                WlanNative.WlanFreeMemory(list);
            }
        }

        public void Scan(Guid interfaceGuid) =>
            WlanNative.ThrowIfFailed(WlanNative.WlanScan(handle, ref interfaceGuid, 0, 0, 0), "WlanScan");

        public IReadOnlyList<HostWifiNetwork> GetAvailableNetworks(Guid interfaceGuid)
        {
            WlanNative.ThrowIfFailed(WlanNative.WlanGetAvailableNetworkList(handle, ref interfaceGuid, 0, 0, out nint list), "WlanGetAvailableNetworkList");
            try
            {
                int count = Marshal.ReadInt32(list);
                List<HostWifiNetwork> result = [];
                for (int i = 0; i < count; i++)
                {
                    nint item = list + WlanNative.ListHeaderSize + i * WlanNative.AvailableNetworkSize;
                    string ssid = WlanNative.ReadSsid(item + WlanNative.AvailableNetworkSsidOffset);
                    if (ssid.Length > 0)
                        result.Add(new HostWifiNetwork(ssid, Marshal.ReadInt32(item + WlanNative.AvailableNetworkSignalOffset)));
                }
                return result;
            }
            finally
            {
                WlanNative.WlanFreeMemory(list);
            }
        }

        /// <summary>
        /// 현재 연결의 (프로필 이름, SSID). 연결 완료(connected) 상태가 아니면 null(연결 시도 중에도 SSID가 보일 수 있다).
        /// </summary>
        public (string ProfileName, string Ssid)? GetCurrentConnection(Guid interfaceGuid)
        {
            int result = WlanNative.WlanQueryInterface(handle, ref interfaceGuid, WlanNative.OpcodeCurrentConnection, 0, out _, out nint data, out _);
            if (result == WlanNative.ErrorInvalidState)
                return null;
            WlanNative.ThrowIfFailed(result, "WlanQueryInterface(current_connection)");
            try
            {
                if (Marshal.ReadInt32(data + WlanNative.ConnectionStateOffset) != WlanNative.InterfaceStateConnected)
                    return null;
                string profile = Marshal.PtrToStringUni(data + WlanNative.ConnectionProfileNameOffset) ?? string.Empty;
                return (profile, WlanNative.ReadSsid(data + WlanNative.ConnectionSsidOffset));
            }
            finally
            {
                WlanNative.WlanFreeMemory(data);
            }
        }

        public void SetProfile(Guid interfaceGuid, string profileXml)
        {
            int result = WlanNative.WlanSetProfile(handle, ref interfaceGuid, 0, profileXml, null, true, 0, out uint reason);
            if (result != 0)
                throw new HostWifiException($"WlanSetProfile failed: {result}, reason {reason}", new System.ComponentModel.Win32Exception(result));
        }

        public void Connect(Guid interfaceGuid, string profileName)
        {
            nint profile = Marshal.StringToHGlobalUni(profileName);
            try
            {
                WlanNative.WlanConnectionParameters parameters = new()
                {
                    ConnectionMode = WlanNative.ConnectionModeProfile,
                    Profile = profile,
                    BssType = WlanNative.BssTypeInfrastructure,
                };
                WlanNative.ThrowIfFailed(WlanNative.WlanConnect(handle, ref interfaceGuid, ref parameters, 0), "WlanConnect");
            }
            finally
            {
                Marshal.FreeHGlobal(profile);
            }
        }

        public void DeleteProfile(Guid interfaceGuid, string profileName) =>
            WlanNative.ThrowIfFailed(WlanNative.WlanDeleteProfile(handle, ref interfaceGuid, profileName, 0), "WlanDeleteProfile");

        public void Dispose() => WlanNative.WlanCloseHandle(handle, 0);
    }
}
