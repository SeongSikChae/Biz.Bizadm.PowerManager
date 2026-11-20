using System.Text;
using System.Threading.Channels;
using Biz.Bizadm.PowerManager.FakeDevice.App.Recording;
using Biz.Bizadm.PowerManager.FakeDevice.App.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests;

/// <summary>실행기에 콘솔 줄을 넣는 입력. 줄을 넣거나 입력 끝(EOF)을 알린다.</summary>
internal sealed class TestConsoleInput : TextReader
{
    private readonly Channel<string> lines = Channel.CreateUnbounded<string>();

    public void Send(string line) => lines.Writer.TryWrite(line);

    public void Complete() => lines.Writer.TryComplete();

    public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await lines.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public override Task<string?> ReadLineAsync() => ReadLineAsync(CancellationToken.None).AsTask();

    public override string? ReadLine() => ReadLineAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
}

/// <summary>실행기 콘솔 출력 수집기. 여러 작업이 동시에 써도 읽기와 겹치지 않게 잠근다.</summary>
internal sealed class TestConsoleOutput : TextWriter
{
    private readonly StringBuilder text = new();

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (text)
            text.Append(value);
    }

    public override void Write(string? value)
    {
        lock (text)
            text.Append(value);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        lock (text)
            text.Append(buffer, index, count);
    }

    public override string ToString()
    {
        lock (text)
            return text.ToString();
    }
}

/// <summary>실행기 테스트 공용 도우미: 빈 포트, 임시 폴더, 시험 옵션, 조건 기다리기, 기록 읽기.</summary>
internal static class TestHarness
{
    public const string Mac = "a1b2c3d4e5f6";
    public const string DeviceKey = "DAWONDNS-B540_W-" + Mac;
    public const string ClientId = "DAWONDNS-" + Mac;

    /// <summary>기록·콘솔에 나오면 안 되는 시험 키(치환표 문자만).</summary>
    public const string TestKey = "TestKey9w5ZEu2cgypBmei";

    public const string TestPassword = "TestPassword7kJtVq";

    public static ushort FreeTcpPort()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        ushort port = (ushort)((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static string NewTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.FakeDevice.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>가짜 서버를 가리키는 시험 옵션. 서버가 없으면 닫힌 포트를 쓴다. 시간 제한과 크기 상한은 줄였다.</summary>
    public static FakeDeviceOptions Options(FakeDawonHttp? http, FakeDawonBroker? broker, string recordDirectory) => new()
    {
        ApiServerHost = "127.0.0.1",
        ApiServerPort = http?.HttpsPort ?? FreeTcpPort(),
        MqttServerHost = "127.0.0.1",
        MqttServerPort = broker?.Port ?? FreeTcpPort(),
        SkipCertificateValidation = true,
        Mac = Mac,
        Model = "B540_W",
        Company = "DAWONDNS",
        UserId = "tester",
        Lati = "0",
        Long = "0",
        FirmwareVersion = "1.01.36",
        MqttKey = TestKey,
        VerifyServerAddr = "dwmqtt.dawonai.com",
        ClientId = ClientId,
        UserName = DeviceKey,
        NotifyElements = [new NotifyElement("/100/0/31", "false")],
        RecordDirectory = recordDirectory,
        RestTimeout = TimeSpan.FromSeconds(2),
        MqttTimeout = TimeSpan.FromSeconds(2),
        FotaTimeout = TimeSpan.FromSeconds(3),
        FotaMaxBytes = 64 * 1024,
    };

    public static async Task WaitUntilAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out waiting: {because}");
            await Task.Delay(25);
        }
    }

    /// <summary>기록 폴더의 모든 JSONL 줄(끝난 줄만)을 읽는다. 쓰는 중인 파일도 읽을 수 있게 공유 모드로 연다.</summary>
    public static List<JsonElement> ReadRecords(string directory)
    {
        List<JsonElement> records = [];
        if (!Directory.Exists(directory))
            return records;
        foreach (string path in Directory.GetFiles(directory, "dawon-fake-device-*.jsonl").Order())
        {
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            string all = reader.ReadToEnd();
            int end = all.LastIndexOf('\n');
            if (end < 0)
                continue;
            foreach (string line in all[..end].Split('\n'))
                records.Add(JsonDocument.Parse(line).RootElement.Clone());
        }
        return records;
    }

    public static string RawRecords(string directory)
        => !Directory.Exists(directory)
            ? ""
            : string.Concat(Directory.GetFiles(directory, "*.jsonl").Select(path =>
            {
                using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }));

    public static IEnumerable<JsonElement> Of(this IEnumerable<JsonElement> records, string channel, string kind, string? direction = null)
        => records.Where(r => r.GetProperty("channel").GetString() == channel
                              && r.GetProperty("kind").GetString() == kind
                              && (direction is null || r.GetProperty("direction").GetString() == direction));
}

/// <summary>실행기를 백그라운드로 돌리는 시험 세션. 정리 때 정지 토큰을 취소하고 끝나기를 기다린다.</summary>
internal sealed class RunnerSession : IAsyncDisposable
{
    public required FakeDeviceOptions Options { get; init; }
    public TestConsoleInput Input { get; } = new();
    public TestConsoleOutput Output { get; } = new();
    public CancellationTokenSource Stop { get; } = new();
    public Task<bool> Run { get; private set; } = null!;

    public static RunnerSession Start(FakeDeviceOptions options)
    {
        RunnerSession session = new RunnerSession { Options = options };
        FakeDeviceRecorder recorder = new FakeDeviceRecorder(options.RecordDirectory, TimeProvider.System, NullLogger<FakeDeviceRecorder>.Instance);
        FakeDeviceRunner runner = new FakeDeviceRunner(options, recorder, session.Input, session.Output, TimeProvider.System, NullLogger<FakeDeviceRunner>.Instance);
        session.Run = Task.Run(() => runner.RunAsync(session.Stop.Token));
        return session;
    }

    public List<JsonElement> Records() => TestHarness.ReadRecords(Options.RecordDirectory);

    public string Console => Output.ToString();

    /// <summary>실행이 끝나기를 기다려 결과를 돌려준다.</summary>
    public async Task<bool> ResultAsync(TimeSpan? timeout = null) => await Run.WaitAsync(timeout ?? TimeSpan.FromSeconds(15));

    public async ValueTask DisposeAsync()
    {
        Stop.Cancel();
        try
        {
            await Run.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (Exception)
        {
        }
        Stop.Dispose();
    }
}
