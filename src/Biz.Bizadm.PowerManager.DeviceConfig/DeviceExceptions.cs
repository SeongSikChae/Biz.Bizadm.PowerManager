namespace Biz.Bizadm.PowerManager.DeviceConfig
{
    /// <summary>
    /// 장치 응답이 예상한 프로토콜 형식과 다르다.
    /// </summary>
    public class DeviceProtocolException(string message) : Exception(message)
    {
    }

    /// <summary>
    /// 응답을 기다리는 중 장치가 연결을 끊었다.
    /// </summary>
    public sealed class DeviceDisconnectedException(string received)
        : Exception($"Device closed the connection. Received: {received}")
    {
        /// <summary>끊기기 전까지 받은(아직 프레임이 완성되지 않은) 내용.</summary>
        public string Received { get; } = received;
    }
}
