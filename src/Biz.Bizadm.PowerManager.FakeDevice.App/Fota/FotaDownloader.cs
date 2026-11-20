using Biz.Bizadm.PowerManager.FakeDevice.App.Recording;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Security.Cryptography;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Fota
{
    /// <summary>
    /// FOTA 이미지 요청(받은 URL + 반대편 슬롯 접미사)과 단일 GET 요청을 장치 펌웨어의 고정 헤더로 보내고,
    /// 200 응답 본문을 기록 폴더 아래 <c>fota</c>에 저장한다. 요청마다 시간 제한과 크기 상한을 두며, 받은 파일은 실행하지 않는다.
    /// </summary>
    internal sealed class FotaDownloader : IDisposable
    {
        public const string PurposeImage = "image";
        public const string PurposeGet = "get";

        private readonly FakeDeviceOptions options;
        private readonly FakeDeviceRecorder recorder;
        private readonly TextWriter output;
        private readonly TimeProvider timeProvider;
        private readonly ILogger logger;
        private readonly HttpClient client;
        private int sequence;

        public FotaDownloader(FakeDeviceOptions options, FakeDeviceRecorder recorder, TextWriter output, TimeProvider timeProvider, ILogger logger)
        {
            this.options = options;
            this.recorder = recorder;
            this.output = output;
            this.timeProvider = timeProvider;
            this.logger = logger;
            // 받은 바이트를 그대로 저장하고 Content-Encoding·Location을 기록한다(plan 결정 절)
            SocketsHttpHandler handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
            };
            if (options.SkipCertificateValidation)
                handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        /// <summary>
        /// B540 펌웨어 덤프(슬롯 B 0xdf6ec 부근)의 고정 헤더 묶음을 같은 순서로 넣은 GET 요청. <c>User-Agent</c>만 설정값이다.
        /// </summary>
        public static HttpRequestMessage CreateRequest(Uri url, string userAgent)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url) { Version = HttpVersion.Version11 };
            request.Headers.TryAddWithoutValidation("Connection", "keep-alive");
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            request.Headers.TryAddWithoutValidation("Accept", "*/*");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip,deflate");
            // "eb-US"는 펌웨어에 있는 그대로다
            request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,eb-US;q=0.8");
            return request;
        }

        /// <summary>
        /// 장치처럼 받은 URL 문자열 끝에 슬롯 접미사를 그대로 붙인다. 장치는 호스트 뒤 경로 필드 끝에 붙이므로 결과가 같다(끝 <c>/</c>도 손대지 않음).
        /// </summary>
        public static string ImageUrl(string url, string suffix) => url + suffix;

        /// <summary>장치와 같은 이미지 요청 하나(<c>GET {URL}{접미사}</c>)를 보낸다. 버전 요청은 보내지 않는다.</summary>
        public Task DownloadImageAsync(string url, CancellationToken cancellationToken)
            => DownloadAsync(ImageUrl(url, options.FotaSuffix), PurposeImage, cancellationToken);

        /// <summary>요청 하나를 보낸다. 실패해도 예외를 던지지 않고 기록·콘솔에 남긴다.</summary>
        public async Task DownloadAsync(string url, string purpose, CancellationToken cancellationToken)
        {
            int id = Interlocked.Increment(ref sequence);
            await recorder.FotaRequestAsync(id, purpose, url);
            await output.WriteLineAsync($"FOTA #{id} {purpose} GET {url}");

            string directory = Path.Combine(options.RecordDirectory, "fota");
            string fileName = $"{timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd'T'HHmmssfff'Z'}-{id:D4}-{purpose}.bin";
            string filePath = Path.Combine(directory, fileName);
            string partPath = filePath + ".part";

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.FotaTimeout);
            try
            {
                using HttpRequestMessage request = CreateRequest(new Uri(url), options.UserAgent);
                using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                int status = (int)response.StatusCode;
                long? contentLength = response.Content.Headers.ContentLength;
                string? contentType = response.Content.Headers.ContentType?.ToString();
                string? contentEncoding = response.Content.Headers.ContentEncoding.Count > 0 ? string.Join(", ", response.Content.Headers.ContentEncoding) : null;
                string? location = response.Headers.Location?.OriginalString;

                if (contentLength > options.FotaMaxBytes)
                {
                    await FailAsync(id, url, "too-large");
                    return;
                }

                if (status != 200)
                {
                    await recorder.FotaResponseAsync(new FotaResponseRecord(id, url, status, contentType, contentLength, contentEncoding, location, null, null, null));
                    await output.WriteLineAsync($"FOTA #{id} status {status}");
                    return;
                }

                await using Stream body = await response.Content.ReadAsStreamAsync(timeout.Token);
                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long total = 0;
                Directory.CreateDirectory(directory);
                await using (FileStream file = new FileStream(partPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = await body.ReadAsync(buffer, timeout.Token)) > 0)
                    {
                        total += read;
                        if (total > options.FotaMaxBytes)
                        {
                            await file.DisposeAsync();
                            await FailAsync(id, url, "too-large");
                            return;
                        }
                        hash.AppendData(buffer, 0, read);
                        await file.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                    }
                }
                string sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
                File.Move(partPath, filePath);
                string recordedFile = "fota/" + fileName;
                await recorder.FotaResponseAsync(new FotaResponseRecord(id, url, status, contentType, contentLength, contentEncoding, location, total, sha256, recordedFile));
                await output.WriteLineAsync($"FOTA #{id} status {status}, {total} bytes, sha256 {sha256}, saved {recordedFile}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await FailAsync(id, url, "cancelled");
            }
            catch (OperationCanceledException)
            {
                await FailAsync(id, url, "timeout");
            }
            catch (Exception ex)
            {
                await FailAsync(id, url, ex.Message);
            }
            finally
            {
                TryDelete(partPath);
            }
        }

        private async Task FailAsync(int id, string url, string error)
        {
            logger.LogWarning("FOTA #{Id} failed: {Url} {Error}", id, url, error);
            await recorder.FotaErrorAsync(id, url, error);
            await output.WriteLineAsync($"FOTA #{id} error: {error}");
        }

        private void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException ex)
            {
                logger.LogWarning("FOTA partial file delete failed: {Path} {Error}", path, ex.Message);
            }
        }

        public void Dispose() => client.Dispose();
    }
}
