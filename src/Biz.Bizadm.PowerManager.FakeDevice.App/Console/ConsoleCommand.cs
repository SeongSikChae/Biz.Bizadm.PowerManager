namespace Biz.Bizadm.PowerManager.FakeDevice.App.Console
{
    internal enum ConsoleCommandKind
    {
        /// <summary><c>pub &lt;토픽&gt; &lt;페이로드&gt;</c></summary>
        Publish,

        /// <summary><c>up &lt;접미&gt; &lt;페이로드&gt;</c></summary>
        PublishSuffix,

        /// <summary><c>fota &lt;URL&gt;</c>: 장치와 같은 이미지 요청 하나(URL + 반대편 슬롯 접미사)</summary>
        Fota,

        /// <summary><c>get &lt;URL&gt;</c>: URL 하나만 GET</summary>
        Get,

        /// <summary><c>quit</c>·<c>exit</c></summary>
        Quit,

        /// <summary><c>help</c></summary>
        Help,

        /// <summary>알 수 없는 명령이나 인자 모자람. 사용법을 쓰고 무시한다.</summary>
        Usage,
    }

    /// <summary>
    /// 콘솔 한 줄을 명령으로 해석한다(plan 콘솔 명령 표). 앞뒤 공백을 무시하고 명령어는 대소문자를 무시한다.
    /// </summary>
    internal sealed record ConsoleCommand(ConsoleCommandKind Kind, string? Target = null, string? Payload = null)
    {
        public const string UsageText =
            "Commands:\n" +
            "  pub <topic> <payload>   publish to the topic as is\n" +
            "  up <suffix> <payload>   publish to {prefix}.v1/{device key}/iot-server/{suffix}\n" +
            "  fota <url>              GET {url}{slot suffix} (RunningSlot 2 -> _1, 1 -> _2)\n" +
            "  get <url>               GET {url}\n" +
            "  quit | exit             disconnect and exit\n" +
            "  help                    show this help";

        private static readonly ConsoleCommand UsageCommand = new(ConsoleCommandKind.Usage);

        /// <param name="line">입력 한 줄.</param>
        /// <returns>
        /// <see cref="ConsoleCommandKind.Publish"/>·<see cref="ConsoleCommandKind.PublishSuffix"/>는 <c>Target</c>에 토픽·접미,
        /// <c>Payload</c>에 토픽 뒤 첫 공백 다음의 나머지 줄 전체를 담는다.
        /// <see cref="ConsoleCommandKind.Fota"/>·<see cref="ConsoleCommandKind.Get"/>은 <c>Target</c>에 입력한 URL 문자열을 담는다.
        /// </returns>
        public static ConsoleCommand Parse(string? line)
        {
            string trimmed = (line ?? "").Trim();
            (string name, string rest) = SplitFirst(trimmed);
            switch (name.ToLowerInvariant())
            {
                case "pub":
                case "up":
                {
                    (string target, string payload) = SplitFirst(rest, trimRest: false);
                    if (target.Length == 0 || payload.Length == 0)
                        return UsageCommand;
                    return new ConsoleCommand(name.Equals("pub", StringComparison.OrdinalIgnoreCase) ? ConsoleCommandKind.Publish : ConsoleCommandKind.PublishSuffix, target, payload);
                }
                case "fota":
                case "get":
                {
                    (string url, string extra) = SplitFirst(rest);
                    if (extra.Length != 0 || !IsHttpUrl(url))
                        return UsageCommand;
                    return new ConsoleCommand(name.Equals("fota", StringComparison.OrdinalIgnoreCase) ? ConsoleCommandKind.Fota : ConsoleCommandKind.Get, url);
                }
                case "quit":
                case "exit":
                    return rest.Length == 0 ? new ConsoleCommand(ConsoleCommandKind.Quit) : UsageCommand;
                case "help":
                    return new ConsoleCommand(ConsoleCommandKind.Help);
                default:
                    return UsageCommand;
            }
        }

        /// <summary><c>http</c>·<c>https</c> 절대 URI인지.</summary>
        public static bool IsHttpUrl(string value)
            => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        /// <summary>첫 공백(스페이스·탭) 앞과, 그 공백 다음의 나머지로 나눈다. <paramref name="trimRest"/>이면 나머지의 앞 공백을 지운다.</summary>
        private static (string Head, string Tail) SplitFirst(string value, bool trimRest = true)
        {
            int index = value.IndexOfAny([' ', '\t']);
            if (index < 0)
                return (value, "");
            string rest = value[(index + 1)..];
            return (value[..index], trimRest ? rest.TrimStart(' ', '\t') : rest);
        }
    }
}
