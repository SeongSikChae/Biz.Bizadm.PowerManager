using Biz.Bizadm.PowerManager.FakeDevice.App.Mqtt;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using System.Security.Authentication;
using System.Text;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests;

[TestClass]
public sealed class DeviceMqttClientOptionsTests
{
    private static FakeDeviceOptions Options(bool skipValidation) => TestHarness.Options(null, null, Path.GetTempPath()) with
    {
        MqttServerHost = "dwmqtt.dawonai.com",
        MqttServerPort = 8883,
        SkipCertificateValidation = skipValidation,
        KeepAliveSeconds = 45,
        WillTopic = "/lwt",
        WillPayload = "offline",
    };

    [TestMethod]
    public void Will_TopicPayloadQos0NoRetain()
    {
        MqttClientOptions options = DeviceMqttClient.BuildOptions(Options(false), "pw");

        Assert.AreEqual("/lwt", options.WillTopic);
        Assert.AreEqual("offline", Encoding.UTF8.GetString(options.WillPayload));
        Assert.AreEqual(MqttQualityOfServiceLevel.AtMostOnce, options.WillQualityOfServiceLevel);
        Assert.IsFalse(options.WillRetain);
    }

    [TestMethod]
    public void Protocol_V310_CleanSession_KeepAlive()
    {
        MqttClientOptions options = DeviceMqttClient.BuildOptions(Options(false), "pw");

        Assert.AreEqual(MqttProtocolVersion.V310, options.ProtocolVersion);
        Assert.IsTrue(options.CleanSession);
        Assert.AreEqual(TimeSpan.FromSeconds(45), options.KeepAlivePeriod);
        Assert.AreEqual(TimeSpan.FromSeconds(2), options.Timeout);
        Assert.AreEqual(TestHarness.ClientId, options.ClientId);
    }

    [TestMethod]
    public void Tls12_TargetHost_Validation()
    {
        MqttClientTcpOptions tcp = (MqttClientTcpOptions)DeviceMqttClient.BuildOptions(Options(false), "pw").ChannelOptions;

        Assert.IsTrue(tcp.TlsOptions.UseTls);
        Assert.AreEqual(SslProtocols.Tls12, tcp.TlsOptions.SslProtocol);
        Assert.AreEqual("dwmqtt.dawonai.com", tcp.TlsOptions.TargetHost);
        Assert.IsNull(tcp.TlsOptions.CertificateValidationHandler);

        MqttClientTcpOptions skipped = (MqttClientTcpOptions)DeviceMqttClient.BuildOptions(Options(true), "pw").ChannelOptions;
        Assert.IsNotNull(skipped.TlsOptions.CertificateValidationHandler);
    }

    [TestMethod]
    public void Credentials_UserNameAndUtf8Password()
    {
        MqttClientOptions options = DeviceMqttClient.BuildOptions(Options(false), "비밀pw");

        Assert.AreEqual(TestHarness.DeviceKey, options.Credentials.GetUserName(options));
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("비밀pw"), options.Credentials.GetPassword(options));
    }
}
