using Biz.Bizadm.PowerManager.Core.Mqtt;
using Biz.Bizadm.PowerManager.FakeDevice.App.Console;
using Biz.Bizadm.PowerManager.FakeDevice.App.Fota;
using Biz.Bizadm.PowerManager.FakeDevice.App.Mqtt;
using Biz.Bizadm.PowerManager.FakeDevice.App.Recording;
using Biz.Bizadm.PowerManager.FakeDevice.App.Rest;
using Microsoft.Extensions.Logging;
using MQTTnet.Protocol;
using System.Collections.Concurrent;
using System.Text;

namespace Biz.Bizadm.PowerManager.FakeDevice.App
{
    /// <summary>
    /// 가짜 장비 실행 흐름(plan 실행기 흐름과 종료 표): 등록(C2) → 접속 조회(C3) → MQTT 접속(C4) → 구독(C5) → 정해진 발행(C6)
    /// → 주기 발행·콘솔 명령(C7)·수신 처리(C8)·FOTA 받기(C14~C16) → 종료(C11). 한 번만 실행한다.
    /// </summary>
    internal sealed class FakeDeviceRunner(
        FakeDeviceOptions options,
        FakeDeviceRecorder recorder,
        TextReader input,
        TextWriter output,
        TimeProvider timeProvider,
        ILogger<FakeDeviceRunner> logger)
    {
        private static readonly TimeSpan CommandLoopWait = TimeSpan.FromSeconds(1);

        private readonly TextWriter console = TextWriter.Synchronized(output);
        private readonly ConcurrentDictionary<string, byte> autoDownloaded = new(StringComparer.Ordinal);
        private readonly List<Task> downloads = [];
        private readonly TaskCompletionSource quitRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> serverDisconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly string mqttTarget = $"{options.MqttServerHost}:{options.MqttServerPort}";
        private DeviceMqttClient? mqtt;
        private FotaDownloader? fota;
        private CancellationToken runningToken;
        private bool stopping;

        /// <returns>정상 종료(종료 명령·정지 토큰)면 true. 등록 실패·접속 거부·실패·SUBACK 무응답·다원 쪽 끊김이면 false.</returns>
        public async Task<bool> RunAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Fake device starting: {Options}", options);

            // C2·C3
            string? mqttKey = options.MqttKey;
            using (DawonRestClient rest = new DawonRestClient(options, recorder, console, logger))
            {
                if (mqttKey is null)
                {
                    mqttKey = await rest.RegisterAsync(stoppingToken);
                    if (mqttKey is null)
                        return false;
                }
                await rest.ConnectAsync(stoppingToken);
            }

            using CancellationTokenSource running = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            runningToken = running.Token;
            using DeviceMqttClient client = new DeviceMqttClient(options);
            using FotaDownloader downloader = new FotaDownloader(options, recorder, console, timeProvider, logger);
            mqtt = client;
            fota = downloader;
            client.MessageReceived += OnMessageReceivedAsync;
            client.DisconnectedByServer += reason =>
            {
                serverDisconnected.TrySetResult(reason);
                return Task.CompletedTask;
            };

            // C4
            string? password = options.Password ?? mqttKey;
            await recorder.MqttConnectAsync(options.ClientId, options.UserName, !string.IsNullOrEmpty(password), options.KeepAliveSeconds,
                cleanSession: true, options.WillTopic, Encoding.UTF8.GetBytes(options.WillPayload), willQos: 0, willRetain: false);
            MqttConnectOutcome connect = await client.ConnectAsync(password, stoppingToken);
            if (connect.ReturnCode is not { } returnCode)
            {
                await recorder.MqttErrorAsync(options.ClientId, mqttTarget, connect.Error!);
                await console.WriteLineAsync($"MQTT connect error: {connect.Error}");
                return false;
            }
            await recorder.MqttConnAckAsync(options.ClientId, returnCode);
            await console.WriteLineAsync($"MQTT CONNACK {returnCode}");
            if (returnCode != 0)
                return false;

            // C5
            string filter = DeviceTopic.ToDevice(options.TopicPrefix, options.DeviceKey) + "/#";
            await recorder.MqttSubscribeAsync(options.ClientId, [(filter, 0)]);
            MqttSubscribeOutcome subscribe = await client.SubscribeAsync(filter, MqttQualityOfServiceLevel.AtMostOnce, stoppingToken);
            if (subscribe.Results is not { } results)
            {
                await recorder.MqttErrorAsync(options.ClientId, mqttTarget, subscribe.Error!);
                await console.WriteLineAsync($"MQTT subscribe error: {subscribe.Error}");
                // 구독 중에 접속이 끊겼으면 다원 쪽 끊김으로 남긴다. 끊김 알림은 구독 실패보다 늦게 올 수 있어 연결 상태로 본다
                if (!client.IsConnected)
                    await recorder.MqttDisconnectAsync(FakeDeviceRecorder.DawonToDevice, options.ClientId, clean: false);
                else
                    await DisconnectAsync();
                return false;
            }
            await recorder.MqttSubAckAsync(options.ClientId, results);
            await console.WriteLineAsync(subscribe.HasFailure
                ? $"MQTT SUBACK failure: {string.Join(",", results)}"
                : $"MQTT SUBACK {string.Join(",", results)}");

            // C6
            if (options.SendRegister)
                await PublishAsync(Up(options.RegisterSuffix), DevicePayloads.Register(options.Company, options.Model, options.Mac, options.FirmwareVersion), runningToken);
            if (options.SendTimeSync)
                await PublishAsync(Up(DevicePayloads.TimeSyncSuffix), DevicePayloads.TimeSync(), runningToken);
            if (options.NotifyInterval > TimeSpan.Zero)
                await PublishAsync(Up(DevicePayloads.NotifySuffix), DevicePayloads.Notify(options.NotifyElements), runningToken);

            List<Task> loops = [];
            if (options.SendTimeSync && options.TimeSyncInterval > TimeSpan.Zero)
                loops.Add(Task.Run(() => PeriodicAsync(options.TimeSyncInterval, DevicePayloads.TimeSyncSuffix, DevicePayloads.TimeSync)));
            if (options.NotifyInterval > TimeSpan.Zero)
                loops.Add(Task.Run(() => PeriodicAsync(options.NotifyInterval, DevicePayloads.NotifySuffix, () => DevicePayloads.Notify(options.NotifyElements))));

            // C7: 콘솔 읽기는 동기로 막힐 수 있어(Console.In) 따로 띄운다
            Task commands = Task.Run(CommandLoopAsync);

            TaskCompletionSource stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using CancellationTokenRegistration registration = stoppingToken.Register(() => stopRequested.TrySetResult());
            Task finished = await Task.WhenAny(quitRequested.Task, stopRequested.Task, serverDisconnected.Task);

            // C11
            lock (downloads)
                stopping = true;
            running.Cancel();
            await WhenAllQuietly(loops);
            Task[] pending;
            lock (downloads)
                pending = [.. downloads];
            await WhenAllQuietly(pending);
            await Task.WhenAny(commands, Task.Delay(CommandLoopWait, CancellationToken.None));

            if (finished == serverDisconnected.Task)
            {
                string reason = await serverDisconnected.Task;
                await recorder.MqttDisconnectAsync(FakeDeviceRecorder.DawonToDevice, options.ClientId, clean: false);
                await console.WriteLineAsync($"MQTT disconnected by Dawon: {reason}");
                logger.LogWarning("Disconnected by Dawon: {Reason}", reason);
                return false;
            }

            await DisconnectAsync();
            logger.LogInformation("Fake device stopped");
            return true;
        }

        private string Up(string suffix) => $"{options.TopicPrefix}.v1/{options.DeviceKey}/iot-server/{suffix}";

        /// <summary>DISCONNECT를 보내고 종료 항목(<c>clean: true</c>)을 기록한다.</summary>
        private async Task DisconnectAsync()
        {
            try
            {
                await mqtt!.DisconnectAsync();
            }
            catch (Exception ex)
            {
                await recorder.MqttErrorAsync(options.ClientId, mqttTarget, DeviceMqttClient.ErrorText(ex, CancellationToken.None));
            }
            await recorder.MqttDisconnectAsync(FakeDeviceRecorder.DeviceToDawon, options.ClientId, clean: true);
            await console.WriteLineAsync("MQTT disconnected");
        }

        /// <summary>발행하고 기록·콘솔에 남긴다. 실패는 기록·콘솔 후 계속한다(C6).</summary>
        private async Task PublishAsync(string topic, byte[] payload, CancellationToken cancellationToken)
        {
            try
            {
                await mqtt!.PublishAsync(topic, payload, (MqttQualityOfServiceLevel)options.QoS, cancellationToken);
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;
                string error = DeviceMqttClient.ErrorText(ex, cancellationToken);
                await recorder.MqttErrorAsync(options.ClientId, mqttTarget, $"publish {topic}: {error}");
                await console.WriteLineAsync($"MQTT publish error: {topic} {error}");
                return;
            }
            await recorder.MqttPublishAsync(FakeDeviceRecorder.DeviceToDawon, options.ClientId, topic, options.QoS, retain: false, payload);
            await console.WriteLineAsync($">> {topic} {Text(payload)}");
        }

        private async Task PeriodicAsync(TimeSpan interval, string suffix, Func<byte[]> payload)
        {
            using PeriodicTimer timer = new PeriodicTimer(interval, timeProvider);
            try
            {
                while (await timer.WaitForNextTickAsync(runningToken))
                    await PublishAsync(Up(suffix), payload(), runningToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task CommandLoopAsync()
        {
            while (!runningToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await input.ReadLineAsync(runningToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                if (line is null)
                {
                    // 입력이 끝나도 실행은 계속한다(plan 결정 절)
                    await console.WriteLineAsync("Console input closed; press Ctrl+C to stop");
                    return;
                }
                if (runningToken.IsCancellationRequested)
                    return;

                ConsoleCommand command = ConsoleCommand.Parse(line);
                switch (command.Kind)
                {
                    case ConsoleCommandKind.Publish:
                        await PublishAsync(command.Target!, Encoding.UTF8.GetBytes(command.Payload!), runningToken);
                        break;
                    case ConsoleCommandKind.PublishSuffix:
                        await PublishAsync(Up(command.Target!), Encoding.UTF8.GetBytes(command.Payload!), runningToken);
                        break;
                    case ConsoleCommandKind.Fota:
                        StartDownload(token => fota!.DownloadImageAsync(command.Target!, token));
                        break;
                    case ConsoleCommandKind.Get:
                        StartDownload(token => fota!.DownloadAsync(command.Target!, FotaDownloader.PurposeGet, token));
                        break;
                    case ConsoleCommandKind.Quit:
                        quitRequested.TrySetResult();
                        return;
                    default:
                        await console.WriteLineAsync(ConsoleCommand.UsageText);
                        break;
                }
            }
        }

        /// <summary>받은 메시지를 기록·콘솔에 남기고 FOTA URL 후보를 찾는다(C8·C14). 응답하지 않는다. 받기는 따로 띄운다.</summary>
        private async Task OnMessageReceivedAsync(MqttReceivedMessage message)
        {
            await recorder.MqttPublishAsync(FakeDeviceRecorder.DawonToDevice, options.ClientId, message.Topic, message.QoS, message.Retain, message.Payload);
            await console.WriteLineAsync($"<< {message.Topic} {Text(message.Payload)}");
            foreach (string url in FotaUrlFinder.Find(message.Payload))
            {
                await recorder.FotaCandidateAsync(message.Topic, url);
                await console.WriteLineAsync($"FOTA candidate: {url}");
                if (options.FotaAutoDownload && autoDownloaded.TryAdd(url, 0))
                    StartDownload(token => fota!.DownloadImageAsync(url, token));
            }
        }

        private void StartDownload(Func<CancellationToken, Task> download)
        {
            lock (downloads)
            {
                if (stopping)
                    return;
                downloads.Add(Task.Run(() => download(runningToken)));
            }
        }

        private static async Task WhenAllQuietly(IEnumerable<Task> tasks)
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception)
            {
                // 각 작업이 실패를 스스로 기록한다
            }
        }

        private static string Text(byte[] payload) => Encoding.UTF8.GetString(payload);
    }
}
