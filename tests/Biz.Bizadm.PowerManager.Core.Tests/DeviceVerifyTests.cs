using Biz.Bizadm.PowerManager.Core.Devices;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Core.Tests;

[TestClass]
public sealed class DeviceVerifyTests
{
    /// <summary>
    /// 기대값은 서버 구현이 아니라, 실제 다원 응답과 일치함을 확인한 Python 참조 계산으로 만든 값이다(입력은 가상).
    /// </summary>
    [TestMethod]
    [DataRow("dwmqtt.dawonai.com", "0123456789ab", "AbCdEfGh12345678", "7L.eB2kswATShmfbnM2JFsl6HrH+hkaw4JI3xz0NxU2J")]
    [DataRow("192.0.2.10", "a1b2c3d4e5f6", "ZzYyXx0987654321", "Le0=MTDlQIdlTyE4f-GNO05mxB67Me2-OxPb/YTwuetn")]
    [DataRow("iot.example.com", "ffeeddccbbaa", "Q1w2E3r4T5y6U7i8", "Kl.ykhN.6jj-2BLiCbSfI0AtCy-lqqa7uwkVUG+Fk4ZY")]
    [DataRow("dwmqtt.dawonai.com", "000000000001", "abcdefghijklmnopqrst", "q1yTx0-pTP.WJq1TlOxceB306Pys.+ku6t=lQUhlPnU-")]
    public void Compute_MatchesReference(string serverAddr, string mac, string mqttKey, string expected)
    {
        Assert.AreEqual(expected, DeviceVerify.Compute(serverAddr, mac, mqttKey));
    }

    [TestMethod]
    [DataRow("dwmqtt.dawonai.com")]
    [DataRow("192.0.2.10")]
    [DataRow("a-b.c=d/e+f")]
    public void IsValidServerAddr_AlphabetOnly_True(string serverAddr)
    {
        Assert.IsTrue(DeviceVerify.IsValidServerAddr(serverAddr));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("host:8883")]
    [DataRow("my_host")]
    [DataRow("my host")]
    public void IsValidServerAddr_Invalid_False(string? serverAddr)
    {
        Assert.IsFalse(DeviceVerify.IsValidServerAddr(serverAddr));
    }

    [TestMethod]
    public void Compute_OutsideAlphabet_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => DeviceVerify.Compute("my_host", "0123456789ab", "AbCdEfGh12345678"));
    }

    /// <summary>
    /// 실측 대조(수동). 실제 다원 응답 JSON 파일 경로와 그 장치 mac을 환경 변수로 줄 때만 실행한다.
    /// 실제 값은 저장소에 두지 않는다.
    /// </summary>
    [TestMethod]
    public void RealDawonResponse_Matches()
    {
        string? path = Environment.GetEnvironmentVariable("POWERMANAGER_DAWON_RESPONSE");
        string? mac = Environment.GetEnvironmentVariable("POWERMANAGER_DAWON_MAC");
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(mac))
        {
            Assert.Inconclusive("POWERMANAGER_DAWON_RESPONSE·POWERMANAGER_DAWON_MAC가 없어 실측 대조를 건너뛴다.");
            return;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        string verify = document.RootElement.GetProperty("verify").GetString()!;
        string mqttKey = document.RootElement.GetProperty("mqtt_key").GetString()!;

        // 값 자체는 메시지에 넣지 않는다.
        Assert.IsTrue(DeviceVerify.Compute("dwmqtt.dawonai.com", mac, mqttKey) == verify, "verify 불일치");
    }
}
