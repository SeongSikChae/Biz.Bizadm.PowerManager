using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests.Fakes;

/// <summary>
/// 가짜 다원 REST·FOTA HTTP 서버(Kestrel). HTTPS(자체 서명 인증서)와 HTTP 포트를 무작위로 열고,
/// 받은 요청을 차례로 기록하며 응답 함수로 응답한다.
/// </summary>
internal sealed class FakeDawonHttp : IAsyncDisposable
{
    public sealed record Received(string Scheme, string Method, string Path, IReadOnlyDictionary<string, string> Headers, byte[] Body);

    /// <param name="Status">상태 코드.</param>
    /// <param name="ContentType">없으면 보내지 않는다.</param>
    /// <param name="Body">본문.</param>
    /// <param name="Delay">응답 헤더 전 지연.</param>
    /// <param name="Chunked">Content-Length 없이 청크로 보낸다.</param>
    /// <param name="ChunkDelay">0보다 크면 본문을 조각마다 이만큼 쉬며 보낸다(느린 본문).</param>
    public sealed record Reply(int Status, string? ContentType, byte[] Body, TimeSpan Delay = default, bool Chunked = false, TimeSpan ChunkDelay = default);

    private readonly WebApplication app;
    private readonly X509Certificate2 certificate;

    public ConcurrentQueue<Received> Requests { get; } = new();

    /// <summary>받은 요청에 돌려줄 응답. 테스트가 바꾼다.</summary>
    public Func<Received, Reply> Responder { get; set; } = _ => new Reply(200, "application/json", "{}"u8.ToArray());

    public ushort HttpsPort { get; }
    public ushort HttpPort { get; }

    public string HttpsBase => $"https://127.0.0.1:{HttpsPort}";
    public string HttpBase => $"http://127.0.0.1:{HttpPort}";

    private FakeDawonHttp(WebApplication app, X509Certificate2 certificate, ushort httpsPort, ushort httpPort)
    {
        this.app = app;
        this.certificate = certificate;
        HttpsPort = httpsPort;
        HttpPort = httpPort;
    }

    public static async Task<FakeDawonHttp> StartAsync()
    {
        X509Certificate2 certificate = TestCertificate.CreateLocalhost();
        ushort httpsPort = TestHarness.FreeTcpPort();
        ushort httpPort = TestHarness.FreeTcpPort();
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, httpsPort, listen => listen.UseHttps(certificate));
            options.Listen(IPAddress.Loopback, httpPort);
        });
        WebApplication app = builder.Build();
        FakeDawonHttp fake = new FakeDawonHttp(app, certificate, httpsPort, httpPort);
        app.Run(fake.HandleAsync);
        await app.StartAsync();
        return fake;
    }

    private async Task HandleAsync(HttpContext context)
    {
        using MemoryStream body = new MemoryStream();
        await context.Request.Body.CopyToAsync(body);
        Dictionary<string, string> headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        Received received = new Received(context.Request.Scheme, context.Request.Method, context.Request.Path + context.Request.QueryString, headers, body.ToArray());
        Requests.Enqueue(received);

        Reply reply = Responder(received);
        try
        {
            if (reply.Delay > TimeSpan.Zero)
                await Task.Delay(reply.Delay, context.RequestAborted);
            context.Response.StatusCode = reply.Status;
            if (reply.ContentType is not null)
                context.Response.ContentType = reply.ContentType;
            if (!reply.Chunked)
                context.Response.ContentLength = reply.Body.Length;
            if (reply.ChunkDelay <= TimeSpan.Zero)
            {
                await context.Response.Body.WriteAsync(reply.Body, context.RequestAborted);
                return;
            }
            const int chunk = 1024;
            for (int offset = 0; offset < reply.Body.Length; offset += chunk)
            {
                await context.Response.Body.WriteAsync(reply.Body.AsMemory(offset, Math.Min(chunk, reply.Body.Length - offset)), context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                await Task.Delay(reply.ChunkDelay, context.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
        certificate.Dispose();
    }
}
