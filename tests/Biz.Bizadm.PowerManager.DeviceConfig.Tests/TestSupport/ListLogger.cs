using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.TestSupport
{
    /// <summary>로그 메시지(예외 메시지 포함)를 모아 두는 테스트용 로거.</summary>
    internal sealed class ListLogger : ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public string AllText => string.Join("\n", Messages);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Enqueue($"{logLevel}: {formatter(state, exception)}{(exception is null ? "" : " " + exception)}");
        }
    }
}
