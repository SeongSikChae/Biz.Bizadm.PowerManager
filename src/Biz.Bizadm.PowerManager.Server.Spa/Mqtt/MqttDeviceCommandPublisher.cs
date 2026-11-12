using Biz.Bizadm.PowerManager.Core.Mqtt;

namespace Biz.Bizadm.PowerManager.Server.Spa.Mqtt
{
    /// <summary>
    /// <see cref="IDeviceCommandPublisher"/> 껍데기. 발행(w/chg/reb)은 아직 구현하지 않는다.
    /// </summary>
    public sealed class MqttDeviceCommandPublisher : IDeviceCommandPublisher
    {
        public Task WriteAsync(string prefix, string deviceKey, IReadOnlyList<MqttElement> elements, CancellationToken ct)
            => throw new NotImplementedException();

        public Task ChangeAsync(string prefix, string deviceKey, IReadOnlyList<MqttElement> elements, CancellationToken ct)
            => throw new NotImplementedException();

        public Task RebootAsync(string prefix, string deviceKey, CancellationToken ct)
            => throw new NotImplementedException();
    }
}
