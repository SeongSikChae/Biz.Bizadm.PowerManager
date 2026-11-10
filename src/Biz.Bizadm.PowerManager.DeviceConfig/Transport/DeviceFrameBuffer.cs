using System.Text;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Transport
{
    /// <summary>
    /// 수신 바이트를 누적해 프레임 단위로 꺼낸다. 소켓과 분리해 테스트할 수 있게 한다.
    /// </summary>
    internal sealed class DeviceFrameBuffer
    {
        private const string EventPrefix = "[DUT->PC] BUTTON_";

        private readonly StringBuilder buffer = new();

        public void Append(ReadOnlySpan<byte> data) => buffer.Append(Encoding.ASCII.GetString(data));

        /// <summary>
        /// 아직 프레임으로 꺼내지 않은 내용.
        /// </summary>
        public string Peek() => buffer.ToString();

        /// <summary>
        /// 버퍼 앞의 비요청 버튼 이벤트(<c>[DUT-&gt;PC] BUTTON_...#</c>)를 하나 꺼낸다.
        /// </summary>
        public bool TryTakeEvent(out string message)
        {
            TrimLeadingWhitespace();
            string text = buffer.ToString();
            int end = text.StartsWith(EventPrefix, StringComparison.Ordinal) ? text.IndexOf('#') : -1;
            return TakeUntil(end, out message);
        }

        public bool TryTake(DeviceFrame frame, out string message)
        {
            TrimLeadingWhitespace();
            string text = buffer.ToString();
            int end = frame == DeviceFrame.Test ? text.IndexOf('#') : FindJsonEnd(text);
            return TakeUntil(end, out message);
        }

        private bool TakeUntil(int end, out string message)
        {
            if (end < 0)
            {
                message = string.Empty;
                return false;
            }
            message = buffer.ToString(0, end + 1).Trim();
            buffer.Remove(0, end + 1);
            return true;
        }

        private void TrimLeadingWhitespace()
        {
            int count = 0;
            while (count < buffer.Length && char.IsWhiteSpace(buffer[count]))
                count++;
            if (count > 0)
                buffer.Remove(0, count);
        }

        private static int FindJsonEnd(string text)
        {
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '{')
                    depth++;
                else if (c == '}' && depth > 0 && --depth == 0)
                    return i;
                else if (c == '\n' && depth == 0)
                    return i;
            }
            return -1;
        }
    }
}
