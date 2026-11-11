using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests.TestSupport;
using Biz.Bizadm.PowerManager.DeviceConfig.Protocol;
using Biz.Bizadm.PowerManager.DeviceConfig.Tests.TestSupport;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using ModelContextProtocol;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests
{
    [TestClass]
    public sealed class DeviceConfigServiceTests
    {
        private const string Ok = "{\"mac\":\"" + MockDevice.Mac + "\",\"respone\":\"OK\"}";
        private const string DeviceAp = "DWD-LS120_000001";
        private const string RouterPass = "router-secret-pass";
        private static readonly string AdapterId = FakeHostWifi.Adapter.Id;

        private static readonly DeviceSettings Settings = new()
        {
            ApiServerAddr = "api.example.local",
            ApiServerPort = 443,
            ServerAddr = "mqtt.example.local",
            ServerPort = 8883,
            SslSupport = false,
            UserId = "user",
            Company = "DAWONDNS",
            Model = "B540_W",
            Lati = "",
            Long = "",
            Topic = "dwd",
        };

        private static readonly WifiProfile[] Profiles = [new("MyRouter", RouterPass), new("Other", "other-pass")];

        public TestContext TestContext { get; set; } = null!;

        private static string? Respond(string line) => line switch
        {
            "{\"command\":\"apscan\"}" => "{\"SCAN_AP_LIST\":{\"MyRouter:-60\",\"Other:-40\"}}",
            "[DUT<-PC] MEASUREMENT" => "[DUT->PC] MEASUREMENT_W:0.0#",
            "[DUT<-PC] ON_CMD" => "[DUT->PC] ON_CMD_OK#",
            "[DUT<-PC] OFF_CMD" => "[DUT->PC] OFF_CMD_OK#",
            _ when line.Contains("connectap") || line.Contains("\"pass\"") => Ok,
            _ => null, // OverLoad 등: 응답 없음 → 시간 초과
        };

        private static DeviceConfigService.ServiceSettings ServiceSettings(MockDevice device, string host = "192.0.2.1",
            DeviceSettings? settings = null, IReadOnlyList<WifiProfile>? profiles = null, TimeSpan? timeout = null)
        {
            IReadOnlyList<WifiProfile> wifiProfiles = profiles ?? Profiles;
            return new(new DeviceConfigOptions
            {
                Host = host, // 192.0.2.1(TEST-NET-1)은 쓰이면 연결 실패: 게이트웨이(127.0.0.1)를 썼는지 구분
                Port = device.Port,
                Timeout = timeout ?? TimeSpan.FromSeconds(1),
                ApPrefix = "DWD-",
                ApConnectTimeout = TimeSpan.FromSeconds(30),
                Commands = [],
                WifiProfiles = wifiProfiles,
                Settings = settings ?? Settings,
            }, wifiProfiles);
        }

        private static DeviceConfigService Service(IHostWifi hostWifi, DeviceConfigService.ServiceSettings settings, ListLogger<DeviceConfigRunner>? logger = null) =>
            new(hostWifi, settings, logger ?? new ListLogger<DeviceConfigRunner>());

        [TestMethod]
        public async Task ListAdapters_ReturnsCurrentSsid()
        {
            await using MockDevice device = new(Respond);

            DeviceConfigService.AdaptersResult result = await Service(new FakeHostWifi(), ServiceSettings(device)).ListAdaptersAsync(TestContext.CancellationToken);

            Assert.AreEqual(new DeviceConfigService.AdapterInfo(AdapterId, "Test Wi-Fi", "HomeRouter"), result.Adapters.Single());
        }

        [TestMethod]
        public async Task ScanDeviceAps_FiltersPrefix_SortsBySignal()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new() { Scanned = [new("DWD-A", -70), new("HomeRouter", -30), new("DWD-B", -40), new("dwd-c", -20)] };

            DeviceConfigService.DeviceApsResult result = await Service(hostWifi, ServiceSettings(device))
                .ScanDeviceApsAsync(AdapterId.ToUpperInvariant(), TestContext.CancellationToken);

            CollectionAssert.AreEqual(new WifiNetwork[] { new("DWD-B", -40), new("DWD-A", -70) }, result.DeviceAps.ToArray());
        }

        [TestMethod]
        public async Task ScanDeviceAps_UnknownAdapter_Throws()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new();

            ArgumentException ex = await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => Service(hostWifi, ServiceSettings(device)).ScanDeviceApsAsync("nope", TestContext.CancellationToken));

            Assert.AreEqual("Adapter not found: nope", ex.Message);
            CollectionAssert.DoesNotContain(hostWifi.Calls, nameof(IHostWifi.ScanAsync));
        }

        [TestMethod]
        public async Task Run_Success_ReturnsResults_Restores()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new();

            DeviceConfigService.RunResult result = await Service(hostWifi, ServiceSettings(device))
                .RunAsync(AdapterId, DeviceAp, ["OnCmd", "Measurement"], null, null, null, TestContext.CancellationToken);

            Assert.IsTrue(result.Success);
            Assert.IsNull(result.Error);
            CollectionAssert.AreEqual(
                new DeviceConfigService.CommandResult[] { new("OnCmd", "[DUT->PC] ON_CMD_OK#"), new("Measurement", "[DUT->PC] MEASUREMENT_W:0.0#") },
                result.Results.ToArray());
            Assert.AreEqual((FakeHostWifi.Adapter, DeviceAp, TimeSpan.FromSeconds(30)), hostWifi.Connected);
            Assert.AreEqual((FakeHostWifi.Adapter, (string?)"HomeRouter"), hostWifi.Restored);
        }

        [TestMethod]
        public async Task Run_ReportsProgress()
        {
            await using MockDevice device = new(Respond);
            SyncProgress progress = new();

            await Service(new FakeHostWifi(), ServiceSettings(device))
                .RunAsync(AdapterId, DeviceAp, ["OnCmd", "Measurement"], null, null, progress, TestContext.CancellationToken);

            ProgressNotificationValue[] reports = progress.Reports.ToArray();
            CollectionAssert.AreEqual(new float[] { 0, 1, 2 }, reports.Select(r => r.Progress).ToArray());
            Assert.IsTrue(reports.All(r => r.Total == 2));
            CollectionAssert.AreEqual(new[] { "Connecting to device AP", "OnCmd", "Measurement" }, reports.Select(r => r.Message).ToArray());
        }

        [TestMethod]
        public async Task Run_MidFailure_ReturnsCompletedAndError_Restores()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new();

            DeviceConfigService.RunResult result = await Service(hostWifi, ServiceSettings(device))
                .RunAsync(AdapterId, DeviceAp, ["OnCmd", "OverLoad", "Measurement"], null, null, null, TestContext.CancellationToken);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("OnCmd", result.Results.Single().Command);
            StringAssert.StartsWith(result.Error, "TimeoutException: ");
            Assert.IsFalse(device.Received.Any(r => r.Line == "[DUT<-PC] MEASUREMENT"));
            Assert.AreEqual((FakeHostWifi.Adapter, (string?)"HomeRouter"), hostWifi.Restored);
        }

        [TestMethod]
        public async Task Run_ConnectApWithoutSettings_FailsBeforeWifi()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi noSettingsWifi = new();
            FakeHostWifi noProfilesWifi = new();
            DeviceConfigService.ServiceSettings noSettings = ServiceSettings(device) with
            {
                BaseOptions = ServiceSettings(device).BaseOptions with { Settings = null },
            };

            DeviceConfigService.RunResult r1 = await Service(noSettingsWifi, noSettings)
                .RunAsync(AdapterId, DeviceAp, ["ConnectAp"], "MyRouter", null, null, TestContext.CancellationToken);
            DeviceConfigService.RunResult r2 = await Service(noProfilesWifi, ServiceSettings(device, profiles: []))
                .RunAsync(AdapterId, DeviceAp, ["ConnectAp"], "MyRouter", null, null, TestContext.CancellationToken);

            foreach (DeviceConfigService.RunResult r in new[] { r1, r2 })
            {
                Assert.IsFalse(r.Success);
                Assert.AreEqual("ArgumentException: ConnectAp requires Settings and WifiProfiles in the config file", r.Error);
                Assert.IsEmpty(r.Results);
            }
            Assert.IsEmpty(noSettingsWifi.Calls);
            Assert.IsEmpty(noProfilesWifi.Calls);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("Unregistered")]
        [DataRow("myrouter")]
        public async Task Run_ConnectApWithoutOrUnknownWifiSsid_FailsBeforeWifi(string? wifiSsid)
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new();

            DeviceConfigService.RunResult result = await Service(hostWifi, ServiceSettings(device))
                .RunAsync(AdapterId, DeviceAp, ["Measurement", "ConnectAp"], wifiSsid, null, null, TestContext.CancellationToken);

            Assert.IsFalse(result.Success);
            Assert.AreEqual($"ArgumentException: wifiSsid must be one of list_wifi_profiles for ConnectAp: {wifiSsid}", result.Error);
            Assert.IsEmpty(hostWifi.Calls);
            Assert.AreEqual(0, device.ConnectionCount);
        }

        [TestMethod]
        [DataRow("Foo")]
        [DataRow("1")]
        [DataRow("OnCmd,OffCmd")]
        [DataRow("oncmd")]
        public async Task Run_UnknownCommand_Fails(string command)
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new();

            DeviceConfigService.RunResult result = await Service(hostWifi, ServiceSettings(device))
                .RunAsync(AdapterId, DeviceAp, ["OnCmd", command], null, null, null, TestContext.CancellationToken);

            Assert.IsFalse(result.Success);
            Assert.AreEqual($"ArgumentException: Unknown command: {command}", result.Error);
            Assert.IsEmpty(hostWifi.Calls);
        }

        [TestMethod]
        public async Task Run_EmptyCommands_Fails()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new();

            DeviceConfigService.RunResult result = await Service(hostWifi, ServiceSettings(device))
                .RunAsync(AdapterId, DeviceAp, [], null, null, null, TestContext.CancellationToken);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("ArgumentException: commands must have at least one item", result.Error);
            Assert.IsEmpty(hostWifi.Calls);
        }

        [TestMethod]
        public async Task Run_NotImplementedWifi_UsesHost()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new() { ThrowOnListAdapters = new NotImplementedException() };

            DeviceConfigService.RunResult result = await Service(hostWifi, ServiceSettings(device, host: device.Host))
                .RunAsync("ignored", "ignored", ["Measurement"], null, null, null, TestContext.CancellationToken);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(1, device.ConnectionCount);
            Assert.IsNull(hostWifi.Restored);
        }

        [TestMethod]
        public async Task Run_Cancelled_PropagatesOce_Restores()
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            await using MockDevice device = new(line =>
            {
                if (line == "[DUT<-PC] MEASUREMENT")
                {
                    cts.Cancel(); // 장치 AP 접속 뒤, 명령 응답을 기다리는 중에 취소
                    return null;
                }
                return Respond(line);
            });
            FakeHostWifi hostWifi = new();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => Service(hostWifi, ServiceSettings(device, timeout: TimeSpan.FromSeconds(30)))
                    .RunAsync(AdapterId, DeviceAp, ["Measurement"], null, null, null, cts.Token));

            Assert.IsNotNull(hostWifi.Connected);
            Assert.AreEqual((FakeHostWifi.Adapter, (string?)"HomeRouter"), hostWifi.Restored);
        }

        [TestMethod]
        public async Task Tools_NotImplemented_ThrowsMcpException()
        {
            await using MockDevice device = new(Respond);
            DeviceConfigTools listTools = new(Service(new FakeHostWifi { ThrowOnListAdapters = new NotImplementedException() }, ServiceSettings(device)));
            DeviceConfigTools scanTools = new(Service(new FakeHostWifi { ThrowOnScan = new NotImplementedException() }, ServiceSettings(device)));

            McpException e1 = await Assert.ThrowsExactlyAsync<McpException>(() => listTools.ListWifiAdapters(TestContext.CancellationToken));
            McpException e2 = await Assert.ThrowsExactlyAsync<McpException>(() => scanTools.ScanDeviceAps(AdapterId, TestContext.CancellationToken));

            Assert.AreEqual("PC Wi-Fi is not implemented on this OS", e1.Message);
            Assert.AreEqual("PC Wi-Fi is not implemented on this OS", e2.Message);
        }

        [TestMethod]
        public async Task Lookup_WaitsWhileRunInProgress()
        {
            await using MockDevice device = new(Respond);
            GatedHostWifi hostWifi = new(new FakeHostWifi());
            DeviceConfigService service = Service(hostWifi, ServiceSettings(device));

            Task<DeviceConfigService.RunResult> run = service.RunAsync(AdapterId, DeviceAp, ["Measurement"], null, null, null, TestContext.CancellationToken);
            await hostWifi.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
            Task<DeviceConfigService.AdaptersResult> lookup = service.ListAdaptersAsync(TestContext.CancellationToken);
            await Task.Delay(300, TestContext.CancellationToken);

            Assert.IsFalse(lookup.IsCompleted);
            hostWifi.Release.SetResult();
            Assert.IsTrue((await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken)).Success);
            Assert.HasCount(1, (await lookup.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken)).Adapters);
        }

        [TestMethod]
        public async Task Run_ConnectAp_DoesNotLogPass()
        {
            await using MockDevice device = new(Respond);
            ListLogger<DeviceConfigRunner> logger = new();

            DeviceConfigService.RunResult result = await Service(new FakeHostWifi(), ServiceSettings(device), logger)
                .RunAsync(AdapterId, DeviceAp, ["ConnectAp"], "MyRouter", null, null, TestContext.CancellationToken);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(new DeviceConfigService.CommandResult("ConnectAp", "OK (MyRouter)"), result.Results.Single());
            Assert.IsTrue(device.Received.Any(r => r.Line.Contains(RouterPass)), "the device must receive the password");
            Assert.IsFalse(logger.AllText.Contains(RouterPass, StringComparison.Ordinal));
        }

        [TestMethod]
        public async Task Run_ConcurrentCalls_Serialized()
        {
            await using MockDevice device = new(Respond);
            GatedHostWifi hostWifi = new(new FakeHostWifi());
            DeviceConfigService service = Service(hostWifi, ServiceSettings(device));

            Task<DeviceConfigService.RunResult> first = service.RunAsync(AdapterId, DeviceAp, ["Measurement"], null, null, null, TestContext.CancellationToken);
            Task<DeviceConfigService.RunResult> second = service.RunAsync(AdapterId, DeviceAp, ["OnCmd"], null, null, null, TestContext.CancellationToken);
            await hostWifi.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);
            await Task.Delay(300, TestContext.CancellationToken);

            Assert.AreEqual(1, hostWifi.EnteredCount); // 두 번째 호출은 세마포어에서 기다린다
            hostWifi.Release.SetResult();
            DeviceConfigService.RunResult[] results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

            Assert.IsTrue(results.All(r => r.Success));
            Assert.AreEqual(2, hostWifi.EnteredCount);
            Assert.AreEqual(1, hostWifi.MaxConcurrent);
        }

        [TestMethod]
        public async Task ListWifiProfiles_SsidsOnly()
        {
            await using MockDevice device = new(Respond);

            DeviceConfigService.WifiProfilesResult result = new DeviceConfigTools(Service(new FakeHostWifi(), ServiceSettings(device))).ListWifiProfiles();

            CollectionAssert.AreEqual(new[] { "MyRouter", "Other" }, result.Ssids.ToArray());
        }

        private static DeviceConfigService.ServiceSettings WithoutFileSettings(MockDevice device) => ServiceSettings(device) with
        {
            BaseOptions = ServiceSettings(device).BaseOptions with { Settings = null },
        };

        private static readonly DeviceConfigService.SettingsOverride RequiredOverride = new()
        {
            ApiServerAddr = "api.arg.local",
            ApiServerPort = 8443,
            ServerAddr = "mqtt.arg.local",
            ServerPort = 1883,
            UserId = "arg-user",
        };

        /// <summary>장치가 받은 설정 JSON 줄(pass 포함).</summary>
        private static string SettingsLine(MockDevice device) => device.Received.Single(r => r.Line.Contains("\"pass\"")).Line;

        [TestMethod]
        public async Task Run_ConnectAp_SettingsOverride_MergesPerField()
        {
            await using MockDevice device = new(Respond);
            DeviceConfigService.SettingsOverride o = new() { UserId = "dev01", ApiServerAddr = "api.dev01.local", Lati = "37.5" };

            DeviceConfigService.RunResult result = await Service(new FakeHostWifi(), ServiceSettings(device))
                .RunAsync(AdapterId, DeviceAp, ["ConnectAp"], "MyRouter", o, null, TestContext.CancellationToken);

            Assert.IsTrue(result.Success, result.Error);
            string line = SettingsLine(device);
            StringAssert.Contains(line, "\"user_id\":\"dev01\"");               // 인자가 파일 값을 이긴다
            StringAssert.Contains(line, "\"api_server_addr\":\"api.dev01.local\"");
            StringAssert.Contains(line, "\"lati\":\"37.5\"");
            StringAssert.Contains(line, "\"server_addr\":\"mqtt.example.local\""); // 넘기지 않은 필드는 파일 값
            StringAssert.Contains(line, "\"server_port\":\"8883\"");
        }

        [TestMethod]
        public async Task Run_ConnectAp_NoFileSettings_ArgumentComplete_UsesDefaults()
        {
            await using MockDevice device = new(Respond);

            DeviceConfigService.RunResult result = await Service(new FakeHostWifi(), WithoutFileSettings(device))
                .RunAsync(AdapterId, DeviceAp, ["ConnectAp"], "MyRouter", RequiredOverride, null, TestContext.CancellationToken);

            Assert.IsTrue(result.Success, result.Error);
            string line = SettingsLine(device);
            StringAssert.Contains(line, "\"api_server_addr\":\"api.arg.local\"");
            StringAssert.Contains(line, "\"api_server_port\":\"8443\"");
            StringAssert.Contains(line, "\"user_id\":\"arg-user\"");
            StringAssert.Contains(line, "\"company\":\"DAWONDNS\"");
            StringAssert.Contains(line, "\"model\":\"B540_W\"");
            StringAssert.Contains(line, "\"topic\":\"dwd\"");
            StringAssert.Contains(line, "\"ssl_support\":\"no\"");
        }

        [TestMethod]
        public async Task Run_ConnectAp_NoFileSettings_ArgumentMissing_FailsBeforeWifi()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new();

            DeviceConfigService.RunResult result = await Service(hostWifi, WithoutFileSettings(device))
                .RunAsync(AdapterId, DeviceAp, ["ConnectAp"], "MyRouter", new() { UserId = "x" }, null, TestContext.CancellationToken);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("ArgumentException: ConnectAp requires settings: missing apiServerAddr, apiServerPort, serverAddr, serverPort (set Settings in the config file or the settings argument)", result.Error);
            Assert.IsEmpty(hostWifi.Calls);
        }

        [TestMethod]
        public async Task Run_ConnectAp_EmptyOverride_NoFileSettings_ReportsAllMissing()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new();

            DeviceConfigService.RunResult result = await Service(hostWifi, WithoutFileSettings(device))
                .RunAsync(AdapterId, DeviceAp, ["ConnectAp"], "MyRouter", new(), null, TestContext.CancellationToken);

            Assert.AreEqual("ArgumentException: ConnectAp requires settings: missing apiServerAddr, apiServerPort, serverAddr, serverPort, userId (set Settings in the config file or the settings argument)", result.Error);
            Assert.IsEmpty(hostWifi.Calls);
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(65536)]
        public async Task Run_ConnectAp_SettingsPortOutOfRange_FailsBeforeWifi(int port)
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi noFileWifi = new();
            FakeHostWifi fileWifi = new();

            DeviceConfigService.RunResult r1 = await Service(noFileWifi, WithoutFileSettings(device))
                .RunAsync(AdapterId, DeviceAp, ["ConnectAp"], "MyRouter", RequiredOverride with { ApiServerPort = port }, null, TestContext.CancellationToken);
            DeviceConfigService.RunResult r2 = await Service(fileWifi, ServiceSettings(device))
                .RunAsync(AdapterId, DeviceAp, ["ConnectAp"], "MyRouter", new() { ServerPort = port }, null, TestContext.CancellationToken);

            Assert.AreEqual($"ArgumentException: settings.apiServerPort out of range: {port}", r1.Error);
            Assert.AreEqual($"ArgumentException: settings.serverPort out of range: {port}", r2.Error);
            Assert.IsEmpty(noFileWifi.Calls);
            Assert.IsEmpty(fileWifi.Calls);
        }

        [TestMethod]
        public async Task Run_ConnectAp_OverrideWithoutProfiles_KeepsLegacyMessage()
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new();

            DeviceConfigService.RunResult result = await Service(hostWifi, ServiceSettings(device, profiles: []))
                .RunAsync(AdapterId, DeviceAp, ["ConnectAp"], "MyRouter", RequiredOverride, null, TestContext.CancellationToken);

            Assert.AreEqual("ArgumentException: ConnectAp requires Settings and WifiProfiles in the config file", result.Error);
            Assert.IsEmpty(hostWifi.Calls);
        }

        [TestMethod]
        public async Task Run_NoConnectAp_SettingsIgnored()
        {
            await using MockDevice device = new(Respond);

            DeviceConfigService.RunResult result = await Service(new FakeHostWifi(), WithoutFileSettings(device))
                .RunAsync(AdapterId, DeviceAp, ["Measurement"], null, new() { ApiServerPort = 0 }, null, TestContext.CancellationToken);

            Assert.IsTrue(result.Success, result.Error);
        }

        [TestMethod]
        public void SettingsOverride_Deserialize_CamelCase()
        {
            DeviceConfigService.SettingsOverride? o = JsonSerializer.Deserialize<DeviceConfigService.SettingsOverride>(
                """{"userId":"x","apiServerPort":443,"lati":"37.5","sslSupport":true}""", McpJsonUtilities.DefaultOptions);

            Assert.AreEqual(new DeviceConfigService.SettingsOverride { UserId = "x", ApiServerPort = 443, Lati = "37.5", SslSupport = true }, o);
        }

        [TestMethod]
        [DataRow("""["ConnectAp"]""")]
        [DataRow("""["Measurement"]""")]
        public async Task Run_UnknownSettingsKey_FailsBeforeWifi(string commandsJson)
        {
            await using MockDevice device = new(Respond);
            FakeHostWifi hostWifi = new();
            DeviceConfigService.SettingsOverride o = JsonSerializer.Deserialize<DeviceConfigService.SettingsOverride>(
                """{"user_id":"dev01","userId":"ok"}""", McpJsonUtilities.DefaultOptions)!;

            DeviceConfigService.RunResult result = await Service(hostWifi, ServiceSettings(device))
                .RunAsync(AdapterId, DeviceAp, JsonSerializer.Deserialize<string[]>(commandsJson)!, "MyRouter", o, null, TestContext.CancellationToken);

            Assert.IsFalse(result.Success);
            Assert.AreEqual("ArgumentException: Unknown settings keys: user_id (use camelCase names such as userId, apiServerPort)", result.Error);
            Assert.IsEmpty(hostWifi.Calls);
            Assert.AreEqual(0, device.ConnectionCount);
        }

        [TestMethod]
        public async Task Tools_LookupFailure_ThrowsMcpExceptionWithMessage()
        {
            await using MockDevice device = new(Respond);
            DeviceConfigTools tools = new(Service(new FakeHostWifi { ThrowOnListAdapters = new InvalidOperationException("WLAN service stopped") }, ServiceSettings(device)));

            McpException listError = await Assert.ThrowsExactlyAsync<McpException>(() => tools.ListWifiAdapters(TestContext.CancellationToken));
            McpException scanError = await Assert.ThrowsExactlyAsync<McpException>(
                () => new DeviceConfigTools(Service(new FakeHostWifi(), ServiceSettings(device))).ScanDeviceAps("nope", TestContext.CancellationToken));

            Assert.AreEqual("InvalidOperationException: WLAN service stopped", listError.Message);
            Assert.AreEqual("ArgumentException: Adapter not found: nope", scanError.Message);
        }
    }
}
