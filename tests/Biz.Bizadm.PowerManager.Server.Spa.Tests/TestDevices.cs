using Biz.Bizadm.PowerManager.Server.Spa.Devices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

/// <summary>
/// 등록한 시험 장치. 브로커는 등록 장치가 발급 키로 접속할 때만 받으므로(mqtt-broker R9) 테스트 클라이언트는 이 값으로 접속한다.
/// </summary>
internal sealed record TestDevice(string Mac, string ClientId, string UserName, string Password);

internal static class TestDevices
{
    /// <summary>
    /// mac(12자리 16진수 소문자)을 등록하고, ClientId <c>DAWONDNS-{mac}</c>·사용자명 <c>DAWONDNS-B540_W-{mac}</c>·발급 키를 돌려준다.
    /// </summary>
    public static async Task<TestDevice> RegisterAsync(WebApplication app, string mac)
        => await RegisterAsync(app, mac, "B540_W");

    /// <summary>
    /// 모델을 정해 등록한다. 사용자명은 <c>DAWONDNS-{모델}-{mac}</c>이다.
    /// </summary>
    public static async Task<TestDevice> RegisterAsync(WebApplication app, string mac, string modelId)
    {
        DeviceRegistrationService.Result result = await app.Services.GetRequiredService<DeviceRegistrationService>()
            .RegisterAsync(mac, "user@example.com/kakao", modelId, "DAWONDNS", CancellationToken.None);
        return new TestDevice(mac, "DAWONDNS-" + mac, $"DAWONDNS-{modelId}-{mac}", result.MqttKey);
    }

    /// <summary>
    /// 등록 장치의 ClientId·사용자명·비밀번호를 넣는다.
    /// </summary>
    public static MqttClientOptionsBuilder WithDevice(this MqttClientOptionsBuilder builder, TestDevice device)
        => builder.WithClientId(device.ClientId).WithCredentials(device.UserName, device.Password);
}
