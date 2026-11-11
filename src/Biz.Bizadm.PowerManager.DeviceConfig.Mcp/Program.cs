using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Security;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Configuration;
using System.Configuration;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Mcp
{
    /// <summary>
    /// stdio MCP 서버. stdout은 MCP 프로토콜 전용이므로 모든 메시지·로그는 stderr와 파일로 보낸다(설계 2절).
    /// --config, --log, ContentRootPath는 절대 경로라고 가정한다.
    /// </summary>
    internal class Program
    {
        public sealed class CmdMain
        {
            [Option("config", Required = true, HelpText = "config file path")]
            public string ConfigFilePath { get; set; } = null!;

            [Option("log", Required = true, HelpText = "log dir path")]
            public string LogDirPath { get; set; } = null!;
        }

        static async Task<int> Main(string[] args)
        {
            // Parser.Default의 도움말·인수 오류 출력은 stderr다
            ParserResult<CmdMain> result = Parser.Default.ParseArguments<CmdMain>(args);
            return (int)await result.MapResult(
                cmdMain => RunAsync(args, cmdMain),
                errors => Task.FromResult(errors.IsHelp() || errors.IsVersion()
                    ? ExitCode.Success
                    : ExitCode.InvalidArguments));
        }

        private static async Task<ExitCode> RunAsync(string[] args, CmdMain cmdMain)
        {
            McpConfiguration? mcpConfiguration;
            try
            {
                YamlDotNet.Serialization.Deserializer deserializer = new();
                mcpConfiguration = deserializer.Deserialize<McpConfiguration>(File.ReadAllText(cmdMain.ConfigFilePath));
                ConfigurationValidator.Validate(mcpConfiguration);
                mcpConfiguration.Validate();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                return ExitCode.ConfigurationError;
            }

            IReadOnlyList<WifiProfile> wifiProfiles; // U4에서 tool 서비스 설정으로 등록한다
            try
            {
                wifiProfiles = ProtectSecrets(cmdMain.ConfigFilePath, mcpConfiguration);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                return ExitCode.ConfigurationError;
            }

            IHost host;
            try
            {
                host = CreateHostApplication(args, cmdMain, mcpConfiguration);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Host creation error: {ex}");
                return ExitCode.RuntimeError;
            }

            using (host)
            {
                // RunAsync는 finally에서 Host를 dispose하므로 쓰지 않는다(catch에서 로그를 남길 수 있게 dispose는 using에 맡긴다)
                ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
                try
                {
                    await host.StartAsync();
                    await host.WaitForShutdownAsync(); // stdin이 닫히면 stdio transport가 Host를 정지시킨다. 정지 후 StopAsync까지 호출한다
                    return ExitCode.Success;
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "Host terminated unexpectedly");
                    return ExitCode.RuntimeError;
                }
            }
        }

        private static IReadOnlyList<WifiProfile> ProtectSecrets(string configFilePath, McpConfiguration mcpConfiguration)
        {
            if (!ConfigSecretProtector.HasSecrets(mcpConfiguration))
                return [];
            using ConfigSecretProtector protector = ConfigSecretProtector.Create(mcpConfiguration);
            IReadOnlyList<WifiProfile> profiles = protector.Protect(mcpConfiguration);
            if (protector.Rewrite)
            {
                YamlDotNet.Serialization.Serializer serializer = new();
                File.WriteAllText(configFilePath, serializer.Serialize(mcpConfiguration));
                Console.Error.WriteLine("Configuration file updated with encrypted secrets");
            }
            return profiles;
        }

        private static IHost CreateHostApplication(string[] args, CmdMain cmdMain, McpConfiguration mcpConfiguration)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = args,
                ContentRootPath = mcpConfiguration.ContentRootPath,
            });

            // 기본 Console 로거는 stdout에 쓰므로 제거한다
            builder.Logging.ClearProviders();
            builder.Logging.Services.AddSerilog(configureLogger =>
            {
                configureLogger
                    .MinimumLevel.Information()
                    .Enrich.WithCaller()
                    .WriteTo.Console(
                        Serilog.Events.LogEventLevel.Information,
                        CallerEnricherOutputTemplate.Default,
                        standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose // stdout은 MCP 전용
                    )
                    .WriteTo.File(
                        new DirectoryInfo(cmdMain.LogDirPath).FullName,
                        "Biz.Bizadm.PowerManager.DeviceConfig.Mcp.log",
                        Serilog.Events.LogEventLevel.Information,
                        CallerEnricherOutputTemplate.Default,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 12
                    );
            });

            builder.Services.AddSingleton(sp => HostWifiLoader.CreateForCurrentOs(sp.GetRequiredService<ILogger<DeviceConfigRunner>>()));
            builder.Services.AddMcpServer().WithStdioServerTransport(); // tool 등록은 U4

            return builder.Build();
        }
    }
}
