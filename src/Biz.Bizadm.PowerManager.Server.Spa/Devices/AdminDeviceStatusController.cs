using Biz.Bizadm.PowerManager.Server.Spa.Admin;
using Microsoft.AspNetCore.Mvc;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;

namespace Biz.Bizadm.PowerManager.Server.Spa.Devices
{
    /// <summary>
    /// 장비 상태 조회·스트림과 마지막으로 보낸 설정 조회 API. 관리 포트에서만 처리한다(<see cref="AdminPortOnlyAttribute"/>).
    /// </summary>
    [ApiController]
    [AdminPortOnly]
    public sealed class AdminDeviceStatusController(DeviceStatusService statuses, DeviceCommandService commands,
        IHostApplicationLifetime lifetime) : ControllerBase
    {
        private const string SnapshotEvent = "snapshot";
        private const string DeviceEvent = "device";

        [HttpGet("/api/admin/devices/status")]
        public Task<IReadOnlyList<DeviceStatusResponse>> List(CancellationToken cancellationToken) => statuses.ListAsync(cancellationToken);

        /// <summary>
        /// 전체 상태(<c>snapshot</c>)를 먼저 보내고, 그 뒤 바뀐 장비의 원소(<c>device</c>)를 보낸다.
        /// 클라이언트가 연결을 끊거나 서버가 종료하면 끝난다.
        /// </summary>
        [HttpGet("/api/admin/devices/status/stream")]
        public IResult Stream() => TypedResults.ServerSentEvents(StreamAsync(HttpContext.RequestAborted));

        [HttpGet("/api/admin/devices/{mac}/settings")]
        public async Task<IActionResult> Settings(string mac, CancellationToken cancellationToken)
        {
            (DeviceCommandResult result, DeviceSettingsResponse? settings) = await commands.GetSettingsAsync(mac, cancellationToken);
            return settings is null ? Problem(statusCode: result.StatusCode, title: result.Title) : Ok(settings);
        }

        private async IAsyncEnumerable<SseItem<object>> StreamAsync(CancellationToken requestAborted,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(
                requestAborted, lifetime.ApplicationStopping, cancellationToken);
            // 전체 상태를 읽는 사이의 변경을 놓치지 않게 먼저 구독한다.
            using DeviceChangeSubscription subscription = statuses.Subscribe();

            IReadOnlyList<DeviceStatusResponse>? snapshot = await UntilStoppedAsync(statuses.ListAsync(stop.Token), stop.Token);
            if (snapshot is null)
                yield break;
            yield return new SseItem<object>(snapshot, SnapshotEvent);

            while (await UntilStoppedAsync(subscription.ReadAsync(stop.Token), stop.Token) is IReadOnlyList<string> macs)
            {
                foreach (string mac in macs)
                {
                    DeviceStatusResponse? status = await UntilStoppedAsync(statuses.FindAsync(mac, stop.Token), stop.Token);
                    if (stop.IsCancellationRequested)
                        yield break;
                    if (status is not null)
                        yield return new SseItem<object>(status, DeviceEvent);
                }
            }
        }

        /// <summary>
        /// 스트림을 끝내는 취소는 오류가 아니므로 null로 바꾼다.
        /// </summary>
        private static async Task<T?> UntilStoppedAsync<T>(Task<T> task, CancellationToken stop) where T : class?
        {
            try
            {
                return await task;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return null;
            }
        }
    }
}
