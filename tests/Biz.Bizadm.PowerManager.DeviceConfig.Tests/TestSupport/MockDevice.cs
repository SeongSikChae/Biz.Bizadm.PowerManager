using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.TestSupport
{
    /// <summary>
    /// loopback 임시 포트에서 연결 하나를 받는 모의 장치.
    /// </summary>
    internal sealed class MockDevice : IAsyncDisposable
    {
        private static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(5);

        private readonly TcpListener listener;
        private readonly Task<TcpClient> acceptTask;
        private readonly StringBuilder pending = new();
        private readonly byte[] readBuffer = new byte[1500];

        private MockDevice()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            acceptTask = listener.AcceptTcpClientAsync();
        }

        public static MockDevice Start() => new();

        public string Host => IPAddress.Loopback.ToString();

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

        /// <summary>
        /// PC가 보낸 한 줄('\n' 제외)을 읽는다.
        /// </summary>
        public async Task<string> ReadLineAsync()
        {
            NetworkStream stream = (await acceptTask.WaitAsync(IoTimeout)).GetStream();
            while (true)
            {
                string text = pending.ToString();
                int newline = text.IndexOf('\n');
                if (newline >= 0)
                {
                    pending.Remove(0, newline + 1);
                    return text[..newline];
                }
                int read = await stream.ReadAsync(readBuffer).AsTask().WaitAsync(IoTimeout);
                if (read == 0)
                    throw new IOException($"PC closed the connection. Pending: {text}");
                pending.Append(Encoding.ASCII.GetString(readBuffer, 0, read));
            }
        }

        public async Task SendAsync(string text)
        {
            NetworkStream stream = (await acceptTask.WaitAsync(IoTimeout)).GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(text));
            await stream.FlushAsync();
        }

        /// <summary>
        /// 장치 쪽에서 연결을 끊는다.
        /// </summary>
        public async Task CloseAsync() => (await acceptTask.WaitAsync(IoTimeout)).Close();

        public async ValueTask DisposeAsync()
        {
            listener.Stop();
            if (acceptTask.IsCompletedSuccessfully)
                (await acceptTask).Dispose();
        }
    }
}
