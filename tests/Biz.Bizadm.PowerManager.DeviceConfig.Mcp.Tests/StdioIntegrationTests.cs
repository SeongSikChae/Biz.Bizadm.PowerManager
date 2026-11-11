using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Tests
{
    /// <summary>Mcp 프로젝트 출력 폴더의 exe를 stdio MCP 서버로 실행한다(설계 6절). PC Wi-Fi를 바꾸는 호출은 하지 않는다.</summary>
    [TestClass]
    public sealed class StdioIntegrationTests
    {
        private static readonly string McpExePath = ResolveExePath();

        private string workDir = null!;

        public TestContext TestContext { get; set; } = null!;

        private static string ResolveExePath()
        {
            string path = typeof(StdioIntegrationTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
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

        /// <summary>절대 경로 설정. WifiProfiles가 없어 KMS(자격 증명·DEK)를 쓰지 않는다.</summary>
        private string WriteConfig(bool withOsKms = true)
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
            File.WriteAllText(path, $"ContentRootPath: '{workDir}'\n{osKms}Device:\n  Port: 5000\n");
            return path;
        }

        private Task<McpClient> StartClientAsync() =>
            McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "device-config",
                Command = McpExePath,
                Arguments = ["--config", WriteConfig(), "--log", LogDir],
            }), cancellationToken: TestContext.CancellationToken);

        private static string[] Required(Tool tool) =>
            tool.InputSchema.TryGetProperty("required", out JsonElement required)
                ? required.EnumerateArray().Select(e => e.GetString()!).ToArray()
                : [];

        [TestMethod]
        public async Task Stdio_ListsFourTools_WithAnnotations()
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
            CollectionAssert.AreEquivalent(new[] { "adapterId", "deviceApSsid", "commands", "wifiSsid" },
                properties.EnumerateObject().Select(p => p.Name).ToArray()); // progress·cancellationToken은 schema에 없다
            CollectionAssert.AreEquivalent(new[] { "adapterId", "deviceApSsid", "commands" }, Required(run)); // wifiSsid는 비필수
            CollectionAssert.AreEquivalent(new[] { "adapterId" }, Required(tools.Single(t => t.Name == "scan_device_aps").ProtocolTool));
        }

        [TestMethod]
        public async Task Stdio_ListWifiProfiles_ReturnsEmptySsids()
        {
            await using McpClient client = await StartClientAsync();

            CallToolResult result = await client.CallToolAsync("list_wifi_profiles", cancellationToken: TestContext.CancellationToken);

            Assert.IsFalse(result.IsError ?? false);
            Assert.IsNotNull(result.StructuredContent);
            JsonElement content = JsonSerializer.SerializeToElement(result.StructuredContent);
            Assert.AreEqual(0, content.GetProperty("ssids").GetArrayLength());
        }

        [TestMethod]
        public async Task Stdio_ScanUnknownAdapter_IsError()
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
        public async Task Exe_InvalidConfig_ExitCode2()
        {
            ProcessStartInfo psi = new(McpExePath)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string arg in new[] { "--config", WriteConfig(withOsKms: false), "--log", LogDir })
                psi.ArgumentList.Add(arg);

            using Process process = Process.Start(psi)!;
            try
            {
                process.StandardInput.Close();
                Task<string> stdout = process.StandardOutput.ReadToEndAsync(TestContext.CancellationToken);
                Task<string> stderr = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);
                await process.WaitForExitAsync(TestContext.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.CancellationToken);

                Assert.AreEqual(2, process.ExitCode);
                StringAssert.Contains(await stderr, "Configuration error");
                Assert.AreEqual("", await stdout);
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true); // 시간 초과로 실패해도 서버 프로세스를 남기지 않는다
            }
        }
    }
}
