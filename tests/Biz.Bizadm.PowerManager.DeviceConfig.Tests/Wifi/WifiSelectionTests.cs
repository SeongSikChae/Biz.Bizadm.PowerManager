using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Wifi
{
    [TestClass]
    public sealed class WifiSelectionTests
    {
        [TestMethod]
        public void Parse_EntriesWithColonInSsid()
        {
            IReadOnlyList<ScannedAp> result = ApScanParser.Parse("{\"SCAN_AP_LIST\":{\"HomeAP:-40\",\"a:b:c:-70\",\"Office AP:-55\"}}");

            CollectionAssert.AreEqual(
                new[] { new ScannedAp("HomeAP", -40), new ScannedAp("a:b:c", -70), new ScannedAp("Office AP", -55) },
                result.ToArray());
        }

        /// <summary>
        /// 실장비 응답 형태(공백·대괄호·'+' 포함 SSID). SSID는 가상 값.
        /// </summary>
        [TestMethod]
        public void Parse_RealDeviceShape()
        {
            IReadOnlyList<ScannedAp> result = ApScanParser.Parse(
                "{\"SCAN_AP_LIST\":{\"Net-A_IOT:-57\",\"[Brand Device]d19f:-73\",\"U+Net1234:-78\",\"DIRECT-xx Series:-84\"}}");

            CollectionAssert.AreEqual(
                new[]
                {
                    new ScannedAp("Net-A_IOT", -57), new ScannedAp("[Brand Device]d19f", -73),
                    new ScannedAp("U+Net1234", -78), new ScannedAp("DIRECT-xx Series", -84),
                },
                result.ToArray());
        }

        [TestMethod]
        public void Parse_EmptyList()
        {
            Assert.IsEmpty(ApScanParser.Parse("{\"SCAN_AP_LIST\":{}}"));
        }

        [TestMethod]
        public void Parse_SkipsMalformedEntries()
        {
            IReadOnlyList<ScannedAp> result = ApScanParser.Parse("{\"SCAN_AP_LIST\":{\"NoRssi\",\":-40\",\"Bad:x\",\"Ok:-60\"}}");

            CollectionAssert.AreEqual(new[] { new ScannedAp("Ok", -60) }, result.ToArray());
        }

        [TestMethod]
        public void Parse_MissingListKey_Throws()
        {
            Assert.ThrowsExactly<DeviceProtocolException>(() => ApScanParser.Parse("{\"mac\":\"x\",\"respone\":\"OK\"}"));
        }

        [TestMethod]
        public void Match_FiltersDedupesAndSortsByRssi()
        {
            WifiProfile home = new("HomeAP", "p1");
            WifiProfile office = new("OfficeAP", "p2");
            WifiProfile cafe = new("CafeAP", "p3");
            ScannedAp[] scanned =
            [
                new("HomeAP", -70), new("OfficeAP", -50), new("Unknown", -30), new("HomeAP", -45), new("homeap", -20),
            ];

            IReadOnlyList<WifiCandidate> result = WifiProfileMatcher.Match(scanned, [home, office, cafe]);

            CollectionAssert.AreEqual(new[] { new WifiCandidate(home, -45), new WifiCandidate(office, -50) }, result.ToArray());
        }

        [TestMethod]
        public void Match_NoRegisteredAp_Empty()
        {
            Assert.IsEmpty(WifiProfileMatcher.Match([new ScannedAp("Other", -40)], [new WifiProfile("HomeAP", "p")]));
        }
    }
}
