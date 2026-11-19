using Biz.Bizadm.PowerManager.Server.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Net.Sockets;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

internal static class TestApp
{
    public static ushort FreeTcpPort()
    {
        TcpListener tcp = new TcpListener(IPAddress.IPv6Any, 0);
        tcp.Server.DualMode = true;
        tcp.Start();
        int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();
        return (ushort)port;
    }

    /// <summary>
    /// 포트 6개와 새 설정을 모두 명시한 설정(Validate를 거치지 않아 기본값이 채워지지 않는다).
    /// MqttPort는 빈 TCP 포트(hosted service가 TestServer에서도 실제 포트를 연다).
    /// </summary>
    public static AppConfiguration Configuration(string contentRoot) => new()
    {
        ContentRootPath = contentRoot,
        WebRootPath = "ClientApp",
        HttpPort = 80,
        HttpsPort = 443,
        DeviceHttpPort = 18446,
        DeviceHttpsPort = 18443,
        MqttPort = FreeTcpPort(),
        MqttsPort = FreeTcpPort(),
        DeviceMqttServerAddr = "dwmqtt.dawonai.com",
        DatabasePath = "powermanager.db",
        OsKms = new OsKmsConfiguration(),
    };

    /// <summary>
    /// 앱이 만든 SQLite DB 파일을 연결 풀이 잡고 있으면 삭제가 실패하므로 풀을 비운 뒤 지운다.
    /// </summary>
    public static void DeleteTempRoot(string tempRoot)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    public static async Task<WebApplication> StartAsync(AppConfiguration configuration, string logDir,
        Action<WebApplicationBuilder>? configureBuilder = null, Action<WebApplication>? configureApp = null)
    {
        WebApplication app = Program.CreateWebApplication(["--environment", "Production"], configuration, logDir, builder =>
        {
            builder.WebHost.UseTestServer();
            configureBuilder?.Invoke(builder);
        });
        configureApp?.Invoke(app);
        await app.StartAsync();
        return app;
    }
}
