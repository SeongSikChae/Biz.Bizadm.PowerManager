using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests
{
    /// <summary>Mcp 프로젝트 출력 폴더의 exe를 HTTP MCP 서버로 띄우고 /mcp에 접속한다. PC Wi-Fi를 바꾸는 호출은 하지 않는다.</summary>
    [TestClass]
    public sealed class HttpIntegrationTests
    {
        private static readonly string McpExePath = ResolveExePath();

        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

        private string workDir = null!;

        private Process? server;

        private readonly StringBuilder serverOutput = new();

        public TestContext TestContext { get; set; } = null!;

        private static string ResolveExePath()
        {
            string path = typeof(HttpIntegrationTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "McpExePath").Value!;
            return Path.GetFullPath(OperatingSystem.IsWindows() ? path + ".exe" : path);
        }

        [TestInitialize]
        public void Initialize()
        {
            workDir = Path.Combine(Path.GetTempPath(), "DeviceConfig.Mcp.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (server is not null)
            {
                if (!server.HasExited)
                {
                    server.Kill(entireProcessTree: true);
                    server.WaitForExit(10_000);
                }
                server.Dispose();
            }
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (IOException)
            {
                // 서버 프로세스가 로그 파일을 늦게 놓는 경우. 임시 폴더라 남아도 된다
            }
        }

        private string LogDir => Path.Combine(workDir, "log");

        /// <summary>절대 경로 설정. WifiProfiles가 없어 KMS(자격 증명·DEK)를 쓰지 않는다. port가 null이면 Http 섹션을 넣지 않는다.</summary>
        private string WriteConfig(int? port, bool withOsKms = true)
        {
            string path = Path.Combine(workDir, "config.yaml");
            string osKms = withOsKms
                ? """
                  OsKms:
                    CredentialService: https://deviceconfig-mcp-tests.powermanager.local
                    CredentialAccount: mcp-tests
                    SecretsDekFilePath: deviceconfig.dek

                  """
                : "";
            string http = port is int p ? $"Http:\n  Port: {p}\n" : "";
            File.WriteAllText(path, $"ContentRootPath: '{workDir}'\n{osKms}{http}Device:\n  Port: 5000\n");
            return path;
        }

        private static int FreePort()
        {
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private Process StartExe(string configPath)
        {
            ProcessStartInfo psi = new(McpExePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string arg in new[] { "--config", configPath, "--log", LogDir })
                psi.ArgumentList.Add(arg);

            Process process = new() { StartInfo = psi };
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (serverOutput) serverOutput.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (serverOutput) serverOutput.AppendLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            server = process;
            return process;
        }

        private string ServerOutput()
        {
            lock (serverOutput)
                return serverOutput.ToString();
        }

        /// <summary>exe를 띄우고 /mcp에 접속될 때까지 재시도한다.</summary>
        private async Task<McpClient> StartClientAsync()
        {
            int port = FreePort();
            Process process = StartExe(WriteConfig(port));
            Uri endpoint = new($"http://127.0.0.1:{port}/mcp");
            Stopwatch elapsed = Stopwatch.StartNew();
            while (true)
            {
                if (process.HasExited)
                    Assert.Fail($"server exited with {process.ExitCode}:\n{ServerOutput()}");
                try
                {
                    return await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
                    {
                        Name = "device-config",
                        Endpoint = endpoint,
                    }), cancellationToken: TestContext.CancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or McpException && elapsed.Elapsed < StartTimeout)
                {
                    await Task.Delay(200, TestContext.CancellationToken);
                }
            }
        }

        private static string[] Required(Tool tool) =>
            tool.InputSchema.TryGetProperty("required", out JsonElement required)
                ? required.EnumerateArray().Select(e => e.GetString()!).ToArray()
                : [];

        private static string[] Required(JsonElement schema) =>
            schema.TryGetProperty("required", out JsonElement required)
                ? required.EnumerateArray().Select(e => e.GetString()!).ToArray()
                : [];

        /// <summary>null일 수 있는 출력 필드(미연결 어댑터의 currentSsid, 성공 시 error)는 required가 아니어야 한다(#8).</summary>
        [TestMethod]
        public async Task Http_OutputSchema_NullableFieldsAreOptional()
        {
            await using McpClient client = await StartClientAsync();
            IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.CancellationToken);

            JsonElement adapters = tools.Single(t => t.Name == "list_wifi_adapters").ProtocolTool.OutputSchema!.Value;
            JsonElement adapterItem = adapters.GetProperty("properties").GetProperty("adapters").GetProperty("items");
            Assert.IsTrue(adapterItem.GetProperty("properties").TryGetProperty("currentSsid", out _));
            CollectionAssert.DoesNotContain(Required(adapterItem), "currentSsid");
            CollectionAssert.IsSubsetOf(new[] { "id", "name" }, Required(adapterItem));

            JsonElement run = tools.Single(t => t.Name == "run_device_commands").ProtocolTool.OutputSchema!.Value;
            Assert.IsTrue(run.GetProperty("properties").TryGetProperty("error", out _));
            CollectionAssert.DoesNotContain(Required(run), "error");
            CollectionAssert.IsSubsetOf(new[] { "success", "results" }, Required(run));
        }

        [TestMethod]
        public async Task Http_ListsFourTools_WithAnnotations()
        {
            await using McpClient client = await StartClientAsync();

            IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: TestContext.CancellationToken);

            CollectionAssert.AreEquivalent(
                new[] { "list_wifi_adapters", "scan_device_aps", "list_wifi_profiles", "run_device_commands" },
                tools.Select(t => t.Name).ToArray());
            foreach (Tool tool in tools.Select(t => t.ProtocolTool))
            {
                bool isRun = tool.Name == "run_device_commands";
                Assert.AreEqual(!isRun, tool.Annotations?.ReadOnlyHint, tool.Name);
                Assert.AreEqual(isRun, tool.Annotations?.DestructiveHint, tool.Name);
                Assert.IsNotNull(tool.OutputSchema, tool.Name);
            }

            Tool run = tools.Single(t => t.Name == "run_device_commands").ProtocolTool;
            JsonElement properties = run.InputSchema.GetProperty("properties");
            CollectionAssert.AreEquivalent(new[] { "adapterId", "deviceApSsid", "commands", "wifiSsid", "settings" },
                properties.EnumerateObject().Select(p => p.Name).ToArray()); // progress·cancellationToken은 schema에 없다
            CollectionAssert.AreEquivalent(new[] { "adapterId", "deviceApSsid", "commands" }, Required(run)); // wifiSsid·settings는 비필수
            JsonElement settings = properties.GetProperty("settings"); // #9: 필드 모두 선택, camelCase
            CollectionAssert.AreEquivalent(
                new[] { "apiServerAddr", "apiServerPort", "serverAddr", "serverPort", "sslSupport", "userId", "company", "model", "lati", "long", "topic", "protocol" },
                settings.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray());
            Assert.IsEmpty(Required(settings));
            CollectionAssert.AreEquivalent(new[] { "adapterId" }, Required(tools.Single(t => t.Name == "scan_device_aps").ProtocolTool));
        }

        [TestMethod]
        public async Task Http_ListWifiProfiles_ReturnsEmptySsids()
        {
            await using McpClient client = await StartClientAsync();

            CallToolResult result = await client.CallToolAsync("list_wifi_profiles", cancellationToken: TestContext.CancellationToken);

            Assert.IsFalse(result.IsError ?? false);
            Assert.IsNotNull(result.StructuredContent);
            JsonElement content = JsonSerializer.SerializeToElement(result.StructuredContent);
            Assert.AreEqual(0, content.GetProperty("ssids").GetArrayLength());
        }

        [TestMethod]
        public async Task Http_ScanUnknownAdapter_IsError()
        {
            await using McpClient client = await StartClientAsync();

            CallToolResult result = await client.CallToolAsync("scan_device_aps",
                new Dictionary<string, object?> { ["adapterId"] = "nope" }, cancellationToken: TestContext.CancellationToken);

            Assert.IsTrue(result.IsError);
            string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
            TestContext.WriteLine($"isError text: {text}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(text));
            // 원인 문구는 환경마다 다르므로(Adapter not found, WLAN 서비스 오류, not implemented) 원인 없는 일반 문구가 아닌지만 본다
            Assert.AreNotEqual("An error occurred invoking 'scan_device_aps'.", text);
        }

        [TestMethod]
        public async Task Http_RunUnknownSettingsKey_ReturnsErrorWithKey()
        {
            await using McpClient client = await StartClientAsync();

            // SDK 바인딩 경로 확인(#9). 바인딩에서 거부하면 원인 없는 일반 문구가 나가므로 RunResult.error로 받는다.
            // adapterId가 없는 값이라 검사를 통과하더라도 Wi-Fi 접속 전에 끝난다
            CallToolResult result = await client.CallToolAsync("run_device_commands", new Dictionary<string, object?>
            {
                ["adapterId"] = "nope",
                ["deviceApSsid"] = "DWD-NOPE",
                ["commands"] = new[] { "Measurement" },
                ["settings"] = new Dictionary<string, object?> { ["user_id"] = "x" },
            }, cancellationToken: TestContext.CancellationToken);

            Assert.IsFalse(result.IsError ?? false);
            JsonElement content = JsonSerializer.SerializeToElement(result.StructuredContent);
            Assert.IsFalse(content.GetProperty("success").GetBoolean());
            Assert.AreEqual("ArgumentException: Unknown settings keys: user_id (use camelCase names such as userId, apiServerPort)",
                content.GetProperty("error").GetString());
        }

        private async Task AssertConfigurationErrorAsync(string configPath)
        {
            Process process = StartExe(configPath);
            await process.WaitForExitAsync(TestContext.CancellationToken).WaitAsync(StartTimeout, TestContext.CancellationToken);
            process.WaitForExit(); // 비동기 출력 읽기가 끝날 때까지 기다린다

            Assert.AreEqual(2, process.ExitCode);
            StringAssert.Contains(ServerOutput(), "Configuration error");
        }

        [TestMethod]
        public Task Exe_InvalidConfig_ExitCode2() => AssertConfigurationErrorAsync(WriteConfig(FreePort(), withOsKms: false));

        [TestMethod]
        public Task Exe_MissingHttpPort_ExitCode2() => AssertConfigurationErrorAsync(WriteConfig(port: null));
    }
}
