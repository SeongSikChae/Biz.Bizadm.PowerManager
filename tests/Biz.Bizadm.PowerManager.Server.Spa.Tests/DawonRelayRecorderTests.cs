using Biz.Bizadm.PowerManager.Server.Spa.Relay;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

[TestClass]
public sealed class DawonRelayRecorderTests
{
    private string tempRoot = null!;

    [TestInitialize]
    public void TestInitialize()
        => tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.Recorder.Tests." + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void TestCleanup()
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private DawonRelayRecorder Create(DateTimeOffset now)
        => new DawonRelayRecorder(tempRoot, new FixedTime(now), NullLogger<DawonRelayRecorder>.Instance);

    private List<JsonElement> ReadLines(string fileName)
        => File.ReadAllLines(Path.Combine(tempRoot, fileName)).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();

    [TestMethod]
    public async Task WriteAsync_UsesUtcDateFileAndCommonFields()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 11, 20, 23, 30, 0, TimeSpan.FromHours(-3)); // UTC 2026-11-21 02:30
        DawonRelayRecorder recorder = Create(now);

        await recorder.WriteAsync("mqtt", DawonRelayRecorder.DeviceToDawon, "connect", w => w.WriteString("clientId", "c1"));

        Assert.AreEqual("dawon-relay-20261121.jsonl", DawonRelayRecorder.FileName(now));
        JsonElement line = ReadLines("dawon-relay-20261121.jsonl").Single();
        Assert.AreEqual(now.ToString("O"), line.GetProperty("ts").GetString());
        Assert.AreEqual("mqtt", line.GetProperty("channel").GetString());
        Assert.AreEqual("device-to-dawon", line.GetProperty("direction").GetString());
        Assert.AreEqual("connect", line.GetProperty("kind").GetString());
        Assert.AreEqual("c1", line.GetProperty("clientId").GetString());
    }

    [TestMethod]
    public async Task RestEntries_WriteFieldsAndMaskResponse()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        DawonRelayRecorder recorder = Create(now);
        byte[] response = Encoding.UTF8.GetBytes("""{"verify":"SECRETVERIFY","mqtt_key":"SECRETKEY","x":1}""");

        await recorder.RestRequestAsync("POST", "/api/v1/devices/register/create", Encoding.UTF8.GetBytes("""{"a":"한글"}"""));
        await recorder.RestResponseAsync("POST", "/api/v1/devices/register/create", 200, response);
        await recorder.RestErrorAsync("GET", "/api/v1/device/connect/k", "dwapi:18443", "timeout");

        List<JsonElement> lines = ReadLines(DawonRelayRecorder.FileName(now));
        Assert.HasCount(3, lines);
        Assert.AreEqual("request", lines[0].GetProperty("kind").GetString());
        Assert.AreEqual("""{"a":"한글"}""", lines[0].GetProperty("body").GetString());
        Assert.AreEqual("utf8", lines[0].GetProperty("bodyEncoding").GetString());
        Assert.AreEqual("dawon-to-device", lines[1].GetProperty("direction").GetString());
        Assert.AreEqual(200, lines[1].GetProperty("status").GetInt32());
        Assert.AreEqual("""{"verify":"***","mqtt_key":"***","x":1}""", lines[1].GetProperty("body").GetString());
        Assert.AreEqual("error", lines[2].GetProperty("kind").GetString());
        Assert.AreEqual("timeout", lines[2].GetProperty("error").GetString());
        string raw = File.ReadAllText(Path.Combine(tempRoot, DawonRelayRecorder.FileName(now)));
        Assert.DoesNotContain("SECRETKEY", raw);
        Assert.DoesNotContain("SECRETVERIFY", raw);
    }

    [TestMethod]
    public async Task WriteBytes_InvalidUtf8_UsesBase64()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        DawonRelayRecorder recorder = Create(now);
        byte[] payload = [0xff, 0x00, 0x41];

        await recorder.WriteAsync("mqtt", DawonRelayRecorder.DawonToDevice, "publish", w => DawonRelayRecorder.WriteBytes(w, "payload", "payloadEncoding", payload));

        JsonElement line = ReadLines(DawonRelayRecorder.FileName(now)).Single();
        Assert.AreEqual("base64", line.GetProperty("payloadEncoding").GetString());
        CollectionAssert.AreEqual(payload, Convert.FromBase64String(line.GetProperty("payload").GetString()!));
    }

    [TestMethod]
    public async Task WriteAsync_ParallelWrites_AllLinesValid()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        DawonRelayRecorder recorder = Create(now);
        string big = new string('x', 5000);

        await Task.WhenAll(Enumerable.Range(0, 1000).Select(i => Task.Run(() =>
            recorder.WriteAsync("mqtt", DawonRelayRecorder.DeviceToDawon, "publish", w =>
            {
                w.WriteNumber("i", i);
                w.WriteString("payload", big);
            }))));

        List<JsonElement> lines = ReadLines(DawonRelayRecorder.FileName(now));
        Assert.HasCount(1000, lines);
        CollectionAssert.AreEquivalent(Enumerable.Range(0, 1000).ToList(), lines.Select(l => l.GetProperty("i").GetInt32()).ToList());
        Assert.IsTrue(lines.All(l => l.GetProperty("payload").GetString()!.Length == 5000));
    }

    [TestMethod]
    public async Task WriteAsync_WriteFailure_DoesNotThrow()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(tempRoot)!);
        File.WriteAllText(tempRoot, "not a directory");
        try
        {
            DawonRelayRecorder recorder = Create(DateTimeOffset.UtcNow);

            await recorder.WriteAsync("rest", DawonRelayRecorder.DeviceToDawon, "request", _ => { });
        }
        finally
        {
            File.Delete(tempRoot);
        }
    }
}
