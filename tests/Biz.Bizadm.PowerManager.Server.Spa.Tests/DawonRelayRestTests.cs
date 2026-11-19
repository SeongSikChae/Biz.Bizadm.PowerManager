using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.Server.Spa.Data;
using Biz.Bizadm.PowerManager.Server.Spa.Relay;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

[TestClass]
public sealed class DawonRelayRestTests
{
    private const string RegisterPath = "/api/v1/devices/register/create";
    private const string Mac = "aabbccddeeff";
    private const string DeviceKey = "DAWONDNS-B540_W-aabbccddeeff";
    // 공백·키 순서를 일부러 흩뜨려 원문 그대로 전달되는지 본다.
    private const string RequestBody = "{ \"account\":{\"user_id\":\"user@example.com/kakao\"},\"device\":{\"mac\":\"AABBCCDDEEFF\",\"model_id\":\"B540_W\",\"company\":\"DAWONDNS\",\"lati\":\"37.5\",\"long\":\"127.0\",\"verify\":\"true\"} }";
    private const string DawonKey = "TESTKEYAbcdefgh12";
    private const string DawonVerify = "DAWONVERIFYSECRETVALUE00000000000000000000aa";

    private string tempRoot = null!;
    private FakeDawonRest fake = null!;

    [TestInitialize]
    public async Task TestInitialize()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.RelayRest.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
        fake = await FakeDawonRest.StartAsync();
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        await fake.DisposeAsync();
        TestApp.DeleteTempRoot(tempRoot);
    }

    private AppConfiguration Configuration(bool enabled = true, ushort? apiPort = null, bool skipCertificateValidation = true)
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        configuration.DawonRelay = new DawonRelayConfiguration
        {
            Enabled = enabled,
            ApiServerHost = "localhost",
            ApiServerPort = apiPort ?? fake.Port,
            MqttServerHost = "localhost",
            MqttServerPort = TestApp.FreeTcpPort(),
            RecordDirectory = "dawon-relay",
            SkipCertificateValidation = skipCertificateValidation,
        };
        return configuration;
    }

    private Task<WebApplication> StartAsync(AppConfiguration configuration)
        => TestApp.StartAsync(configuration, Path.Combine(tempRoot, "log"));

    private static ByteArrayContent Body(string json)
    {
        ByteArrayContent content = new ByteArrayContent(Encoding.UTF8.GetBytes(json));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return content;
    }

    private static async Task<List<DeviceRegistration>> ReadDevicesAsync(WebApplication app)
    {
        IDbContextFactory<PowerManagerDbContext> factory = app.Services.GetRequiredService<IDbContextFactory<PowerManagerDbContext>>();
        await using PowerManagerDbContext db = await factory.CreateDbContextAsync();
        return await db.Devices.AsNoTracking().ToListAsync();
    }

    private List<JsonElement> ReadRecords()
    {
        string directory = Path.Combine(tempRoot, "dawon-relay");
        if (!Directory.Exists(directory))
            return [];
        return Directory.GetFiles(directory, "dawon-relay-*.jsonl")
            .SelectMany(File.ReadAllLines)
            .Select(l => JsonDocument.Parse(l).RootElement.Clone())
            .ToList();
    }

    private string ReadAllText(string folder)
    {
        string directory = Path.Combine(tempRoot, folder);
        if (!Directory.Exists(directory))
            return string.Empty;
        StringBuilder builder = new StringBuilder();
        foreach (string file in Directory.GetFiles(directory))
        {
            using FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new StreamReader(stream);
            builder.Append(reader.ReadToEnd());
        }
        return builder.ToString();
    }

    private void AssertNoSecrets(params string[] secrets)
    {
        string text = ReadAllText("dawon-relay") + ReadAllText("log");
        foreach (string secret in secrets)
            Assert.DoesNotContain(secret, text);
    }

    // C2: 원문 요청·헤더 전달, 원문 응답에서 verify 값만 교체, 다원 Content-Type, DB 저장, 기록(C9·C11)
    [TestMethod]
    public async Task Register_Success_ForwardsRawAndReplacesOnlyVerify()
    {
        string dawonBody = "{ \"status\" : \"ok\",\n \"verify\":\"" + DawonVerify + "\" , \"mqtt_key\" : \"TESTKEY\\u0041bcdefgh12\", \"extra\":[1,{\"a\":\"\\u002B\"}] }";
        fake.Responder = _ => new FakeDawonRest.Reply(200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(dawonBody));
        AppConfiguration configuration = Configuration();

        await using (WebApplication app = await StartAsync(configuration))
        {
            HttpResponseMessage response = await app.GetTestClient().PostAsync(RegisterPath, Body(RequestBody));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
            string verify = DeviceVerify.Compute("dwmqtt.dawonai.com", Mac, DawonKey);
            string expected = dawonBody.Replace(DawonVerify, verify, StringComparison.Ordinal);
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(expected), await response.Content.ReadAsByteArrayAsync());

            FakeDawonRest.Received received = fake.Requests.Single();
            Assert.AreEqual("POST", received.Method);
            Assert.AreEqual(RegisterPath, received.Path);
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(RequestBody), received.Body);
            Assert.AreEqual("application/json", received.Headers["Content-Type"]);
            Assert.AreEqual("1.0", received.Headers["X-HIT-Version"]);

            DeviceRegistration device = (await ReadDevicesAsync(app)).Single();
            Assert.AreEqual(Mac, device.Mac);
            Assert.AreEqual(DawonKey, device.MqttKey);
            Assert.AreEqual("user@example.com/kakao", device.UserId);
            Assert.AreEqual("B540_W", device.ModelId);
            await app.StopAsync();
        }

        List<JsonElement> records = ReadRecords();
        Assert.HasCount(2, records);
        Assert.AreEqual("request", records[0].GetProperty("kind").GetString());
        Assert.AreEqual("device-to-dawon", records[0].GetProperty("direction").GetString());
        Assert.AreEqual(RequestBody, records[0].GetProperty("body").GetString());
        Assert.AreEqual("response", records[1].GetProperty("kind").GetString());
        Assert.AreEqual("dawon-to-device", records[1].GetProperty("direction").GetString());
        Assert.AreEqual(200, records[1].GetProperty("status").GetInt32());
        string recordedBody = records[1].GetProperty("body").GetString()!;
        Assert.Contains("\"verify\":\"***\"", recordedBody);
        Assert.Contains("\"mqtt_key\" : \"***\"", recordedBody);
        AssertNoSecrets(DawonKey, "TESTKEY\\u0041", DawonVerify, DeviceVerify.Compute("dwmqtt.dawonai.com", Mac, DawonKey));
    }

    // C2: 이미 저장된 mac이면 키를 다원 키로 바꾸고, 비어 있지 않은 값만 갱신한다.
    [TestMethod]
    public async Task Register_ExistingDevice_ReplacesKey()
    {
        fake.Responder = _ => new FakeDawonRest.Reply(200, "application/json", Encoding.UTF8.GetBytes($$"""{"verify":"x","mqtt_key":"{{DawonKey}}"}"""));
        await using WebApplication app = await StartAsync(Configuration());
        IDbContextFactory<PowerManagerDbContext> factory = app.Services.GetRequiredService<IDbContextFactory<PowerManagerDbContext>>();
        await using (PowerManagerDbContext db = await factory.CreateDbContextAsync())
        {
            db.Devices.Add(new DeviceRegistration
            {
                Mac = Mac, UserId = "old-user", ModelId = "OLD", Company = "OLDCO", MqttKey = "LocalKey12345678",
                RegisteredAt = DateTimeOffset.UnixEpoch, LastRequestedAt = DateTimeOffset.UnixEpoch,
            });
            await db.SaveChangesAsync();
        }

        HttpResponseMessage response = await app.GetTestClient().PostAsync(RegisterPath,
            Body("""{"account":{"user_id":""},"device":{"mac":"aabbccddeeff","model_id":"B540_W"}}"""));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        DeviceRegistration device = (await ReadDevicesAsync(app)).Single();
        Assert.AreEqual(DawonKey, device.MqttKey);
        Assert.AreEqual("old-user", device.UserId);
        Assert.AreEqual("B540_W", device.ModelId);
        Assert.AreEqual("OLDCO", device.Company);
        Assert.AreEqual(DateTimeOffset.UnixEpoch, device.RegisteredAt);
        Assert.AreNotEqual(DateTimeOffset.UnixEpoch, device.LastRequestedAt);
        await app.StopAsync();
    }

    // C2: verify가 없거나 문자열이 아니면 본문을 그대로 둔다(성공 조건에는 영향 없음).
    [TestMethod]
    [DataRow("{\"mqtt_key\":\"" + DawonKey + "\",\"status\":\"x\"}")]
    [DataRow("{\"verify\":12,\"mqtt_key\":\"" + DawonKey + "\"}")]
    [DataRow("{\"verify\":null,\"mqtt_key\":\"" + DawonKey + "\"}")]
    public async Task Register_VerifyMissingOrNotString_BodyUnchanged(string dawonBody)
    {
        fake.Responder = _ => new FakeDawonRest.Reply(200, "application/json", Encoding.UTF8.GetBytes(dawonBody));
        await using WebApplication app = await StartAsync(Configuration());

        HttpResponseMessage response = await app.GetTestClient().PostAsync(RegisterPath, Body(RequestBody));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(dawonBody, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(DawonKey, (await ReadDevicesAsync(app)).Single().MqttKey);
        await app.StopAsync();
    }

    // C3: 성공 조건을 채우지 못하면 다원 상태·Content-Type·본문을 그대로, DB는 바꾸지 않는다.
    [TestMethod]
    [DataRow(500, "text/plain", "server error")]
    [DataRow(400, "application/json", "{\"status\":\"FAIL\",\"reason\":\"x\"}")]
    [DataRow(200, "application/json", "{\"verify\":\"x\"}")]
    [DataRow(200, "application/json", "{\"mqtt_key\":\"\"}")]
    [DataRow(200, "application/json", "{\"mqtt_key\":\"bad key!\"}")]
    [DataRow(200, "application/json", "{\"mqtt_key\":1234567890}")]
    [DataRow(200, "text/html", "<html>not json</html>")]
    [DataRow(200, "application/json", "[{\"mqtt_key\":\"TESTKEYAbcdefgh12\"}]")]
    public async Task Register_NotAccepted_PassesThroughWithoutStoring(int status, string contentType, string dawonBody)
    {
        fake.Responder = _ => new FakeDawonRest.Reply(status, contentType, Encoding.UTF8.GetBytes(dawonBody));
        await using WebApplication app = await StartAsync(Configuration());

        HttpResponseMessage response = await app.GetTestClient().PostAsync(RegisterPath, Body(RequestBody));

        Assert.AreEqual(status, (int)response.StatusCode);
        Assert.AreEqual(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual(dawonBody, await response.Content.ReadAsStringAsync());
        Assert.IsEmpty(await ReadDevicesAsync(app));
        await app.StopAsync();
    }

    // C3: 연결 실패는 502, error 기록, DB 불변
    [TestMethod]
    public async Task Register_ConnectionRefused_Returns502()
    {
        await using WebApplication app = await StartAsync(Configuration(apiPort: TestApp.FreeTcpPort()));

        HttpResponseMessage response = await app.GetTestClient().PostAsync(RegisterPath, Body(RequestBody));

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.IsEmpty(await ReadDevicesAsync(app));
        await app.StopAsync();
        List<JsonElement> records = ReadRecords();
        Assert.AreEqual("request", records[0].GetProperty("kind").GetString());
        Assert.AreEqual("error", records[1].GetProperty("kind").GetString());
        Assert.AreEqual("rest", records[1].GetProperty("channel").GetString());
    }

    // C3: 시간 초과(10초)는 502
    [TestMethod]
    public async Task Register_Timeout_Returns502()
    {
        fake.Responder = _ => new FakeDawonRest.Reply(200, "application/json", Encoding.UTF8.GetBytes($$"""{"mqtt_key":"{{DawonKey}}"}"""), TimeSpan.FromSeconds(20));
        await using WebApplication app = await StartAsync(Configuration());

        HttpResponseMessage response = await app.GetTestClient().PostAsync(RegisterPath, Body(RequestBody));

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.IsEmpty(await ReadDevicesAsync(app));
        await app.StopAsync();
        Assert.AreEqual("timeout", ReadRecords().Single(r => r.GetProperty("kind").GetString() == "error").GetProperty("error").GetString());
    }

    // C3: mac 오류는 다원으로 보내지 않고 400
    [TestMethod]
    public async Task Register_InvalidMac_Returns400WithoutDawonRequest()
    {
        await using WebApplication app = await StartAsync(Configuration());

        HttpResponseMessage response = await app.GetTestClient().PostAsync(RegisterPath, Body("""{"device":{"mac":"xyz"}}"""));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsEmpty(fake.Requests);
        await app.StopAsync();
    }

    // C4: connect는 다원 응답을 그대로 돌려준다(로컬 등록 여부와 무관).
    [TestMethod]
    public async Task Connect_PassesThroughDawonResponse()
    {
        fake.Responder = r => new FakeDawonRest.Reply(404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("no such device " + r.Path));
        await using WebApplication app = await StartAsync(Configuration());

        HttpResponseMessage response = await app.GetTestClient().GetAsync("/api/v1/device/connect/" + DeviceKey);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual("text/plain; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        Assert.AreEqual("no such device /api/v1/device/connect/" + DeviceKey, await response.Content.ReadAsStringAsync());
        Assert.AreEqual("GET", fake.Requests.Single().Method);
        await app.StopAsync();
        Assert.AreEqual(404, ReadRecords().Single(r => r.GetProperty("kind").GetString() == "response").GetProperty("status").GetInt32());
    }

    // C4·C11: connect 응답의 최상위 mqtt_key·verify도 기록에서 가린다.
    [TestMethod]
    public async Task Connect_RecordMasksSecrets()
    {
        fake.Responder = _ => new FakeDawonRest.Reply(200, "application/json", Encoding.UTF8.GetBytes($$"""{"delay":"0","mqtt_key":"{{DawonKey}}"}"""));
        await using (WebApplication app = await StartAsync(Configuration()))
        {
            HttpResponseMessage response = await app.GetTestClient().GetAsync("/api/v1/device/connect/" + DeviceKey);
            Assert.AreEqual($$"""{"delay":"0","mqtt_key":"{{DawonKey}}"}""", await response.Content.ReadAsStringAsync());
            await app.StopAsync();
        }

        AssertNoSecrets(DawonKey);
    }

    [TestMethod]
    public async Task Connect_ConnectionRefused_Returns502()
    {
        await using WebApplication app = await StartAsync(Configuration(apiPort: TestApp.FreeTcpPort()));

        HttpResponseMessage response = await app.GetTestClient().GetAsync("/api/v1/device/connect/" + DeviceKey);

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
        await app.StopAsync();
    }

    // C1: 꺼짐이면 지금 처리(로컬 키 발급)이고 다원 요청·기록 파일이 없다.
    [TestMethod]
    public async Task Disabled_UsesLocalPathWithoutRecords()
    {
        await using (WebApplication app = await StartAsync(Configuration(enabled: false)))
        {
            HttpResponseMessage response = await app.GetTestClient().PostAsync(RegisterPath, Body(RequestBody));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(16, (await ReadDevicesAsync(app)).Single().MqttKey.Length);
            await app.StopAsync();
        }

        Assert.IsEmpty(fake.Requests);
        Assert.IsFalse(Directory.Exists(Path.Combine(tempRoot, "dawon-relay")));
    }

    // C1·C3·C4: 인증서 검증 기본값(켜짐)에서는 자체 서명 인증서라 연결 실패 경로(502)다.
    [TestMethod]
    public async Task CertificateValidationDefault_SelfSigned_Returns502()
    {
        fake.Responder = _ => new FakeDawonRest.Reply(200, "application/json", Encoding.UTF8.GetBytes($$"""{"mqtt_key":"{{DawonKey}}"}"""));
        await using WebApplication app = await StartAsync(Configuration(skipCertificateValidation: false));

        HttpResponseMessage register = await app.GetTestClient().PostAsync(RegisterPath, Body(RequestBody));
        HttpResponseMessage connect = await app.GetTestClient().GetAsync("/api/v1/device/connect/" + DeviceKey);

        Assert.AreEqual(HttpStatusCode.BadGateway, register.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadGateway, connect.StatusCode);
        Assert.IsEmpty(await ReadDevicesAsync(app));
        await app.StopAsync();
        Assert.AreEqual(2, ReadRecords().Count(r => r.GetProperty("kind").GetString() == "error"));
    }
}
