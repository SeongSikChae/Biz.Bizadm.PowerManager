using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.HostWifi
{
    /// <summary>
    /// 호출 인수를 기록하고, 인수 문자열에 맞는 미리 정한 결과를 돌려준다.
    /// </summary>
    internal sealed class FakeProcessRunner : IProcessRunner
    {
        private readonly List<(Func<string, bool> Match, ProcessResult Result)> responses = [];

        public List<string> Calls { get; } = [];

        public FakeProcessRunner On(string argumentsContains, string stdout, int exitCode = 0, string stderr = "")
        {
            responses.Add((args => args.Contains(argumentsContains, StringComparison.Ordinal), new ProcessResult(exitCode, stdout, stderr)));
            return this;
        }

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            string call = $"{fileName} {string.Join('|', arguments)}";
            Calls.Add(call);
            foreach ((Func<string, bool> match, ProcessResult result) in responses)
                if (match(call))
                    return Task.FromResult(result);
            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
        }
    }
}
