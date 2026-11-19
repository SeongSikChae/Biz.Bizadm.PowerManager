using Biz.Bizadm.PowerManager.Core.Devices;
using Biz.Bizadm.PowerManager.Server.Spa.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

[TestClass]
public sealed class DeviceEndpointTests
{
    private const string RegisterPath = "/api/v1/devices/register/create";
    private const string Mac = "aabbccddeeff";
    private const string FullBody = """{"account":{"user_id":"user@example.com/kakao"},"device":{"mac":"aabbccddeeff","model_id":"B540_W","company":"DAWONDNS","lati":"37.5","long":"127.0","verify":"true"}}""";

    private static string tempRoot = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.Server.Spa.Tests." + Guid.NewGuid().ToString("N"));
        // fallback이 있는 운영 모드 상태로 띄운다(HEAD가 fallback으로 빠지지 않는지 확인).
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
        File.WriteAllText(Path.Combine(tempRoot, "ClientApp", "index.html"), "<html>index</html>");
    }

    [ClassCleanup]
    public static void ClassCleanup() => TestApp.DeleteTempRoot(tempRoot);

    /// <summary>
    /// 클래스가 임시 폴더를 공유하므로 테스트마다 DB 파일을 따로 써서 서로 격리한다.
    /// </summary>
    private static AppConfiguration IsolatedConfiguration()
    {
        AppConfiguration configuration = TestApp.Configuration(tempRoot);
        configuration.DatabasePath = $"devices-{Guid.NewGuid():N}.db";
        return configuration;
    }

    private static Task<WebApplication> StartAsync(AppConfiguration? configuration = null)
        => TestApp.StartAsync(configuration ?? IsolatedConfiguration(), Path.Combine(tempRoot, "log"));

    private static StringContent Json(string json) => new StringContent(json, Encoding.UTF8, "application/json");

    private static async Task<(string Verify, string MqttKey)> RegisterAsync(WebApplication app, string body)
    {
        HttpResponseMessage response = await app.GetTestClient().PostAsync(RegisterPath, Json(body));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (document.RootElement.GetProperty("verify").GetString()!, document.RootElement.GetProperty("mqtt_key").GetString()!);
    }

    private static async Task<List<DeviceRegistration>> ReadDevicesAsync(WebApplication app)
    {
        IDbContextFactory<PowerManagerDbContext> factory = app.Services.GetRequiredService<IDbContextFactory<PowerManagerDbContext>>();
        await using PowerManagerDbContext db = await factory.CreateDbContextAsync();
        return await db.Devices.AsNoTracking().ToListAsync();
    }

    private static void AssertProblem(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static void AssertNotImplemented(HttpResponseMessage response, bool hasBody = true)
    {
        Assert.AreEqual(HttpStatusCode.NotImplemented, response.StatusCode);
        if (hasBody)
            Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static string ReadLogs()
    {
        StringBuilder builder = new StringBuilder();
        foreach (string file in Directory.GetFiles(Path.Combine(tempRoot, "log"), "*.log"))
        {
            using FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader reader = new StreamReader(stream);
            builder.Append(reader.ReadToEnd());
        }
        return builder.ToString();
    }

    // C1·C2·C3
    [TestMethod]
    public async Task Register_ValidBody_ReturnsVerifyThenMqttKey()
    {
        await using WebApplication app = await StartAsync();
        HttpResponseMessage response = await app.GetTestClient().PostAsync(RegisterPath, Json(FullBody));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
        string raw = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(raw.StartsWith("{\"verify\":\"", StringComparison.Ordinal), raw);

        using JsonDocument document = JsonDocument.Parse(raw);
        CollectionAssert.AreEqual(new[] { "verify", "mqtt_key" }, document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        string verify = document.RootElement.GetProperty("verify").GetString()!;
        string mqttKey = document.RootElement.GetProperty("mqtt_key").GetString()!;
        Assert.AreEqual(44, verify.Length);
        Assert.IsTrue(Regex.IsMatch(mqttKey, "^[A-Za-z0-9]{16}$"), mqttKey);
        Assert.AreEqual(DeviceVerify.Compute("dwmqtt.dawonai.com", Mac, mqttKey), verify);
        await app.StopAsync();
    }

    // C1·C3: verify에 '+'가 있어도 원문에 글자 그대로(44자) 나간다. 키는 참조 시험 값의 입력으로 미리 저장한다.
    [TestMethod]
    public async Task Register_VerifyWithPlus_IsNotEscaped()
    {
        await using WebApplication app = await StartAsync();
        IDbContextFactory<PowerManagerDbContext> factory = app.Services.GetRequiredService<IDbContextFactory<PowerManagerDbContext>>();
        await using (PowerManagerDbContext db = await factory.CreateDbContextAsync())
        {
            db.Devices.Add(new DeviceRegistration
            {
                Mac = "0123456789ab",
                MqttKey = "AbCdEfGh12345678",
                RegisteredAt = DateTimeOffset.UtcNow,
                LastRequestedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        HttpResponseMessage response = await app.GetTestClient().PostAsync(RegisterPath, Json("""{"device":{"mac":"0123456789ab"}}"""));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("""{"verify":"7L.eB2kswATShmfbnM2JFsl6HrH+hkaw4JI3xz0NxU2J","mqtt_key":"AbCdEfGh12345678"}""",
            await response.Content.ReadAsStringAsync());
        await app.StopAsync();
    }

    // C3
    [TestMethod]
    public async Task Register_UsesConfiguredServerAddr()
    {
        AppConfiguration configuration = IsolatedConfiguration();
        configuration.DeviceMqttServerAddr = "192.168.0.13";
        await using WebApplication app = await StartAsync(configuration);

        (string verify, string mqttKey) = await RegisterAsync(app, FullBody);

        Assert.AreEqual(DeviceVerify.Compute("192.168.0.13", Mac, mqttKey), verify);
        Assert.AreNotEqual(DeviceVerify.Compute("dwmqtt.dawonai.com", Mac, mqttKey), verify);
        await app.StopAsync();
    }

    // C4
    [TestMethod]
    public async Task Register_InvalidMac_Returns400AndStoresNothing()
    {
        string[] bodies =
        [
            """{"account":{"user_id":"u"}}""",
            """{"device":{"model_id":"B540_W"}}""",
            """{"device":{"mac":""}}""",
            """{"device":{"mac":"aabbccddeef"}}""",
            """{"device":{"mac":"aabbccddeeff0"}}""",
            """{"device":{"mac":"zzbbccddeeff"}}""",
        ];
        await using WebApplication app = await StartAsync();
        foreach (string body in bodies)
            AssertProblem(await app.GetTestClient().PostAsync(RegisterPath, Json(body)), HttpStatusCode.BadRequest);

        Assert.AreEqual(0, (await ReadDevicesAsync(app)).Count);
        await app.StopAsync();
    }

    // C4·C11
    [TestMethod]
    public async Task Register_UppercaseMac_StoredLowercase()
    {
        await using WebApplication app = await StartAsync();
        await RegisterAsync(app, """{"device":{"mac":"AABBCCDDEEFF"}}""");

        Assert.AreEqual(Mac, (await ReadDevicesAsync(app)).Single().Mac);
        HttpResponseMessage connect = await app.GetTestClient().GetAsync("/api/v1/device/connect/DAWONDNS-B540_W-" + Mac);
        Assert.AreEqual(HttpStatusCode.OK, connect.StatusCode);
        await app.StopAsync();
    }

    // C4·C5
    [TestMethod]
    public async Task Register_WithoutOptionalFields_StoresEmptyValuesAndTimes()
    {
        await using WebApplication app = await StartAsync();
        DateTimeOffset before = DateTimeOffset.UtcNow;
        await RegisterAsync(app, """{"device":{"mac":"aabbccddeeff"}}""");
        DateTimeOffset after = DateTimeOffset.UtcNow;

        DeviceRegistration device = (await ReadDevicesAsync(app)).Single();
        Assert.AreEqual(string.Empty, device.UserId);
        Assert.AreEqual(string.Empty, device.ModelId);
        Assert.AreEqual(string.Empty, device.Company);
        Assert.IsTrue(device.RegisteredAt >= before && device.RegisteredAt <= after, $"{before:O} {device.RegisteredAt:O} {after:O}");
        Assert.AreEqual(device.RegisteredAt, device.LastRequestedAt);
        await app.StopAsync();
    }

    // C5
    [TestMethod]
    public async Task Register_AfterRestart_ReturnsSameKey()
    {
        AppConfiguration configuration = IsolatedConfiguration();
        string firstKey;
        await using (WebApplication app = await StartAsync(configuration))
        {
            firstKey = (await RegisterAsync(app, FullBody)).MqttKey;
            await app.StopAsync();
        }
        Assert.IsTrue(File.Exists(Path.Combine(tempRoot, configuration.DatabasePath)));

        await using (WebApplication app = await StartAsync(configuration))
        {
            Assert.AreEqual(firstKey, (await RegisterAsync(app, FullBody)).MqttKey);
            await app.StopAsync();
        }
    }

    // C6
    [TestMethod]
    public async Task Register_Again_ReusesKeyAndUpdatesOnlyNonEmptyValues()
    {
        await using WebApplication app = await StartAsync();
        string firstKey = (await RegisterAsync(app, FullBody)).MqttKey;
        DeviceRegistration first = (await ReadDevicesAsync(app)).Single();

        await Task.Delay(50);
        DateTimeOffset beforeSecond = DateTimeOffset.UtcNow;
        (string verify, string secondKey) = await RegisterAsync(app,
            """{"account":{"user_id":"other@example.com/kakao"},"device":{"mac":"aabbccddeeff","model_id":""}}""");

        Assert.AreEqual(firstKey, secondKey);
        Assert.AreEqual(DeviceVerify.Compute("dwmqtt.dawonai.com", Mac, secondKey), verify);
        DeviceRegistration second = (await ReadDevicesAsync(app)).Single();
        Assert.AreEqual(first.RegisteredAt, second.RegisteredAt);
        Assert.IsTrue(second.LastRequestedAt >= beforeSecond && second.LastRequestedAt > first.LastRequestedAt,
            $"{first.LastRequestedAt:O} {beforeSecond:O} {second.LastRequestedAt:O}");
        Assert.AreEqual("other@example.com/kakao", second.UserId);
        Assert.AreEqual("B540_W", second.ModelId);
        Assert.AreEqual("DAWONDNS", second.Company);
        await app.StopAsync();
    }

    // C7
    [TestMethod]
    public async Task RegisterAndConnect_LogsDoNotContainKeyOrVerify()
    {
        string verify;
        string mqttKey;
        await using (WebApplication app = await StartAsync())
        {
            (verify, mqttKey) = await RegisterAsync(app, FullBody);
            await RegisterAsync(app, FullBody);
            await app.GetTestClient().GetAsync("/api/v1/device/connect/DAWONDNS-B540_W-" + Mac);
            await app.StopAsync();
        }

        string log = ReadLogs();
        StringAssert.Contains(log, "Device registered: mac=\"aabbccddeeff\"");
        Assert.IsFalse(log.Contains(mqttKey, StringComparison.Ordinal), "로그에 mqtt_key가 있다");
        Assert.IsFalse(log.Contains(verify, StringComparison.Ordinal), "로그에 verify가 있다");
    }

    // C11
    [TestMethod]
    [DataRow("DAWONDNS-B540_W-aabbccddeeff")]
    [DataRow("DAWONDNS-M130-W-AABBCCDDEEFF")]
    public async Task Connect_RegisteredDevice_ReturnsDelay(string deviceKey)
    {
        await using WebApplication app = await StartAsync();
        await RegisterAsync(app, FullBody);

        HttpResponseMessage response = await app.GetTestClient().GetAsync("/api/v1/device/connect/" + deviceKey);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("""{"delay":"0"}""", await response.Content.ReadAsStringAsync());
        await app.StopAsync();
    }

    // C11
    [TestMethod]
    public async Task Connect_UnregisteredOrInvalidKey_Returns404()
    {
        string[] keys =
        [
            "DAWONDNS-B540_W-112233445566",
            "DAWONDNS--aabbccddeeff",
            "DAWONDNS-aabbccddeeff",
            "X-B540_W-aabbccddeeff",
        ];
        await using WebApplication app = await StartAsync();
        await RegisterAsync(app, FullBody);

        foreach (string key in keys)
            AssertProblem(await app.GetTestClient().GetAsync("/api/v1/device/connect/" + key), HttpStatusCode.NotFound);
        await app.StopAsync();
    }

    [TestMethod]
    [DataRow("/fota/b540/version")]
    [DataRow("/fota/b540/user2.bin")]
    [DataRow("/fota/")]
    public async Task Fota_Get_Returns501(string path)
    {
        await using WebApplication app = await StartAsync();
        HttpResponseMessage response = await app.GetTestClient().GetAsync(path);
        AssertNotImplemented(response);
        await app.StopAsync();
    }

    [TestMethod]
    [DataRow("/fota/b540/version")]
    [DataRow("/fota/b540/user2.bin")]
    public async Task Fota_Head_Returns501(string path)
    {
        await using WebApplication app = await StartAsync();
        HttpResponseMessage response = await app.GetTestClient().SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
        AssertNotImplemented(response, hasBody: false);
        await app.StopAsync();
    }
}
