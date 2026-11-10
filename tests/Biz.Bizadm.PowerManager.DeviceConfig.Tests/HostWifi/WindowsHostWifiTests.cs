using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.Versioning;
using System.Xml.Linq;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.HostWifi
{
    [TestClass]
    [SupportedOSPlatform("windows")]
    [OSCondition(OperatingSystems.Windows)]
    public sealed class WindowsHostWifiTests
    {
        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public void OpenProfileXml_EscapesAndIsOpen()
        {
            XDocument document = XDocument.Parse(WindowsHostWifi.OpenProfileXml("B540-Config-Temp", "A&B<\"C\">"));
            XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";

            Assert.AreEqual("A&B<\"C\">", document.Descendants(ns + "SSID").Single().Element(ns + "name")!.Value);
            Assert.AreEqual("open", document.Descendants(ns + "authentication").Single().Value);
            Assert.AreEqual("none", document.Descendants(ns + "encryption").Single().Value);
        }

        [TestMethod]
        public void InterfaceId_MatchesNetworkInterfaceIdFormat()
        {
            Assert.AreEqual("{0F1E2D3C-4B5A-6978-8796-A5B4C3D2E1F0}",
                WindowsHostWifi.ToInterfaceId(Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0")));
        }

        /// <summary>
        /// 실제 무선 어댑터로 스캔·현재 연결만 조회한다(접속·프로필 변경 없음). B540_MANUAL_WIFI=1 일 때만 실행.
        /// </summary>
        [TestMethod]
        [TestCategory("Manual")]
        public async Task Manual_ScanAndCurrent()
        {
            if (Environment.GetEnvironmentVariable("B540_MANUAL_WIFI") != "1")
                Assert.Inconclusive("B540_MANUAL_WIFI=1 not set");

            WindowsHostWifi wifi = new(NullLogger.Instance);
            try
            {
                IReadOnlyList<HostWifiNetwork> networks = await wifi.ScanAsync(TestContext.CancellationToken);
                TestContext.WriteLine($"Scanned {networks.Count} networks, device AP candidates: {networks.Count(n => n.Ssid.StartsWith("DWD-", StringComparison.Ordinal))}");
                HostWifiConnection? current = await wifi.GetCurrentAsync(TestContext.CancellationToken);
                TestContext.WriteLine($"Current interface: {current?.InterfaceId ?? "none"}, connected: {current is not null}");
                if (current is not null)
                    Assert.IsNotNull(new SystemNetworkInterfaceInspector().Inspect(current.InterfaceId), "NetworkInterface.Id must match the WLAN interface GUID");
            }
            catch (HostWifiException ex)
            {
                // 위치 서비스 권한 거부(5) 등. 결과를 기록하고 실패로 남긴다
                TestContext.WriteLine($"HostWifiException: {ex.Message} / {ex.InnerException?.Message}");
                throw;
            }
        }
    }
}
