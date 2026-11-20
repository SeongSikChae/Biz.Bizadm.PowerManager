using Biz.Bizadm.PowerManager.FakeDevice.App.Recording;
using Biz.Bizadm.PowerManager.FakeDevice.App.Security;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Configuration;
using System.Configuration;

namespace Biz.Bizadm.PowerManager.FakeDevice.App
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

        static Task<int> Main(string[] args) => RunAsync(args);

        /// <summary>명령줄을 해석해 실행하고 종료 코드(0/1/2/3)를 돌려준다. 테스트에서도 부른다.</summary>
        internal static async Task<int> RunAsync(string[] args)
        {
            ParserResult<CmdMain> result = Parser.Default.ParseArguments<CmdMain>(args);
            return (int)await result.MapResult(
                cmdMain => RunAsync(args, cmdMain),
                errors => Task.FromResult(errors.IsHelp() || errors.IsVersion()
                    ? ExitCode.Success
                    : ExitCode.InvalidArguments));
        }

        /// <summary>상대 경로의 기준. ContentRootPath가 비어 있으면 실행 폴더다. 설정 모델의 값은 바꾸지 않는다(D025).</summary>
        internal static string ResolveBasePath(AppConfiguration appConfiguration)
            => string.IsNullOrEmpty(appConfiguration.ContentRootPath) ? AppContext.BaseDirectory : appConfiguration.ContentRootPath;

        private static async Task<ExitCode> RunAsync(string[] args, CmdMain cmdMain)
        {
            AppConfiguration? appConfiguration;
            string basePath;
            try
            {
                YamlDotNet.Serialization.Deserializer deserializer = new();
                appConfiguration = deserializer.Deserialize<AppConfiguration>(File.ReadAllText(cmdMain.ConfigFilePath))
                    ?? throw new ArgumentException("Configuration file is empty");
                basePath = ResolveBasePath(appConfiguration);
                ConfigurationValidator.Validate(appConfiguration);
                appConfiguration.Validate();
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"Configuration error: {ex.Message}");
                return ExitCode.ConfigurationError;
            }

            ConfigSecrets secrets;
            try
            {
                secrets = ProtectSecrets(cmdMain.ConfigFilePath, appConfiguration, basePath);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"Configuration error: {ex.Message}");
                return ExitCode.ConfigurationError;
            }

            IHost host;
            try
            {
                host = CreateHostApplication(args, cmdMain, appConfiguration, secrets, basePath);
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"Host creation error: {ex}");
                return ExitCode.RuntimeError;
            }

            using (host)
            {
                ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
                try
                {
                    await host.StartAsync();
                    // 실행 결과(등록 실패·접속 거부 등)를 종료 코드 3으로 내기 위해 BackgroundService 대신 실행기를 직접 실행한다(plan 결정 절)
                    IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                    bool succeeded = await host.Services.GetRequiredService<FakeDeviceRunner>().RunAsync(lifetime.ApplicationStopping);
                    await host.StopAsync();
                    return succeeded ? ExitCode.Success : ExitCode.RuntimeError;
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, "Host terminated unexpectedly");
                    return ExitCode.RuntimeError;
                }
            }
        }

        private static ConfigSecrets ProtectSecrets(string configFilePath, AppConfiguration appConfiguration, string basePath)
        {
            if (!ConfigSecretProtector.HasSecrets(appConfiguration))
                return ConfigSecrets.Empty;
            using ConfigSecretProtector protector = ConfigSecretProtector.Create(appConfiguration, basePath);
            ConfigSecrets secrets = protector.Protect(appConfiguration);
            if (protector.Rewrite)
            {
                YamlDotNet.Serialization.Serializer serializer = new();
                File.WriteAllText(configFilePath, serializer.Serialize(appConfiguration));
                System.Console.WriteLine("Configuration file updated with encrypted secrets");
            }
            return secrets;
        }

        private static IHost CreateHostApplication(string[] args, CmdMain cmdMain, AppConfiguration appConfiguration, ConfigSecrets secrets, string basePath)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = args,
                ContentRootPath = basePath,
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
                        "Biz.Bizadm.PowerManager.FakeDevice.App.log",
                        Serilog.Events.LogEventLevel.Information,
                        CallerEnricherOutputTemplate.Default,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 12
                    );
            });

            FakeDeviceOptions options = FakeDeviceOptions.Create(appConfiguration, secrets, basePath);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton(sp => new FakeDeviceRecorder(
                options.RecordDirectory, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<FakeDeviceRecorder>>()));
            builder.Services.AddSingleton(sp => new FakeDeviceRunner(
                options,
                sp.GetRequiredService<FakeDeviceRecorder>(),
                System.Console.In,
                System.Console.Out,
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<FakeDeviceRunner>>()));

            return builder.Build();
        }
    }
}
