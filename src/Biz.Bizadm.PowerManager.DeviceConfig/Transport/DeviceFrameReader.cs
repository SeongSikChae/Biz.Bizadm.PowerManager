using System.Text;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Transport
{
    /// <summary>
    /// 수신 버퍼에서 '#' 프레임(공장검사 응답) 또는 JSON(중괄호 짝) 하나를 꺼낸다.
    /// "[DUT->PC] BUTTON_…#" 이벤트 프레임은 버린다.
    /// 송수신은 UTF-8이고 Decoder로 읽기 경계에서 잘린 멀티바이트를 이어 붙인다.
    /// 알려진 한계: SSID에 '{'·'}'가 있으면 JSON 프레임이 깨진다(설계 5.1).
    /// </summary>
    internal sealed class DeviceFrameReader
    {
        private const string ButtonEventPrefix = "[DUT->PC] BUTTON_";

        private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
        private readonly StringBuilder buffer = new();

        public void Append(ReadOnlySpan<byte> data)
        {
            char[] chars = new char[Encoding.UTF8.GetMaxCharCount(data.Length)];
            int count = decoder.GetChars(data, chars, flush: false);
            buffer.Append(chars, 0, count);
        }

        public bool TryTake(out string frame)
        {
            while (true)
            {
                TrimLeadingWhitespace();
                string text = buffer.ToString();
                int end = text.StartsWith('{') ? FindJsonEnd(text) : text.IndexOf('#');
                if (end < 0)
                {
                    frame = string.Empty;
                    return false;
                }
                frame = text[..(end + 1)];
                buffer.Remove(0, end + 1);
                if (!frame.StartsWith(ButtonEventPrefix, StringComparison.Ordinal))
                    return true;
            }
        }

        private void TrimLeadingWhitespace()
        {
            int count = 0;
            while (count < buffer.Length && char.IsWhiteSpace(buffer[count]))
                count++;
            buffer.Remove(0, count);
        }

        private static int FindJsonEnd(string text)
        {
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '{')
                    depth++;
                else if (text[i] == '}' && --depth == 0)
                    return i;
            }
            return -1;
        }
    }
}
