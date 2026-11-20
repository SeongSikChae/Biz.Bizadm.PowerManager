using Biz.Bizadm.PowerManager.FakeDevice.App.Tests.Fakes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests;

[TestClass]
public sealed class RunnerFotaTests
{
    private const string Down = "dwd.v1/iot-server/" + TestHarness.DeviceKey;
    private const string Up = "dwd.v1/" + TestHarness.DeviceKey + "/iot-server/";
    private const string UserAgent = "Test-Agent/1.0 (FOTA)";

    private static readonly byte[] Image = RandomNumberGenerator.GetBytes(10_000);

    private FakeDawonHttp http = null!;
    private FakeDawonBroker broker = null!;
    private string recordDirectory = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        http = await FakeDawonHttp.StartAsync();
        broker = await FakeDawonBroker.StartAsync();
        recordDirectory = TestHarness.NewTempDirectory();
        http.Responder = r => r.Path switch
        {
            "/fw/b540_1" or "/fw/b540_2" or "/fw/b540" or "/fw/b540/_1" => new FakeDawonHttp.Reply(200, "application/octet-stream", Image),
            _ when r.Path.StartsWith("/api/", StringComparison.Ordinal) => new FakeDawonHttp.Reply(200, "application/json", "{}"u8.ToArray()),
            _ => new FakeDawonHttp.Reply(404, null, []),
        };
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        await broker.DisposeAsync();
        await http.DisposeAsync();
        TestHarness.DeleteDirectory(recordDirectory);
    }

    private string Url => http.HttpBase + "/fw/b540";

    private FakeDeviceOptions Options() => TestHarness.Options(http, broker, recordDirectory) with { UserAgent = UserAgent, SendRegister = false, SendTimeSync = false };

    private List<FakeDawonHttp.Received> FotaRequests() => http.Requests.Where(r => !r.Path.StartsWith("/api/", StringComparison.Ordinal)).ToList();

    private static string WriteMessage(string url)
        => $"{{\"sid\":\"1\",\"msg\":{{\"o\":\"w\",\"e\":[{{\"n\":\"/5/0/1\",\"sv\":\"{url}\"}}]}}}}";

    private async Task<RunnerSession> StartAsync(FakeDeviceOptions? options = null)
    {
        RunnerSession session = RunnerSession.Start(options ?? Options());
        await TestHarness.WaitUntilAsync(() => session.Records().Of("mqtt", "suback").Any(), "SUBACK record");
        return session;
    }

    private static async Task ResponsesAsync(RunnerSession session, int count)
        => await TestHarness.WaitUntilAsync(() => session.Records().Of("fota", "response").Count() + session.Records().Of("fota", "error").Count() >= count, $"{count} FOTA results");

    // ---------- C1 ----------

    [TestMethod]
    public async Task Auto_CandidateRecordedAndOneSuffixedRequestWithFixedHeaders()
    {
        await using RunnerSession session = await StartAsync();

        await broker.PublishAsync(Down + "/w/json", WriteMessage(Url));
        await ResponsesAsync(session, 1);
        await Task.Delay(TimeSpan.FromSeconds(1));

        JsonElement candidate = session.Records().Of("fota", "candidate").Single();
        Assert.AreEqual(Down + "/w/json", candidate.GetProperty("topic").GetString());
        Assert.AreEqual(Url, candidate.GetProperty("url").GetString());
        StringAssert.Contains(session.Console, "FOTA candidate: " + Url);

        List<FakeDawonHttp.Received> requests = FotaRequests();
        CollectionAssert.AreEqual(new[] { "/fw/b540_1" }, requests.Select(r => r.Path).ToArray());
        JsonElement requestRecord = session.Records().Of("fota", "request").Single();
        Assert.AreEqual("image", requestRecord.GetProperty("purpose").GetString());
        Assert.AreEqual(Url + "_1", requestRecord.GetProperty("url").GetString());
        foreach (FakeDawonHttp.Received request in requests)
        {
            Assert.AreEqual("GET", request.Method);
            Assert.AreEqual("keep-alive", request.Headers["Connection"]);
            Assert.AreEqual("no-cache", request.Headers["Cache-Control"]);
            Assert.AreEqual(UserAgent, request.Headers["User-Agent"]);
            Assert.AreEqual("*/*", request.Headers["Accept"]);
            Assert.AreEqual("gzip,deflate", request.Headers["Accept-Encoding"]);
            Assert.AreEqual("zh-CN,eb-US;q=0.8", request.Headers["Accept-Language"]);
        }
    }

    [TestMethod]
    public async Task Auto_NestedArrayUrl()
    {
        await using RunnerSession session = await StartAsync();

        await broker.PublishAsync(Down + "/w/json", $"{{\"msg\":{{\"e\":[{{\"v\":[1,{{\"deep\":\"{Url}/\"}}]}}]}}}}");
        await ResponsesAsync(session, 1);

        CollectionAssert.AreEqual(new[] { "/fw/b540/_1" }, FotaRequests().Select(r => r.Path).ToArray());
    }

    [TestMethod]
    public async Task Auto_SameUrlOnlyOnce()
    {
        await using RunnerSession session = await StartAsync();

        await broker.PublishAsync(Down + "/w/json", WriteMessage(Url));
        await ResponsesAsync(session, 1);
        await broker.PublishAsync(Down + "/w/json", WriteMessage(Url));
        await TestHarness.WaitUntilAsync(() => session.Records().Of("fota", "candidate").Count() == 2, "second candidate");
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.AreEqual(1, FotaRequests().Count);
    }

    // ---------- C2 ----------

    [TestMethod]
    public async Task Auto_RunningSlot1_RequestsSlot2Image()
    {
        await using RunnerSession session = await StartAsync(Options() with { RunningSlot = 1 });

        await broker.PublishAsync(Down + "/w/json", WriteMessage(Url));
        await ResponsesAsync(session, 1);

        CollectionAssert.AreEqual(new[] { "/fw/b540_2" }, FotaRequests().Select(r => r.Path).ToArray());
        Assert.AreEqual(Url + "_2", session.Records().Of("fota", "response").Single().GetProperty("url").GetString());
    }

    [TestMethod]
    [DataRow("not-json")]
    [DataRow("split")]
    [DataRow("auto-off")]
    public async Task Auto_NoRequest(string scenario)
    {
        FakeDeviceOptions options = Options() with { FotaAutoDownload = scenario != "auto-off" };
        await using RunnerSession session = await StartAsync(options);
        string payload = scenario switch
        {
            "not-json" => "url=" + Url,
            "split" => "{\"msg\":{\"e\":[{\"n\":\"/5/0/1\",\"sv\":\"127.0.0.1\"},{\"n\":\"/5/0/2\",\"sv\":\"" + http.HttpPort + "\"},{\"n\":\"/5/0/3\",\"sv\":\"/fw/b540\"}]}}",
            _ => WriteMessage(Url),
        };

        await broker.PublishAsync(Down + "/w/json", payload);
        await TestHarness.WaitUntilAsync(() => session.Records().Of("mqtt", "publish", "dawon-to-device").Any(), "received record");
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.AreEqual(0, FotaRequests().Count);
        Assert.AreEqual(scenario == "auto-off" ? 1 : 0, session.Records().Of("fota", "candidate").Count());
    }

    // ---------- C3·C4 ----------

    [TestMethod]
    public async Task Command_FotaOneSuffixedRequest_GetAsIs_Repeats()
    {
        await using RunnerSession session = await StartAsync();

        session.Input.Send("fota " + Url);
        await ResponsesAsync(session, 1);
        session.Input.Send("fota " + Url);
        await ResponsesAsync(session, 2);
        session.Input.Send("get " + Url);
        await ResponsesAsync(session, 3);
        session.Input.Send("get " + Url);
        await ResponsesAsync(session, 4);

        CollectionAssert.AreEqual(new[] { "/fw/b540_1", "/fw/b540_1", "/fw/b540", "/fw/b540" }, FotaRequests().Select(r => r.Path).ToArray());
        CollectionAssert.AreEqual(new[] { "image", "image", "get", "get" },
            session.Records().Of("fota", "request").Select(r => r.GetProperty("purpose").GetString()).ToArray());
    }

    // ---------- C5 ----------

    [TestMethod]
    public async Task Ok_SavesFile_RecordsHashSizeAndFields()
    {
        await using RunnerSession session = await StartAsync();

        session.Input.Send("fota " + Url);
        await ResponsesAsync(session, 1);

        JsonElement image = session.Records().Of("fota", "response").Single();
        Assert.AreEqual(Url + "_1", image.GetProperty("url").GetString());
        Assert.AreEqual(200, image.GetProperty("status").GetInt32());
        Assert.AreEqual("application/octet-stream", image.GetProperty("contentType").GetString());
        Assert.AreEqual(Image.Length, image.GetProperty("contentLength").GetInt64());
        Assert.AreEqual(Image.Length, image.GetProperty("bytes").GetInt64());
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(Image)), image.GetProperty("sha256").GetString());
        Assert.IsFalse(image.TryGetProperty("body", out _), "no body field");
        string file = image.GetProperty("file").GetString()!;
        StringAssert.Matches(file, new System.Text.RegularExpressions.Regex(@"^fota/\d{8}T\d{9}Z-\d{4}-image\.bin$"));
        CollectionAssert.AreEqual(Image, File.ReadAllBytes(Path.Combine(recordDirectory, file)));
        Assert.AreEqual(0, Directory.GetFiles(Path.Combine(recordDirectory, "fota"), "*.part").Length);
        StringAssert.Contains(session.Console, "sha256 " + image.GetProperty("sha256").GetString());
    }

    [TestMethod]
    public async Task Https_IsSupported()
    {
        await using RunnerSession session = await StartAsync();

        session.Input.Send("get " + http.HttpsBase + "/fw/b540");
        await ResponsesAsync(session, 1);

        Assert.AreEqual("https", FotaRequests().Single().Scheme);
        Assert.AreEqual(200, session.Records().Of("fota", "response").Single().GetProperty("status").GetInt32());
    }

    [TestMethod]
    public async Task Image404_RecordsStatusOnlyWithoutFile()
    {
        http.Responder = _ => new FakeDawonHttp.Reply(404, "text/plain", Encoding.UTF8.GetBytes("missing"));
        await using RunnerSession session = await StartAsync();

        session.Input.Send("fota " + Url);
        await ResponsesAsync(session, 1);

        JsonElement response = session.Records().Of("fota", "response").Single();
        Assert.AreEqual(Url + "_1", response.GetProperty("url").GetString());
        Assert.AreEqual(404, response.GetProperty("status").GetInt32());
        Assert.IsFalse(response.TryGetProperty("body", out _));
        Assert.IsFalse(response.TryGetProperty("file", out _));
        Assert.IsFalse(response.TryGetProperty("sha256", out _));
        string fota = Path.Combine(recordDirectory, "fota");
        Assert.AreEqual(0, Directory.Exists(fota) ? Directory.GetFiles(fota).Length : 0);
        StringAssert.Contains(session.Console, "status 404");
    }

    [TestMethod]
    public async Task Delay_Timeout()
    {
        http.Responder = _ => new FakeDawonHttp.Reply(200, "application/octet-stream", Image, Delay: TimeSpan.FromSeconds(5));
        await using RunnerSession session = await StartAsync(Options() with { FotaTimeout = TimeSpan.FromSeconds(1) });

        session.Input.Send("get " + Url);
        await ResponsesAsync(session, 1);

        Assert.AreEqual("timeout", session.Records().Of("fota", "error").Single().GetProperty("error").GetString());
        StringAssert.Contains(session.Console, "error: timeout");
    }

    [TestMethod]
    public async Task ClosedPort_RecordsErrorAndKeepsRunning()
    {
        await using RunnerSession session = await StartAsync();

        session.Input.Send($"get http://127.0.0.1:{TestHarness.FreeTcpPort()}/fw/b540");
        await ResponsesAsync(session, 1);
        session.Input.Send("up after/json still-running");
        await TestHarness.WaitUntilAsync(() => broker.Publishes.Any(p => p.Topic == Up + "after/json"), "publish after FOTA failure");

        string error = session.Records().Of("fota", "error").Single().GetProperty("error").GetString()!;
        Assert.AreNotEqual("timeout", error);
        Assert.IsFalse(session.Run.IsCompleted);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TooLarge_StopsWithoutFiles(bool chunked)
    {
        byte[] large = new byte[100_000];
        http.Responder = _ => new FakeDawonHttp.Reply(200, "application/octet-stream", large, Chunked: chunked);
        await using RunnerSession session = await StartAsync();

        session.Input.Send("get " + Url);
        await ResponsesAsync(session, 1);

        Assert.AreEqual("too-large", session.Records().Of("fota", "error").Single().GetProperty("error").GetString());
        string fota = Path.Combine(recordDirectory, "fota");
        Assert.AreEqual(0, Directory.Exists(fota) ? Directory.GetFiles(fota).Length : 0);
    }

    [TestMethod]
    public async Task SlowDownload_DoesNotBlockConsolePublish()
    {
        http.Responder = _ => new FakeDawonHttp.Reply(200, "application/octet-stream", Image, ChunkDelay: TimeSpan.FromMilliseconds(200));
        await using RunnerSession session = await StartAsync();

        session.Input.Send("get " + Url);
        await TestHarness.WaitUntilAsync(() => FotaRequests().Count == 1, "FOTA request started");
        session.Input.Send("up during/json while-downloading");
        await TestHarness.WaitUntilAsync(() => broker.Publishes.Any(p => p.Topic == Up + "during/json"), "publish during download");

        Assert.AreEqual(0, session.Records().Of("fota", "response").Count(), "download still running");
        await ResponsesAsync(session, 1);
        Assert.AreEqual(200, session.Records().Of("fota", "response").Single().GetProperty("status").GetInt32());
    }

    [TestMethod]
    public async Task Quit_CancelsDownload()
    {
        http.Responder = _ => new FakeDawonHttp.Reply(200, "application/octet-stream", Image, ChunkDelay: TimeSpan.FromMilliseconds(500));
        await using RunnerSession session = await StartAsync(Options() with { FotaTimeout = TimeSpan.FromSeconds(30) });

        session.Input.Send("get " + Url);
        await TestHarness.WaitUntilAsync(() => FotaRequests().Count == 1, "FOTA request started");
        session.Input.Send("quit");

        Assert.IsTrue(await session.ResultAsync());
        List<JsonElement> records = session.Records();
        Assert.AreEqual("cancelled", records.Of("fota", "error").Single().GetProperty("error").GetString());
        Assert.IsTrue(records.Of("mqtt", "disconnect").Single().GetProperty("clean").GetBoolean());
        string fota = Path.Combine(recordDirectory, "fota");
        Assert.AreEqual(0, Directory.Exists(fota) ? Directory.GetFiles(fota).Length : 0);
    }
}
