using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using System.Text;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Transport
{
    [TestClass]
    public sealed class DeviceFrameReaderTests
    {
        private static void Append(DeviceFrameReader reader, string text) => reader.Append(Encoding.UTF8.GetBytes(text));

        [TestMethod]
        public void Fragmented_TestFrame()
        {
            DeviceFrameReader reader = new();
            Append(reader, "[DUT->PC] ON_CM");
            Assert.IsFalse(reader.TryTake(out _));
            Append(reader, "D_OK#");
            Assert.IsTrue(reader.TryTake(out string frame));
            Assert.AreEqual("[DUT->PC] ON_CMD_OK#", frame);
        }

        [TestMethod]
        public void Fragmented_Json()
        {
            DeviceFrameReader reader = new();
            Append(reader, "{\"SCAN_AP_LIST\":{\"A:-1\"");
            Assert.IsFalse(reader.TryTake(out _));
            Append(reader, "}}");
            Assert.IsTrue(reader.TryTake(out string frame));
            Assert.AreEqual("{\"SCAN_AP_LIST\":{\"A:-1\"}}", frame);
        }

        [TestMethod]
        public void TwoFramesInOneRead()
        {
            DeviceFrameReader reader = new();
            Append(reader, "{\"respone\":\"OK\"}\r\n[DUT->PC] OFF_CMD_OK#");
            Assert.IsTrue(reader.TryTake(out string first));
            Assert.IsTrue(reader.TryTake(out string second));
            Assert.AreEqual("{\"respone\":\"OK\"}", first);
            Assert.AreEqual("[DUT->PC] OFF_CMD_OK#", second);
            Assert.IsFalse(reader.TryTake(out _));
        }

        [TestMethod]
        public void ButtonEvent_IsSkipped()
        {
            DeviceFrameReader reader = new();
            Append(reader, "[DUT->PC] BUTTON_PRESS#[DUT->PC] START_OK:a1b2c3d4e5f6#");
            Assert.IsTrue(reader.TryTake(out string frame));
            Assert.AreEqual("[DUT->PC] START_OK:a1b2c3d4e5f6#", frame);
        }

        [TestMethod]
        public void Utf8MultibyteSplitAcrossReads()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{\"SCAN_AP_LIST\":{\"공유기:-50\"}}");
            int split = Encoding.UTF8.GetByteCount("{\"SCAN_AP_LIST\":{\"") + 1; // '공' 3바이트 중간
            DeviceFrameReader reader = new();
            reader.Append(bytes.AsSpan(0, split));
            Assert.IsFalse(reader.TryTake(out _));
            reader.Append(bytes.AsSpan(split));
            Assert.IsTrue(reader.TryTake(out string frame));
            Assert.AreEqual("{\"SCAN_AP_LIST\":{\"공유기:-50\"}}", frame);
        }
    }
}
