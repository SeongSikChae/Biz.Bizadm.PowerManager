using Biz.Bizadm.PowerManager.Core.Mqtt;

namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    /// <summary>
    /// 릴레이 상태를 모르는 장비에 읽기(<c>r</c> <c>/100/0/30</c>)를 보낸다. 보낼지는 <see cref="DeviceStateStore"/>가 정한다.
    /// </summary>
    public sealed class DeviceRelayReader(DeviceStateStore store, IDeviceCommandPublisher publisher, ILogger<DeviceRelayReader> logger)
    {
        /// <summary>
        /// 읽기를 따로 실행한다. 발행 결과를 기다리는 동안 부른 쪽(브로커의 그 클라이언트 메시지 처리)을 막지 않는다.
        /// </summary>
        public void Start(RelayReadRequest request) => _ = Task.Run(() => RunAsync(request));

        private async Task RunAsync(RelayReadRequest request)
        {
            bool enqueued = false;
            try
            {
                DevicePublishResult result = await publisher.PublishAsync(request.Target, MqttOperation.Read,
                    [new MqttElement(DeviceStateStore.RelayReadPath, string.Empty)], CancellationToken.None);
                enqueued = result.Enqueued;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Relay read failed: mac={Mac}", request.Target.Mac);
            }
            finally
            {
                store.CompleteRelayRead(request, enqueued);
            }
        }
    }
}
