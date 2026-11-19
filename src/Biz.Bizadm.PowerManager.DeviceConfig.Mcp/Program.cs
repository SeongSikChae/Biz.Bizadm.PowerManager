using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Mcp.Security;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using CommandLine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Configuration;
using System.Configuration;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Mcp
{
    /// <summary>
    /// MCP Streamable HTTP 서버. 루프백(127.0.0.1, ::1)의 Http.Port에만 바인딩하고 endpoint는 /mcp다.
    /// 콘솔 실행과 Windows 서비스 실행을 모두 지원한다(AddWindowsService는 서비스로 시작됐을 때만 동작).
    /// --config, --log, ContentRootPath는 절대 경로라고 가정한다(서비스의 작업 폴더는 System32).
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

            ProtectedSecrets secrets;
            try
            {
                secrets = ProtectSecrets(cmdMain.ConfigFilePath, mcpConfiguration);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                return ExitCode.ConfigurationError;
            }

            WebApplication host;
            try
            {
                host = CreateHostApplication(args, cmdMain, mcpConfiguration, secrets);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Host creation error: {ex}");
                return ExitCode.RuntimeError;
            }

            await using (host)
            {
                // RunAsync는 finally에서 Host를 dispose하므로 쓰지 않는다(catch에서 로그를 남길 수 있게 dispose는 using에 맡긴다)
                ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
                try
                {
                    await host.StartAsync();
                    await host.WaitForShutdownAsync(); // Ctrl+C 또는 SCM 중지로 정지한다. 정지 후 StopAsync까지 호출한다
                    return ExitCode.Success;
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "Host terminated unexpectedly");
                    return ExitCode.RuntimeError;
                }
            }
        }

        private static ProtectedSecrets ProtectSecrets(string configFilePath, McpConfiguration mcpConfiguration)
        {
            if (!ConfigSecretProtector.HasSecrets(mcpConfiguration))
                return ProtectedSecrets.Empty;
            using ConfigSecretProtector protector = ConfigSecretProtector.Create(mcpConfiguration);
            ProtectedSecrets profiles = protector.Protect(mcpConfiguration);
            if (protector.Rewrite)
            {
                YamlDotNet.Serialization.Serializer serializer = new();
                File.WriteAllText(configFilePath, serializer.Serialize(mcpConfiguration));
                Console.Error.WriteLine("Configuration file updated with encrypted secrets");
            }
            return profiles;
        }

        private static WebApplication CreateHostApplication(string[] args, CmdMain cmdMain, McpConfiguration mcpConfiguration, ProtectedSecrets secrets)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = mcpConfiguration.ContentRootPath,
            });

            // 루프백에만 바인딩한다. 코드로 정한 Listen이 ASPNETCORE_URLS 등 다른 URL 설정보다 우선한다
            builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(mcpConfiguration.Http.Port));
            builder.Services.AddWindowsService();

            // 로깅은 Serilog로만 한다
            builder.Logging.ClearProviders();
            builder.Logging.Services.AddSerilog(configureLogger =>
            {
                configureLogger
                    .MinimumLevel.Information()
                    .Enrich.WithCaller()
                    .WriteTo.Console(
                        Serilog.Events.LogEventLevel.Information,
                        CallerEnricherOutputTemplate.Default,
                        standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose
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
            builder.Services.AddSingleton(new DeviceConfigService.ServiceSettings(
                mcpConfiguration.ToOptions(secrets.WifiProfiles, secrets.MqttKey), secrets.WifiProfiles));
            builder.Services.AddSingleton<DeviceConfigService>();
            // SDK 기본값(Stateless)을 쓴다. tool은 서버→클라이언트 요청을 쓰지 않고, 진행 알림은 요청의 응답 스트림으로 간다
            builder.Services.AddMcpServer().WithHttpTransport().WithTools<DeviceConfigTools>();

            WebApplication app = builder.Build();
            app.MapMcp("/mcp");
            return app;
        }
    }
}
