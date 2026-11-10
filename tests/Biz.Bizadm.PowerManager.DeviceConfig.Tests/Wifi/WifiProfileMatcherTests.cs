using Biz.Bizadm.PowerManager.DeviceConfig.HostWifi;
using Biz.Bizadm.PowerManager.DeviceConfig.Wifi;

namespace Biz.Bizadm.PowerManager.DeviceConfig.Tests.Wifi
{
    [TestClass]
    public sealed class WifiProfileMatcherTests
    {
        private static readonly WifiProfile Home = new("Home", "p1");
        private static readonly WifiProfile Office = new("Office", "p2");

        [TestMethod]
        public void Match_ExactSsid_OrderedByRssi()
        {
            IReadOnlyList<WifiProfileCandidate> candidates = WifiProfileMatcher.Match(
                [Home, Office],
                [new WifiNetwork("Office", -70), new WifiNetwork("home", -30), new WifiNetwork("Home", -50), new WifiNetwork("Other", -20)]);

            CollectionAssert.AreEqual(
                new[] { new WifiProfileCandidate(Home, -50), new WifiProfileCandidate(Office, -70) },
                candidates.ToArray());
        }

        [TestMethod]
        public void Match_DuplicateSsid_AllKept()
        {
            IReadOnlyList<WifiProfileCandidate> candidates = WifiProfileMatcher.Match(
                [Home],
                [new WifiNetwork("Home", -60), new WifiNetwork("Home", -40)]);

            CollectionAssert.AreEqual(new[] { -40, -60 }, candidates.Select(c => c.Signal).ToArray());
        }

        [TestMethod]
        public void Match_None_Empty()
        {
            Assert.AreEqual(0, WifiProfileMatcher.Match([Home], [new WifiNetwork("Other", -40)]).Count);
        }

        [TestMethod]
        public void WifiProfile_ToString_HidesPass()
        {
            WifiProfile profile = new("Home", "secret-pass");
            Assert.IsFalse(profile.ToString().Contains("secret-pass"));
            Assert.IsFalse(new WifiProfileCandidate(profile, -40).ToString().Contains("secret-pass"));
        }
    }
}
