using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// PDF/A-2/3 clause 6.1.8 (<see cref="NameUtf8Rule"/>): after expansion of any <c>#XX</c> escapes, a name
/// object's byte sequence shall be valid UTF-8. Calibrated against veraPDF's PDFA-2 rule (object
/// <c>CosName</c>, test 1: <c>isValidUtf8 == true</c>), which applies to every name in the document.
/// </summary>
public class NameUtf8RuleTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    // PdfName stores one byte per char, so these char sequences ARE the expanded name bytes.
    // Built from explicit char codes so the values don't depend on the source-file encoding.
    private static readonly string InvalidUtf8Name = new([(char)0xC3, (char)0x28]); // C3 is a lead byte, 28 not a continuation
    private static readonly string ValidUtf8Name = new([(char)0xC3, (char)0xA9]);   // C3 A9 = U+00E9, valid two-byte UTF-8

    private static PdfDocument DocWith(string key, PdfObject value)
    {
        var doc = new PdfDocument();
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N(key)] = value });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static Finding[] Findings(PdfDocument doc) =>
        [.. new NameUtf8Rule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))];

    [Fact]
    public void A_name_value_that_is_not_valid_utf8_is_flagged()
    {
        Finding f = Assert.Single(Findings(DocWith("Bad", N(InvalidUtf8Name))));
        Assert.Equal("name-utf8", f.RuleId);
        Assert.Equal(ConformanceClauses.For(ConformanceProfile.PdfA2b, "6.1.8"), f.Clause);
    }

    [Fact]
    public void A_name_that_is_valid_utf8_is_not_flagged()
    {
        Assert.Empty(Findings(DocWith("Ok", N(ValidUtf8Name))));
    }

    [Fact]
    public void Ascii_names_are_not_flagged()
    {
        Assert.Empty(Findings(DocWith("Foo", N("Bar"))));
    }

    [Fact]
    public void An_invalid_name_used_as_a_dictionary_key_is_flagged()
    {
        var inner = new PdfDictionary { [N(InvalidUtf8Name)] = N("x") };
        Assert.Single(Findings(DocWith("Sub", inner)));
    }

    [Fact]
    public void An_invalid_name_nested_in_an_array_is_flagged()
    {
        Assert.Single(Findings(DocWith("Arr", new PdfArray(N(InvalidUtf8Name)))));
    }
}
