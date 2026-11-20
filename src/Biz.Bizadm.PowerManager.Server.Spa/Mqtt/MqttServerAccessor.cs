using MQTTnet.Server;

namespace Biz.Bizadm.PowerManager.Server.Spa.Mqtt
{
    /// <summary>
    /// 실행 중인 내장 브로커 참조. <see cref="MqttBrokerHostedService"/>가 시작할 때 넣고 멈출 때 비운다.
    /// 브로커를 만드는 호스티드 서비스가 핸들러를 주입받으므로, 핸들러가 브로커에 발행할 때 순환 의존 없이 이 싱글턴으로 받는다.
    /// </summary>
    public sealed class MqttServerAccessor
    {
        private volatile MqttServer? server;

        /// <summary>
        /// 실행 중인 브로커. 시작 전이나 종료 중이면 null이다.
        /// </summary>
        public MqttServer? Server
        {
            get => server;
            set => server = value;
        }
    }
}
