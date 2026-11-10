using Biz.Bizadm.PowerManager.DeviceConfig.Transport;
using System.Text;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Transport
{
    [TestClass]
    public sealed class DeviceFrameBufferTests
    {
        private static DeviceFrameBuffer Buffer(params string[] chunks)
        {
            DeviceFrameBuffer buffer = new();
            foreach (string chunk in chunks)
                buffer.Append(Encoding.ASCII.GetBytes(chunk));
            return buffer;
        }

        [TestMethod]
        public void Test_FragmentedUntilHash()
        {
            DeviceFrameBuffer buffer = Buffer("[DUT->PC] STA");
            Assert.IsFalse(buffer.TryTake(DeviceFrame.Test, out _));

            buffer.Append(Encoding.ASCII.GetBytes("RT_OK:aabbccddeeff#"));
            Assert.IsTrue(buffer.TryTake(DeviceFrame.Test, out string message));
            Assert.AreEqual("[DUT->PC] START_OK:aabbccddeeff#", message);
            Assert.AreEqual(string.Empty, buffer.Peek());
        }

        [TestMethod]
        public void Test_TwoMergedFrames()
        {
            DeviceFrameBuffer buffer = Buffer("[DUT->PC] ON_CMD_OK#[DUT->PC] OFF_CMD_OK#");

            Assert.IsTrue(buffer.TryTake(DeviceFrame.Test, out string first));
            Assert.IsTrue(buffer.TryTake(DeviceFrame.Test, out string second));
            Assert.AreEqual("[DUT->PC] ON_CMD_OK#", first);
            Assert.AreEqual("[DUT->PC] OFF_CMD_OK#", second);
        }

        [TestMethod]
        public void Json_NestedBraces()
        {
            DeviceFrameBuffer buffer = Buffer("{\"SCAN_AP_LIST\":{\"a:-40\",\"b:-70\"}}{\"mac\":\"x\"}");

            Assert.IsTrue(buffer.TryTake(DeviceFrame.Json, out string first));
            Assert.AreEqual("{\"SCAN_AP_LIST\":{\"a:-40\",\"b:-70\"}}", first);
            Assert.IsTrue(buffer.TryTake(DeviceFrame.Json, out string second));
            Assert.AreEqual("{\"mac\":\"x\"}", second);
        }

        [TestMethod]
        public void Json_IncompleteWaits()
        {
            DeviceFrameBuffer buffer = Buffer("{\"SCAN_AP_LIST\":{\"a:-40\"");
            Assert.IsFalse(buffer.TryTake(DeviceFrame.Json, out _));
        }

        [TestMethod]
        public void Json_NonJsonLineEndsAtNewline()
        {
            DeviceFrameBuffer buffer = Buffer("OK\n");
            Assert.IsTrue(buffer.TryTake(DeviceFrame.Json, out string message));
            Assert.AreEqual("OK", message);
        }

        [TestMethod]
        public void LeadingNewline_RemovedBeforeEventAndFrame()
        {
            DeviceFrameBuffer buffer = Buffer("{\"respone\":\"OK\"}\r\n[DUT->PC] BUTTON_ON_EVT#\n[DUT->PC] MEASUREMENT_W:1.0#");

            Assert.IsTrue(buffer.TryTake(DeviceFrame.Json, out string json));
            Assert.AreEqual("{\"respone\":\"OK\"}", json);
            Assert.IsTrue(buffer.TryTakeEvent(out string evt));
            Assert.AreEqual("[DUT->PC] BUTTON_ON_EVT#", evt);
            Assert.IsFalse(buffer.TryTakeEvent(out _));
            Assert.IsTrue(buffer.TryTake(DeviceFrame.Test, out string test));
            Assert.AreEqual("[DUT->PC] MEASUREMENT_W:1.0#", test);
        }

        [TestMethod]
        public void Event_IncompleteIsNotTaken()
        {
            DeviceFrameBuffer buffer = Buffer("[DUT->PC] BUTTON_OFF");
            Assert.IsFalse(buffer.TryTakeEvent(out _));
            Assert.AreEqual("[DUT->PC] BUTTON_OFF", buffer.Peek());
        }
    }
}
