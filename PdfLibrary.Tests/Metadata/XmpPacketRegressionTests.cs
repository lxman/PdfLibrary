using System.Text;
using PdfLibrary.Metadata;
using Xunit;

namespace PdfLibrary.Tests.Metadata;

public class XmpPacketRegressionTests
{
    // FINDING 2: BOM must be canonical 3-byte UTF-8 sequence EF BB BF

    [Fact]
    public void Serialize_HeaderContainsCanonical3ByteUtf8Bom()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(XmpSchemas.Pdf, XmpSchemas.PdfPrefix, "Producer", "Test");
        byte[] bytes = pkt.Serialize();
        ReadOnlySpan<byte> header = bytes.AsSpan(0, Math.Min(200, bytes.Length));
        bool found = false;
        for (int i = 0; i <= header.Length - 3; i++)
        {
            if (header[i] == 0xEF && header[i + 1] == 0xBB && header[i + 2] == 0xBF)
            { found = true; break; }
        }
        Assert.True(found, "Header must contain canonical 3-byte UTF-8 BOM EF BB BF.");
        byte[] mojibake = [0xC3, 0xAF, 0xC2, 0xBB, 0xC2, 0xBF];
        bool mojibakeFound = false;
        for (int i = 0; i <= bytes.Length - 6; i++)
        {
            if (bytes[i] == mojibake[0] && bytes[i+1] == mojibake[1] &&
                bytes[i+2] == mojibake[2] && bytes[i+3] == mojibake[3] &&
                bytes[i+4] == mojibake[4] && bytes[i+5] == mojibake[5])
            { mojibakeFound = true; break; }
        }
        Assert.False(mojibakeFound, "Header must NOT contain 6-byte mojibake BOM.");
    }

    [Fact]
    public void Serialize_BomFix_RoundTripPreservesProperties()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(XmpSchemas.Xmp, XmpSchemas.XmpPrefix, "CreatorTool", "BomFixApp");
        pkt.SetLangAlt(XmpSchemas.Dc, XmpSchemas.DcPrefix, "title", "BOM Test");
        XmpPacket reparsed = XmpPacket.Parse(pkt.Serialize());
        Assert.Equal("BomFixApp", reparsed.Get(XmpSchemas.Xmp, "CreatorTool")?.Value);
        Assert.Equal("BOM Test", reparsed.Get(XmpSchemas.Dc, "title")?.LangAlt["x-default"]);
    }

    // FINDING 1: Prefix collision must not corrupt packet on round-trip

    [Fact]
    public void Serialize_TwoNamespacesSameDesiredPrefix_BothPropertiesRoundTrip()
    {
        const string ns1 = "http://example.com/ns1/";
        const string ns2 = "http://example.com/ns2/";
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(ns1, "ex", "prop1", "Value1");
        pkt.SetSimple(ns2, "ex", "prop2", "Value2");
        byte[] bytes = pkt.Serialize();
        string xml = Encoding.UTF8.GetString(bytes);
        int firstEx  = xml.IndexOf("xmlns:ex=", StringComparison.Ordinal);
        int secondEx = firstEx >= 0 ? xml.IndexOf("xmlns:ex=", firstEx + 1, StringComparison.Ordinal) : -1;
        Assert.Equal(-1, secondEx);
        XmpPacket reparsed = XmpPacket.Parse(bytes);
        Assert.Equal("Value1", reparsed.Get(ns1, "prop1")?.Value);
        Assert.Equal("Value2", reparsed.Get(ns2, "prop2")?.Value);
    }

    [Fact]
    public void Serialize_TwoNamespacesSameDesiredPrefix_OutputIsValidXml()
    {
        const string ns1 = "http://collide.example.com/a/";
        const string ns2 = "http://collide.example.com/b/";
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(ns1, "col", "thing1", "A");
        pkt.SetSimple(ns2, "col", "thing2", "B");
        XmpPacket reparsed = XmpPacket.Parse(pkt.Serialize());
        Assert.Equal(2, reparsed.Properties.Count(p => p.NamespaceUri == ns1 || p.NamespaceUri == ns2));
    }

    [Fact]
    public void SetSimple_FiveSameDesiredPrefix_AllFivePropsPreserved()
    {
        XmpPacket pkt = XmpPacket.CreateEmpty();
        const string prefix = "custom";
        for (int i = 1; i <= 5; i++)
        {
            string ns = $"http://collision-test.example.com/ns{i}/";
            pkt.SetSimple(ns, prefix, $"prop{i}", $"val{i}");
        }
        XmpPacket reparsed = XmpPacket.Parse(pkt.Serialize());
        for (int i = 1; i <= 5; i++)
        {
            string ns = $"http://collision-test.example.com/ns{i}/";
            Assert.Equal($"val{i}", reparsed.Get(ns, $"prop{i}")?.Value);
        }
    }

    // Regression: two unknown-namespace props with different source prefixes must both
    // survive Serialize->Parse even if internal fallback prefix assignment would collide.
    [Fact]
    public void Parse_TwoUnknownNamespaceDifferentXmlPrefixes_BothPreservedOnRoundTrip()
    {
        // Use XmpPacket.CreateEmpty() and SetSimple with two different namespaces and different
        // caller-supplied prefixes (aa and bb). This avoids needing to embed XML strings.
        const string nsA = "http://unknown-a.example.com/ns/";
        const string nsB = "http://unknown-b.example.com/ns/";
        XmpPacket pkt = XmpPacket.CreateEmpty();
        pkt.SetSimple(nsA, "aa", "propA", "Alpha");
        pkt.SetSimple(nsB, "bb", "propB", "Beta");

        Assert.Equal("Alpha", pkt.Get(nsA, "propA")?.Value);
        Assert.Equal("Beta",  pkt.Get(nsB, "propB")?.Value);

        XmpPacket reparsed = XmpPacket.Parse(pkt.Serialize());
        Assert.Equal("Alpha", reparsed.Get(nsA, "propA")?.Value);
        Assert.Equal("Beta",  reparsed.Get(nsB, "propB")?.Value);
    }
}
