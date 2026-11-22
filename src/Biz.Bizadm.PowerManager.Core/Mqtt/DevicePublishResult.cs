namespace Biz.Bizadm.PowerManager.Core.Mqtt
{
    /// <summary>
    /// 명령 발행 결과. <paramref name="Enqueued"/>는 브로커가 대상 장비 클라이언트의 전송 큐에 메시지를 넣었는지이며,
    /// 장치가 받았거나 처리했다는 뜻이 아니다.
    /// </summary>
    public sealed record DevicePublishResult(bool Enqueued, string Topic, string Sid);
}
