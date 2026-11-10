using Biz.Bizadm.PowerManager.DeviceConfig.App.Security;
using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Protocol;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Configuration;
using System.Configuration;

namespace Biz.Bizadm.PowerManager.DeviceConfig.App
{
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
            ParserResult<CmdMain> result = Parser.Default.ParseArguments<CmdMain>(args);
            return (int)await result.MapResult(
                cmdMain => RunAsync(args, cmdMain),
                errors => Task.FromResult(errors.IsHelp() || errors.IsVersion()
                    ? ExitCode.Success
                    : ExitCode.InvalidArguments));
        }

        private static async Task<ExitCode> RunAsync(string[] args, CmdMain cmdMain)
        {
            AppConfiguration? appConfiguration;
            try
            {
                YamlDotNet.Serialization.Deserializer deserializer = new();
                appConfiguration = deserializer.Deserialize<AppConfiguration>(File.ReadAllText(cmdMain.ConfigFilePath));
                ConfigurationValidator.Validate(appConfiguration);
                appConfiguration.Validate();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                return ExitCode.ConfigurationError;
            }

            IReadOnlyList<WifiProfile> wifiProfiles;
            try
            {
                wifiProfiles = ProtectSecrets(cmdMain.ConfigFilePath, appConfiguration);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Configuration error: {ex.Message}");
                return ExitCode.ConfigurationError;
            }

            IHost host;
            try
            {
                host = CreateHostApplication(args, cmdMain, appConfiguration, wifiProfiles);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Host creation error: {ex}");
                return ExitCode.RuntimeError;
            }

            using (host)
            {
                ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
                try
                {
                    await host.StartAsync();
                    // 명령 실패를 종료 코드 3으로 내기 위해 BackgroundService 대신 Runner를 직접 실행한다(설계 9절)
                    IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                    await host.Services.GetRequiredService<DeviceConfigRunner>().RunAsync(lifetime.ApplicationStopping);
                    await host.StopAsync();
                    return ExitCode.Success;
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "Host terminated unexpectedly");
                    return ExitCode.RuntimeError;
                }
            }
        }

        private static IReadOnlyList<WifiProfile> ProtectSecrets(string configFilePath, AppConfiguration appConfiguration)
        {
            if (!ConfigSecretProtector.HasSecrets(appConfiguration))
                return [];
            using ConfigSecretProtector protector = ConfigSecretProtector.Create(appConfiguration);
            IReadOnlyList<WifiProfile> profiles = protector.Protect(appConfiguration);
            if (protector.Rewrite)
            {
                YamlDotNet.Serialization.Serializer serializer = new();
                File.WriteAllText(configFilePath, serializer.Serialize(appConfiguration));
                Console.WriteLine("Configuration file updated with encrypted secrets");
            }
            return profiles;
        }

        private static DeviceConfigOptions CreateOptions(AppConfiguration config, IReadOnlyList<WifiProfile> wifiProfiles) => new()
        {
            Host = config.Device.Host,
            Port = config.Device.Port,
            Timeout = TimeSpan.FromSeconds(config.Device.TimeoutSeconds),
            ApPrefix = config.Device.ApPrefix,
            ApConnectTimeout = TimeSpan.FromSeconds(config.Device.ApConnectTimeoutSeconds),
            Commands = config.Commands,
            WifiProfiles = wifiProfiles,
            Settings = config.Settings is not { } s ? null : new DeviceSettings
            {
                ApiServerAddr = s.ApiServerAddr,
                ApiServerPort = s.ApiServerPort,
                ServerAddr = s.ServerAddr,
                ServerPort = s.ServerPort,
                SslSupport = s.SslSupport,
                UserId = s.UserId,
                Company = s.Company,
                Model = s.Model,
                Lati = s.Lati,
                Long = s.Long,
                Topic = s.Topic,
            },
        };

        private static IHost CreateHostApplication(string[] args, CmdMain cmdMain, AppConfiguration appConfiguration, IReadOnlyList<WifiProfile> wifiProfiles)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = args,
                ContentRootPath = appConfiguration.ContentRootPath,
            });

            builder.Logging.Services.AddSerilog(configureLogger =>
            {
                configureLogger
                    .MinimumLevel.Information()
                    .Enrich.WithCaller()
                    .WriteTo.Console(
                        Serilog.Events.LogEventLevel.Information,
                        CallerEnricherOutputTemplate.Default
                    )
                    .WriteTo.File(
                        new DirectoryInfo(cmdMain.LogDirPath).FullName,
                        "Biz.Bizadm.PowerManager.DeviceConfig.App.log",
                        Serilog.Events.LogEventLevel.Information,
                        CallerEnricherOutputTemplate.Default,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 12
                    );
            });

            builder.Services.AddSingleton(CreateOptions(appConfiguration, wifiProfiles));
            builder.Services.AddSingleton<IDeviceConfigSelector, ConsoleDeviceConfigSelector>();
            builder.Services.AddSingleton(sp => HostWifiLoader.CreateForCurrentOs(sp.GetRequiredService<ILogger<DeviceConfigRunner>>()));
            builder.Services.AddSingleton<DeviceConfigRunner>();

            return builder.Build();
        }
    }
}
