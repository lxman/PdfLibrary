using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Structure;

public class PdfDocumentSerializerTests
{
    [Fact]
    public void SerializeIndirectObject_Dictionary_WrapsInObjEndobj()
    {
        var dict = new PdfDictionary();
        dict[new PdfName("Type")] = new PdfName("Catalog");

        string text = Encoding.ASCII.GetString(
            PdfDocumentSerializer.SerializeIndirectObject(5, 0, dict));

        Assert.StartsWith("5 0 obj\n", text);
        Assert.Contains("/Type", text);
        Assert.Contains("/Catalog", text);
        Assert.EndsWith("endobj\n", text);
    }

    [Fact]
    public void SerializeIndirectObject_Stream_EmitsRealBytesNotPlaceholder()
    {
        var stream = new PdfStream(new PdfDictionary(), "hello stream"u8.ToArray());

        string text = Encoding.ASCII.GetString(
            PdfDocumentSerializer.SerializeIndirectObject(7, 0, stream));

        Assert.StartsWith("7 0 obj\n", text);
        Assert.Contains("stream\n", text);
        Assert.Contains("hello stream", text);              // real data...
        Assert.DoesNotContain("bytes of binary data", text); // ...not the ToPdfString placeholder
        Assert.Contains("endstream", text);
    }
}
