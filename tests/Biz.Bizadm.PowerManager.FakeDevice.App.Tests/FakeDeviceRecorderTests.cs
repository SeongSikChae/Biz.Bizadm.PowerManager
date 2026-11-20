using Biz.Bizadm.PowerManager.FakeDevice.App.Recording;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests;

[TestClass]
public sealed class FakeDeviceRecorderTests
{
    private string tempRoot = null!;

    [TestInitialize]
    public void TestInitialize() => tempRoot = TestHarness.NewTempDirectory();

    [TestCleanup]
    public void TestCleanup() => TestHarness.DeleteDirectory(tempRoot);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private FakeDeviceRecorder Create(DateTimeOffset now) => new FakeDeviceRecorder(tempRoot, new FixedTime(now), NullLogger<FakeDeviceRecorder>.Instance);

    private string[] Lines(string fileName) => File.ReadAllLines(Path.Combine(tempRoot, fileName));

    [TestMethod]
    public async Task FileName_IsUtcDate()
    {
        // 한국 시각 2026-11-21 08:00은 UTC 2026-11-20 23:00이다
        FakeDeviceRecorder recorder = Create(new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.FromHours(9)));

        await recorder.RestVerifyAsync("match");

        Assert.IsTrue(File.Exists(Path.Combine(tempRoot, "dawon-fake-device-20261120.jsonl")));
    }

    [TestMethod]
    public async Task CommonFields()
    {
        DateTimeOffset now = new DateTimeOffset(2026, 11, 20, 1, 2, 3, TimeSpan.Zero);
        FakeDeviceRecorder recorder = Create(now);

        await recorder.MqttConnAckAsync("client", 4);

        JsonElement record = JsonDocument.Parse(Lines("dawon-fake-device-20261120.jsonl").Single()).RootElement;
        Assert.AreEqual(now.ToString("O"), record.GetProperty("ts").GetString());
        Assert.AreEqual("mqtt", record.GetProperty("channel").GetString());
        Assert.AreEqual("dawon-to-device", record.GetProperty("direction").GetString());
        Assert.AreEqual("connack", record.GetProperty("kind").GetString());
        Assert.AreEqual("client", record.GetProperty("clientId").GetString());
        Assert.AreEqual(4, record.GetProperty("returnCode").GetInt32());
    }

    [TestMethod]
    public async Task Bytes_Utf8OrBase64()
    {
        FakeDeviceRecorder recorder = Create(DateTimeOffset.UtcNow);

        await recorder.MqttPublishAsync(FakeDeviceRecorder.DawonToDevice, "c", "t", 0, false, Encoding.UTF8.GetBytes("한글 {\"a\":1}"));
        await recorder.MqttPublishAsync(FakeDeviceRecorder.DawonToDevice, "c", "t", 0, false, new byte[] { 0xff, 0x00, 0x80 });

        JsonElement[] records = TestHarness.ReadRecords(tempRoot).ToArray();
        Assert.AreEqual("한글 {\"a\":1}", records[0].GetProperty("payload").GetString());
        Assert.AreEqual("utf8", records[0].GetProperty("payloadEncoding").GetString());
        Assert.AreEqual(Convert.ToBase64String(new byte[] { 0xff, 0x00, 0x80 }), records[1].GetProperty("payload").GetString());
        Assert.AreEqual("base64", records[1].GetProperty("payloadEncoding").GetString());
    }

    [TestMethod]
    public async Task LargeBody_IsNotTruncated()
    {
        FakeDeviceRecorder recorder = Create(DateTimeOffset.UtcNow);
        string body = new string('x', 1024 * 1024);

        await recorder.RestRequestAsync("POST", "/p", Encoding.UTF8.GetBytes(body));

        Assert.AreEqual(body, TestHarness.ReadRecords(tempRoot).Single().GetProperty("body").GetString());
    }

    [TestMethod]
    public async Task ConcurrentWrites_AreWholeLines()
    {
        FakeDeviceRecorder recorder = Create(DateTimeOffset.UtcNow);
        string payload = new string('y', 4096);

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() =>
            recorder.MqttPublishAsync(FakeDeviceRecorder.DeviceToDawon, "c" + i, "t", 0, false, Encoding.UTF8.GetBytes(payload)))));

        List<JsonElement> records = TestHarness.ReadRecords(tempRoot);
        Assert.AreEqual(200, records.Count);
        CollectionAssert.AreEquivalent(Enumerable.Range(0, 200).Select(i => "c" + i).ToArray(), records.Select(r => r.GetProperty("clientId").GetString()).ToArray());
    }

    [TestMethod]
    public async Task RestResponse_MasksTopLevelKeyAndVerify()
    {
        FakeDeviceRecorder recorder = Create(DateTimeOffset.UtcNow);

        await recorder.RestResponseAsync("POST", "/p", 200, Encoding.UTF8.GetBytes("{\"mqtt_key\":\"SecretKey\",\"verify\":\"SecretVerify\",\"inner\":{\"mqtt_key\":\"keep\"}}"));

        JsonElement record = TestHarness.ReadRecords(tempRoot).Single();
        Assert.AreEqual("{\"mqtt_key\":\"***\",\"verify\":\"***\",\"inner\":{\"mqtt_key\":\"keep\"}}", record.GetProperty("body").GetString());
        Assert.AreEqual(200, record.GetProperty("status").GetInt32());
    }

    [TestMethod]
    public async Task Connect_RecordsOnlyPasswordPresence()
    {
        FakeDeviceRecorder recorder = Create(DateTimeOffset.UtcNow);

        await recorder.MqttConnectAsync("c", "u", true, 60, true, "/lwt", Encoding.UTF8.GetBytes("offline"), 0, false);

        JsonElement record = TestHarness.ReadRecords(tempRoot).Single();
        Assert.IsTrue(record.GetProperty("hasPassword").GetBoolean());
        Assert.IsFalse(record.TryGetProperty("password", out _));
        JsonElement will = record.GetProperty("will");
        Assert.AreEqual("/lwt", will.GetProperty("topic").GetString());
        Assert.AreEqual("offline", will.GetProperty("payload").GetString());
        Assert.AreEqual(0, will.GetProperty("qos").GetInt32());
        Assert.IsFalse(will.GetProperty("retain").GetBoolean());
    }
}
