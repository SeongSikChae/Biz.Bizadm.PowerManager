using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Device
{
    /// <summary>
    /// 실장비(B540) 테스트. 기본 실행에서 제외한다(환경 변수가 없으면 Inconclusive).
    /// - B540_DEVICE_SSID: PC Wi‑Fi로 이 SSID(정확히 일치)에 접속하고 끝나면 복구한다.
    /// - B540_DEVICE_HOST: PC가 이미 장치 AP에 접속해 있을 때 장치 주소(SSID가 없을 때만).
    /// 장비 상태를 바꾸는 명령(ON/OFF, ConnectAp, Reboot, 초기화)은 여기서 실행하지 않는다.
    /// </summary>
    [TestClass]
    [TestCategory("Device")]
    public sealed class RealDeviceTests
    {
        private const int Port = 5000;
        private static readonly DeviceTimeouts Timeouts = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

        public TestContext TestContext { get; set; } = null!;

        [TestMethod]
        public async Task Handshake_ReturnsMac()
        {
            string mac = await WithSessionAsync(session => Task.FromResult(session.Mac));
            Assert.MatchesRegex("^[0-9a-fA-F]{12}$", mac);
        }

        [TestMethod]
        public async Task Measurement_ReturnsTestFrame()
        {
            DeviceResponse response = await WithSessionAsync(session => session.ExecuteAsync(DeviceCommand.Measurement, TestContext.CancellationToken));
            StringAssert.StartsWith(response.Raw, "[DUT->PC]");
        }

        /// <summary>
        /// 공장검사 SCAN_AP는 '#' 프레임이 아니라 apscan과 같은 JSON으로 응답한다(실장비 확인).
        /// </summary>
        [TestMethod]
        public async Task ScanAp_ReturnsScanApListJson()
        {
            DeviceResponse response = await WithSessionAsync(session => session.ExecuteAsync(DeviceCommand.ScanAp, TestContext.CancellationToken));
            Assert.IsNotEmpty(ApScanParser.Parse(response.Raw));
        }

        /// <summary>
        /// 실장비 ⑤단계. 장치가 주변 AP를 스캔한다(설정 변경 없음). 응답 형식·소요 시간 확인용.
        /// </summary>
        [TestMethod]
        public async Task ApScan_ParsesAtLeastOneAp()
        {
            DateTime start = DateTime.UtcNow;
            DeviceResponse response = await WithSessionAsync(session => session.ExecuteAsync(DeviceCommand.ApScan, TestContext.CancellationToken));
            TestContext.WriteLine($"apscan took {(DateTime.UtcNow - start).TotalSeconds:F1}s (including connect/handshake)");
            Assert.IsNotEmpty(ApScanParser.Parse(response.Raw));
        }

        private async Task<T> WithSessionAsync<T>(Func<DeviceSession, Task<T>> action)
        {
            string? ssid = Environment.GetEnvironmentVariable("B540_DEVICE_SSID");
            string? host = Environment.GetEnvironmentVariable("B540_DEVICE_HOST");
            if (string.IsNullOrEmpty(ssid) && string.IsNullOrEmpty(host))
                Assert.Inconclusive("B540_DEVICE_SSID or B540_DEVICE_HOST not set");

            ILogger logger = new TestContextLogger(TestContext);
            IHostWifi? hostWifi = string.IsNullOrEmpty(ssid) ? null : HostWifiFactory.Create(NullLoggerFactory.Instance);
            DeviceApOptions options = new(ssid ?? "-", host ?? "192.168.244.1", TimeSpan.FromSeconds(30));

            await using DeviceApLocator locator = new(hostWifi, new ExactSsidSelector(ssid), options, logger);
            DeviceEndpoint endpoint = await locator.LocateAsync(TestContext.CancellationToken);
            if (!string.IsNullOrEmpty(ssid) && endpoint.DeviceApSsid != ssid)
                Assert.Inconclusive($"Device AP {ssid} not found by PC Wi-Fi scan");

            await using TcpDeviceChannel channel = await TcpDeviceChannel.ConnectAsync(endpoint.Host, Port, Timeouts.Response, logger, TestContext.CancellationToken);
            DeviceSession session = new(channel, Timeouts, logger);
            await session.HandshakeAsync(TestContext.CancellationToken);
            return await action(session);
        }

        /// <summary>
        /// SSID가 정확히 같은 후보만 고른다(다른 장비에 접속하지 않기 위해).
        /// </summary>
        private sealed class ExactSsidSelector(string? ssid) : IDeviceApSelector
        {
            public Task<HostWifiNetwork> SelectAsync(IReadOnlyList<HostWifiNetwork> candidates, CancellationToken cancellationToken) =>
                Task.FromResult(candidates.FirstOrDefault(candidate => candidate.Ssid == ssid)
                    ?? throw new InvalidOperationException($"Device AP {ssid} is not among the candidates"));
        }

        private sealed class TestContextLogger(TestContext context) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    context.WriteLine($"[{logLevel}] {formatter(state, exception)}{(exception is null ? string.Empty : " " + exception.Message)}");
            }
        }
    }
}
