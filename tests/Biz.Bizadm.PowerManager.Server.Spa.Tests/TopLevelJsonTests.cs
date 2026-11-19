using Biz.Bizadm.PowerManager.Server.Spa.Relay;
using System.Text;
using System.Text.Json;

namespace Biz.Bizadm.PowerManager.Server.Spa.Tests;

[TestClass]
public sealed class TopLevelJsonTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static string Value(string json, string name)
    {
        byte[] bytes = Utf8(json);
        Assert.IsTrue(TopLevelJson.TryFindValue(bytes, name, out Range range, out _));
        return Encoding.UTF8.GetString(bytes[range]);
    }

    [TestMethod]
    [DataRow("""{"a":"xy","b":1}""", "a", "\"xy\"", JsonTokenType.String)]
    [DataRow("""{"a":1, "b" : 12.5 }""", "b", "12.5", JsonTokenType.Number)]
    [DataRow("""{"b":null}""", "b", "null", JsonTokenType.Null)]
    [DataRow("""{"b":{"x":[1,{"y":2}]},"c":3}""", "b", """{"x":[1,{"y":2}]}""", JsonTokenType.StartObject)]
    [DataRow("""{"b":[1, 2 ,3]}""", "b", "[1, 2 ,3]", JsonTokenType.StartArray)]
    public void TryFindValue_ReturnsValueRange(string json, string name, string expected, JsonTokenType type)
    {
        byte[] bytes = Utf8(json);

        Assert.IsTrue(TopLevelJson.TryFindValue(bytes, name, out Range range, out JsonTokenType actualType));
        Assert.AreEqual(expected, Encoding.UTF8.GetString(bytes[range]));
        Assert.AreEqual(type, actualType);
    }

    // DataRow 인자는 직렬화를 거치며 \u 이스케이프가 풀리므로 이 경우는 따로 둔다.
    [TestMethod]
    public void TryFindValue_EscapedString_KeepsRawBytes()
        => Assert.AreEqual("\"x\\u002By\"", Value("{\"a\":\"x\\u002By\",\"b\":1}", "a"));

    [TestMethod]
    public void TryFindValue_IgnoresNestedSameName()
        => Assert.AreEqual("2", Value("""{"o":{"verify":"inner"},"verify":2}""", "verify"));

    [TestMethod]
    public void TryFindValue_ReturnsFirstOfDuplicates()
        => Assert.AreEqual("\"a\"", Value("""{"verify":"a","verify":"b"}""", "verify"));

    [TestMethod]
    [DataRow("""[{"verify":"a"}]""")]
    [DataRow("not json")]
    [DataRow("")]
    [DataRow("""{"other":1}""")]
    [DataRow("""{"verify":""")]
    public void TryFindValue_NotFound_ReturnsFalse(string json)
        => Assert.IsFalse(TopLevelJson.TryFindValue(Utf8(json), "verify", out _, out _));

    [TestMethod]
    public void Replace_KeepsOtherBytes()
    {
        byte[] bytes = Utf8("{ \"z\" : 1 ,\n \"verify\" : \"old\" , \"mqtt_key\":\"K\\u0041\" }");
        Assert.IsTrue(TopLevelJson.TryFindValue(bytes, "verify", out Range range, out _));

        byte[] result = TopLevelJson.Replace(bytes, range, TopLevelJson.RawStringToken("A+B/C="));

        Assert.AreEqual("{ \"z\" : 1 ,\n \"verify\" : \"A+B/C=\" , \"mqtt_key\":\"K\\u0041\" }", Encoding.UTF8.GetString(result));
    }

    [TestMethod]
    public void Mask_MasksTopLevelValuesOfAnyType()
    {
        byte[] result = TopLevelJson.Mask(Utf8("""{"verify":"v","x":{"mqtt_key":"inner"},"mqtt_key":{"a":1}}"""), "mqtt_key", "verify");

        Assert.AreEqual("""{"verify":"***","x":{"mqtt_key":"inner"},"mqtt_key":"***"}""", Encoding.UTF8.GetString(result));
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("""["mqtt_key"]""")]
    public void Mask_NotObject_ReturnsOriginal(string json)
        => Assert.AreEqual(json, Encoding.UTF8.GetString(TopLevelJson.Mask(Utf8(json), "mqtt_key")));

    [TestMethod]
    public void RawStringToken_RejectsEscapeNeeded()
        => Assert.Throws<ArgumentException>(() => TopLevelJson.RawStringToken("a\"b"));
}
