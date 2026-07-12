using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Parsing;

namespace PdfLibrary.Tests;

/// <summary>
/// "0 0 R" is a reference to object 0 — the head of the cross-reference free list, which is never a
/// real object — so it resolves to the null object (ISO 32000-1 §7.3.10). Some producers emit it
/// (e.g. Nlview/Concept Engineering schematics use it for absent connections). Before this fix the
/// parser fed object number 0 to the PdfIndirectReference constructor, which throws
/// ArgumentOutOfRangeException ("Object number must be positive"), so the whole document failed to load.
/// </summary>
public class ZeroObjectReferenceTests
{
    private static PdfParser CreateParser(string content)
        => new(new MemoryStream(Encoding.ASCII.GetBytes(content)));

    [Fact]
    public void ZeroObjectReference_ParsesAsNull_DoesNotThrow()
    {
        PdfParser parser = CreateParser("0 0 R");
        PdfObject? obj = parser.ReadObject();
        Assert.IsType<PdfNull>(obj);
    }

    [Fact]
    public void ZeroObjectReference_InsideArray_IsNull_RealReferencesUnaffected()
    {
        PdfParser parser = CreateParser("[1 0 R 0 0 R 3]");
        var array = (PdfArray)parser.ReadObject()!;

        Assert.IsType<PdfIndirectReference>(array[0]);   // a genuine reference still parses
        Assert.IsType<PdfNull>(array[1]);                // 0 0 R -> null
        Assert.IsType<PdfInteger>(array[2]);             // trailing integer unaffected
        Assert.Equal(3, array.Count);
    }
}
