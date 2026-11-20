using Biz.Bizadm.PowerManager.FakeDevice.App.Console;

namespace Biz.Bizadm.PowerManager.FakeDevice.App.Tests;

[TestClass]
public sealed class ConsoleCommandTests
{
    [TestMethod]
    [DataRow("pub a/b {\"x\":1}", "a/b", "{\"x\":1}")]
    [DataRow("  PUB a/b hello world  ", "a/b", "hello world")]
    [DataRow("pub a/b  two spaces", "a/b", " two spaces")]
    [DataRow("pub\ta/b\tnot json", "a/b", "not json")]
    public void Pub_TopicAndRestOfLine(string line, string topic, string payload)
        => Assert.AreEqual(new ConsoleCommand(ConsoleCommandKind.Publish, topic, payload), ConsoleCommand.Parse(line));

    [TestMethod]
    [DataRow("up notify/json {\"sid\":\"1\"}", "notify/json", "{\"sid\":\"1\"}")]
    [DataRow("Up x y z", "x", "y z")]
    public void Up_SuffixAndRestOfLine(string line, string suffix, string payload)
        => Assert.AreEqual(new ConsoleCommand(ConsoleCommandKind.PublishSuffix, suffix, payload), ConsoleCommand.Parse(line));

    [TestMethod]
    [DataRow("fota http://fw.example/b540/", "Fota", "http://fw.example/b540/")]
    [DataRow("FOTA https://fw.example:8443/x", "Fota", "https://fw.example:8443/x")]
    [DataRow("get http://fw.example/image.bin", "Get", "http://fw.example/image.bin")]
    [DataRow(" GET  HTTPS://fw.example/a ", "Get", "HTTPS://fw.example/a")]
    public void Fota_Url(string line, string kind, string url)
        => Assert.AreEqual(new ConsoleCommand(Enum.Parse<ConsoleCommandKind>(kind), url), ConsoleCommand.Parse(line));

    [TestMethod]
    [DataRow("quit", "Quit")]
    [DataRow(" EXIT ", "Quit")]
    [DataRow("help", "Help")]
    [DataRow("Help", "Help")]
    public void Simple(string line, string kind)
        => Assert.AreEqual(Enum.Parse<ConsoleCommandKind>(kind), ConsoleCommand.Parse(line).Kind);

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("unknown x")]
    [DataRow("pub")]
    [DataRow("pub a/b")]
    [DataRow("up suffix")]
    [DataRow("fota")]
    [DataRow("fota fw.example/b540")]
    [DataRow("fota ftp://fw.example/b540")]
    [DataRow("get /relative/path")]
    [DataRow("get http://a/b extra")]
    [DataRow("quit now")]
    public void Unknown_OrMissingArguments_IsUsage(string line)
        => Assert.AreEqual(ConsoleCommandKind.Usage, ConsoleCommand.Parse(line).Kind);
}
