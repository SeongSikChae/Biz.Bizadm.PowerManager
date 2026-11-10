using System.Diagnostics;

namespace Biz.Bizadm.PowerManager.DeviceConfig.HostWifi
{
    public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>
    /// 외부 명령 실행. 테스트에서 가짜 구현으로 바꾼다.
    /// </summary>
    public interface IProcessRunner
    {
        Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 셸을 거치지 않고 <see cref="ProcessStartInfo.ArgumentList"/>로 인수를 넘긴다.
    /// </summary>
    public sealed class ProcessRunner : IProcessRunner
    {
        public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new(fileName)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using Process process = Process.Start(startInfo)
                ?? throw new HostWifiException($"Failed to start {fileName}");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }
    }

    internal static class ProcessRunnerExtensions
    {
        /// <summary>
        /// 종료 코드가 0이 아니면 <see cref="HostWifiException"/>을 던진다.
        /// </summary>
        public static async Task<string> RunCheckedAsync(this IProcessRunner runner, string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            ProcessResult result = await runner.RunAsync(fileName, arguments, cancellationToken);
            if (result.ExitCode != 0)
                throw new HostWifiException(
                    $"{fileName} {string.Join(' ', arguments)} failed with exit code {result.ExitCode}: {result.StandardError.Trim()}");
            return result.StandardOutput;
        }
    }
}
