using Biz.Bizadm.PowerManager.Server.Spa.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

/// <summary>
/// 장비명 저장 API와 보관(device-name-and-units C10~C13). 상태 스트림의 알림은 <see cref="DeviceStatusApiTests"/>에 있다.
/// </summary>
[TestClass]
public sealed class DeviceNameApiTests
{
    private const string Mac = "3a0000000801";
    private const string NamePath = "/api/admin/devices/" + Mac + "/name";
    private const char Tab = (char)0x09;
    private const char LineFeed = (char)0x0A;
    private const char NextLine = (char)0x85;
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;
    private const char IdeographicSpace = (char)0x3000;
    private const char ByteOrderMark = (char)0xFEFF;

    private static readonly string LongestName = new string('가', 30);
    private static readonly string Emoji = char.ConvertFromUtf32(0x1F600);

    private string tempRoot = null!;
    private AppConfiguration configuration = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), "Biz.Bizadm.PowerManager.DeviceName.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "ClientApp"));
        configuration = TestApp.Configuration(tempRoot);
    }

    [TestCleanup]
    public void TestCleanup() => TestApp.DeleteTempRoot(tempRoot);

    private Task<WebApplication> StartAsync() => TestApp.StartAsync(configuration, Path.Combine(tempRoot, "log"));

    private static string Body(string? name) => JsonSerializer.Serialize(new { name });

    private static Task<(int Status, string? ContentType, string Body)> PutAsync(WebApplication app, string body, string path = NamePath, int port = 80)
        => TestMqtt.SendHttpAsync(app, port, "PUT", path, body);

    private static async Task<DeviceRegistration> LoadAsync(WebApplication app)
    {
        await using PowerManagerDbContext db = await app.Services.GetRequiredService<IDbContextFactory<PowerManagerDbContext>>().CreateDbContextAsync();
        return await db.Devices.AsNoTracking().SingleAsync(d => d.Mac == Mac);
    }

    private static async Task RenameAsync(WebApplication app, string name)
    {
        (int status, _, string body) = await PutAsync(app, Body(name));
        Assert.AreEqual(204, status, body);
    }

    // C10: 이전 마이그레이션까지만 적용된 DB는 시작할 때 열이 더해지고, 기존 장비는 값을 잃지 않은 채 장비명이 빈 문자열이다.
    [TestMethod]
    public async Task Migration_ExistingDevice_KeepsValuesWithEmptyName()
    {
        string databasePath = Path.Combine(tempRoot, configuration.DatabasePath);
        DbContextOptions<PowerManagerDbContext> options =
            new DbContextOptionsBuilder<PowerManagerDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        long ticks = new DateTimeOffset(2026, 11, 19, 1, 2, 3, TimeSpan.Zero).UtcTicks;
        await using (PowerManagerDbContext db = new PowerManagerDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync("InitialDevices");
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO Devices (Mac, UserId, ModelId, Company, MqttKey, RegisteredAt, LastRequestedAt) VALUES ({Mac}, 'user-01', 'M130-W', 'DAWONDNS', 'AbCdEfGh12345678', {ticks}, {ticks})");
        }

        await using WebApplication app = await StartAsync();

        DeviceRegistration device = await LoadAsync(app);
        Assert.AreEqual(string.Empty, device.Name);
        Assert.AreEqual("user-01", device.UserId);
        Assert.AreEqual("M130-W", device.ModelId);
        Assert.AreEqual("DAWONDNS", device.Company);
        Assert.AreEqual("AbCdEfGh12345678", device.MqttKey);
        Assert.AreEqual(ticks, device.RegisteredAt.UtcTicks);
        Assert.AreEqual(ticks, device.LastRequestedAt.UtcTicks);
        await app.StopAsync();
    }

    // C10: DB 기본값이 빈 문자열이라, 장비명 열을 모르는 이전 실행 파일의 INSERT도 된다.
    [TestMethod]
    public async Task Migration_InsertWithoutName_UsesEmptyDefault()
    {
        await using WebApplication app = await StartAsync();
        await using (PowerManagerDbContext db = await app.Services.GetRequiredService<IDbContextFactory<PowerManagerDbContext>>().CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO Devices (Mac, UserId, ModelId, Company, MqttKey, RegisteredAt, LastRequestedAt) VALUES ({Mac}, '', '', '', 'AbCdEfGh12345678', 1, 1)");
        }

        Assert.AreEqual(string.Empty, (await LoadAsync(app)).Name);
        await app.StopAsync();
    }

    public static IEnumerable<object[]> AcceptedNames =>
    [
        ["거실 TV", "거실 TV"],
        ["  거실 " + LineFeed, "거실"],
        [LongestName, LongestName],
        [Tab + LongestName + NextLine, LongestName],
        [string.Concat(Enumerable.Repeat(Emoji, 15)), string.Concat(Enumerable.Repeat(Emoji, 15))],
        [$"{IdeographicSpace}{LineSeparator}거실{ParagraphSeparator}", "거실"],
        [ByteOrderMark + "거실", ByteOrderMark + "거실"],
        ["", ""],
        [Tab.ToString(), ""],
    ];

    // C11: 앞뒤 공백을 없앤 값을 저장하고, 빈 값이면 지운다. 목록·상태 API에 나온다(C13).
    [TestMethod]
    [DynamicData(nameof(AcceptedNames))]
    public async Task Put_AcceptedName_IsStoredAndReturned(string sent, string stored)
    {
        await using WebApplication app = await StartAsync();
        await TestDevices.RegisterAsync(app, Mac);
        await RenameAsync(app, "이전 이름");

        await RenameAsync(app, sent);

        Assert.AreEqual(stored, (await LoadAsync(app)).Name);
        foreach (string path in new[] { "/api/admin/devices", "/api/admin/devices/status" })
        {
            (_, _, string body) = await TestMqtt.SendHttpAsync(app, 80, "GET", path);
            using JsonDocument document = JsonDocument.Parse(body);
            Assert.AreEqual(stored, document.RootElement[0].GetProperty("name").GetString(), path);
        }
        await app.StopAsync();
    }

    public static IEnumerable<object[]> RejectedBodies =>
    [
        ["{}"],
        [Body(null)],
        [Body(LongestName + "가")],
        [Body(string.Concat(Enumerable.Repeat(Emoji, 15)) + "가")],
        [Body($"거{LineFeed}실")],
        [Body($"거{Tab}실")],
        [Body($"거{(char)0}실")],
        [Body($"거{NextLine}실")],
        [Body($"거{LineSeparator}실")],
        [Body($"거{ParagraphSeparator}실")],
    ];

    // C11: 받지 않는 장비명은 400 ProblemDetails이고 저장하지 않는다.
    [TestMethod]
    [DynamicData(nameof(RejectedBodies))]
    public async Task Put_RejectedName_Returns400WithoutStoring(string body)
    {
        await using WebApplication app = await StartAsync();
        await TestDevices.RegisterAsync(app, Mac);
        await RenameAsync(app, "이전 이름");

        (int status, string? contentType, string text) = await PutAsync(app, body);

        Assert.AreEqual(400, status, text);
        Assert.AreEqual("application/problem+json", contentType);
        Assert.AreEqual("이전 이름", (await LoadAsync(app)).Name);
        await app.StopAsync();
    }

    // C11: 판정 순서는 mac 형식(400) → 등록(404) → 장비명(400)이다.
    [TestMethod]
    [DataRow("3a00000008", 400, "Invalid mac")]
    [DataRow("3A0000000801", 400, "Invalid mac")]
    [DataRow("3a00000008ff", 404, "Device not found")]
    [DataRow(Mac, 400, "Invalid device name")]
    public async Task Put_InvalidName_StatusFollowsOrder(string mac, int expected, string title)
    {
        await using WebApplication app = await StartAsync();
        await TestDevices.RegisterAsync(app, Mac);

        (int status, string? contentType, string text) = await PutAsync(app, Body(LongestName + "가"), $"/api/admin/devices/{mac}/name");

        Assert.AreEqual(expected, status, text);
        Assert.AreEqual("application/problem+json", contentType);
        using JsonDocument document = JsonDocument.Parse(text);
        Assert.AreEqual(title, document.RootElement.GetProperty("title").GetString());
        await app.StopAsync();
    }

    // C11: 관리 포트 둘에서 저장하고, 장치 포트는 404이며 저장하지 않는다.
    [TestMethod]
    [DataRow(80, 204)]
    [DataRow(443, 204)]
    [DataRow(18446, 404)]
    [DataRow(18443, 404)]
    public async Task Put_OnlyAdminPort(int port, int expected)
    {
        await using WebApplication app = await StartAsync();
        await TestDevices.RegisterAsync(app, Mac);

        (int status, string? contentType, string text) = await PutAsync(app, Body("거실"), port: port);

        Assert.AreEqual(expected, status, text);
        if (expected == 404)
            Assert.AreEqual("application/problem+json", contentType);
        Assert.AreEqual(expected == 204 ? "거실" : string.Empty, (await LoadAsync(app)).Name);
        await app.StopAsync();
    }

    // C12: 장비명을 바꿔도 다른 값은 그대로이고, 장치가 다시 등록해도 장비명과 응답 키가 그대로다.
    [TestMethod]
    public async Task Name_IsIndependentOfRegistration()
    {
        await using WebApplication app = await StartAsync();
        TestDevice registered = await TestDevices.RegisterAsync(app, Mac);
        DeviceRegistration before = await LoadAsync(app);

        await RenameAsync(app, "거실");

        DeviceRegistration renamed = await LoadAsync(app);
        Assert.AreEqual((before.UserId, before.ModelId, before.Company, before.MqttKey, before.RegisteredAt, before.LastRequestedAt),
            (renamed.UserId, renamed.ModelId, renamed.Company, renamed.MqttKey, renamed.RegisteredAt, renamed.LastRequestedAt));

        TestDevice again = await TestDevices.RegisterAsync(app, Mac, "B540_X");

        Assert.AreEqual(registered.Password, again.Password);
        DeviceRegistration last = await LoadAsync(app);
        Assert.AreEqual("거실", last.Name);
        Assert.AreEqual("B540_X", last.ModelId);

        (int status, _, string body) = await TestMqtt.SendHttpAsync(app, 18446, "POST", "/api/v1/devices/register/create",
            JsonSerializer.Serialize(new { device = new { mac = Mac, model_id = "B540_X" } }));
        Assert.AreEqual(200, status, body);
        using JsonDocument document = JsonDocument.Parse(body);
        CollectionAssert.AreEqual(new[] { "verify", "mqtt_key" }, document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual(registered.Password, document.RootElement.GetProperty("mqtt_key").GetString());
        Assert.AreEqual("거실", (await LoadAsync(app)).Name);
        await app.StopAsync();
    }
}
