using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Protocol;
using Biz.Bizadm.PowerManager.DeviceConfig.Tests.TestSupport;
using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Device
{
    /// <summary>
    /// 실장비(B540) 전체 명령. DeviceConfigRunner 전체(실제 PC Wi-Fi + <see cref="EnvironmentSelector"/>)를 실행한다.
    /// 기본 dotnet test에서 제외되며 장치 상태를 바꾸는 명령이 있으므로 하나씩 --filter로 실행한다.
    /// 환경 변수: DEVICE_AP_SSID(필수), ConnectAp는 DEVICE_WIFI_SSID·DEVICE_WIFI_PASS. 값은 로그에 남기지 않는다.
    /// 로그(마스킹됨)는 %TEMP%\device-config-device-tests\&lt;테스트&gt;.log에 남긴다.
    /// </summary>
    [TestClass]
    [TestCategory("Device")]
    [OSCondition(OperatingSystems.Windows)]
    public sealed class RealDeviceTests
    {
        public TestContext TestContext { get; set; } = null!;

        private static string RequiredEnv(string name) =>
            Environment.GetEnvironmentVariable(name) ?? throw new AssertInconclusiveException($"{name} not set");

        private async Task RunAsync(params DeviceCommand[] commands)
        {
            string deviceAp = RequiredEnv("DEVICE_AP_SSID");
            bool connectAp = commands.Contains(DeviceCommand.ConnectAp);
            string? wifiSsid = connectAp ? RequiredEnv("DEVICE_WIFI_SSID") : null;
            DeviceConfigOptions options = new()
            {
                Host = "192.168.244.1",
                Port = 5000,
                Timeout = TimeSpan.FromSeconds(10),
                ApPrefix = "DWD-",
                ApConnectTimeout = TimeSpan.FromSeconds(30),
                Commands = commands,
                WifiProfiles = connectAp ? [new WifiProfile(wifiSsid!, RequiredEnv("DEVICE_WIFI_PASS"))] : [],
                // 서버(#1)가 없으므로 자리표시자 주소(#2 실장비와 같음)
                Settings = new DeviceSettings
                {
                    ApiServerAddr = "api.example.local",
                    ApiServerPort = 18443,
                    ServerAddr = "mqtt.example.local",
                    ServerPort = 8883,
                    SslSupport = true,
                    UserId = "test@example.local",
                    Company = "DAWONDNS",
                    Model = "B540_W",
                    Lati = "",
                    Long = "",
                    Topic = "dwd",
                },
            };
            ListLogger<DeviceConfigRunner> logger = new();
            try
            {
                await new DeviceConfigRunner(HostWifiLoader.CreateForCurrentOs(logger), new EnvironmentSelector(deviceAp, wifiSsid), options, logger)
                    .RunAsync(TestContext.CancellationToken);
            }
            finally
            {
                string dir = Path.Combine(Path.GetTempPath(), "device-config-device-tests");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"{TestContext.TestName}.log"), logger.AllText);
            }
        }

        // 읽기
        [TestMethod] public Task Measurement() => RunAsync(DeviceCommand.Measurement);
        [TestMethod] public Task ScanAp() => RunAsync(DeviceCommand.ScanAp);
        [TestMethod] public Task ApScan() => RunAsync(DeviceCommand.ApScan);

        // 명령마다 재연결·START 반복 확인(릴레이 동작 포함, 원래 상태 ON으로 끝냄)
        [TestMethod] public Task Sequence() => RunAsync(DeviceCommand.Measurement, DeviceCommand.ScanAp, DeviceCommand.OffCmd, DeviceCommand.OnCmd);

        // 상태 변경
        [TestMethod] public Task OnCmd() => RunAsync(DeviceCommand.OnCmd);
        [TestMethod] public Task OffCmd() => RunAsync(DeviceCommand.OffCmd);
        [TestMethod] public Task OverLoad() => RunAsync(DeviceCommand.OverLoad);
        [TestMethod] public Task ConnectAp() => RunAsync(DeviceCommand.ConnectAp);
        [TestMethod] public Task Reboot() => RunAsync(DeviceCommand.Reboot);
        [TestMethod] public Task FactoryReset() => RunAsync(DeviceCommand.FactoryReset);
        [TestMethod] public Task TestFactoryReset() => RunAsync(DeviceCommand.TestFactoryReset);
    }
}
