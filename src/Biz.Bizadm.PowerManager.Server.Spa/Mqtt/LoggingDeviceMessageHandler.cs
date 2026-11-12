using Biz.Bizadm.PowerManager.Core.Mqtt;

namespace Biz.Bizadm.PowerManager.Server.Spa.Mqtt
{
    /// <summary>
    /// <see cref="IDeviceMessageHandler"/> 껍데기. 수신 내용을 로그로만 남긴다.
    /// </summary>
    public sealed class LoggingDeviceMessageHandler(ILogger<LoggingDeviceMessageHandler> logger) : IDeviceMessageHandler
    {
        public Task RegisterAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct) => Log(topic, envelope);

        public Task DeregisterAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct) => Log(topic, envelope);

        public Task TimeSyncAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct) => Log(topic, envelope);

        public Task NotifyAsync(DeviceTopic topic, MqttEnvelope envelope, CancellationToken ct) => Log(topic, envelope);

        public Task OfflineAsync(DeviceTopic topic, CancellationToken ct)
        {
            logger.LogInformation("Device offline (LWT): prefix={Prefix}, device={DeviceKey}", topic.Prefix, topic.DeviceKey);
            return Task.CompletedTask;
        }

        private Task Log(DeviceTopic topic, MqttEnvelope envelope)
        {
            logger.LogInformation("Device message {Operation}: prefix={Prefix}, device={DeviceKey}, sid={Sid}, elements={Elements}",
                envelope.Msg.Operation, topic.Prefix, topic.DeviceKey, envelope.Sid,
                string.Join(", ", envelope.Msg.Elements.Select(e => $"{e.Path}={e.StringValue}")));
            return Task.CompletedTask;
        }
    }
}
