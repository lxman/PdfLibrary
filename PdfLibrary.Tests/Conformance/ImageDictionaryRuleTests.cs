using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// PDF/A-2/3 clause 6.2.8 (<see cref="ImageDictionaryRule"/>): image XObject restrictions. Calibrated
/// against veraPDF's PDFA-2 rules — an image dictionary shall not contain <c>/Alternates</c> or
/// <c>/OPI</c>; if <c>/Interpolate</c> is present it shall be false; <c>/BitsPerComponent</c> shall be
/// 1, 2, 4, 8, or 16 (and exactly 1 for an image mask).
/// </summary>
public class ImageDictionaryRuleTests
{
    private static PdfName N(string s) => new(s);
    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfA2b);

    private static PdfDocument ImageWith(params (string key, PdfObject value)[] entries)
    {
        var dict = new PdfDictionary
        {
            [N("Type")] = N("XObject"),
            [N("Subtype")] = N("Image"),
        };
        foreach ((string key, PdfObject value) in entries)
            dict[N(key)] = value;

        var doc = new PdfDocument();
        doc.AddObject(5, 0, new PdfStream(dict, Encoding.ASCII.GetBytes(" ")));
        return doc;
    }

    private static Finding[] Findings(PdfDocument doc) => new ImageDictionaryRule().Check(Ctx(doc)).ToArray();

    [Fact]
    public void An_image_with_Alternates_is_flagged()
    {
        Finding f = Assert.Single(Findings(ImageWith(("Alternates", new PdfArray()))));
        Assert.Equal("image-dictionary", f.RuleId);
        Assert.Equal(ConformanceClauses.For(ConformanceProfile.PdfA2b, "6.2.8"), f.Clause);
        Assert.Contains("Alternates", f.Message);
    }

    [Fact]
    public void An_image_with_OPI_is_flagged()
    {
        Finding f = Assert.Single(Findings(ImageWith(("OPI", new PdfDictionary()))));
        Assert.Contains("OPI", f.Message);
    }

    [Fact]
    public void An_image_with_Interpolate_true_is_flagged()
    {
        Finding f = Assert.Single(Findings(ImageWith(("Interpolate", PdfBoolean.True))));
        Assert.Contains("Interpolate", f.Message);
    }

    [Fact]
    public void An_image_with_Interpolate_false_is_not_flagged()
    {
        Assert.Empty(Findings(ImageWith(("Interpolate", PdfBoolean.False))));
    }

    [Fact]
    public void A_valid_bits_per_component_is_not_flagged()
    {
        Assert.Empty(Findings(ImageWith(("BitsPerComponent", new PdfInteger(16)))));
    }

    [Fact]
    public void An_invalid_bits_per_component_is_flagged()
    {
        Finding f = Assert.Single(Findings(ImageWith(("BitsPerComponent", new PdfInteger(3)))));
        Assert.Contains("BitsPerComponent", f.Message);
    }

    [Fact]
    public void An_image_mask_with_bits_per_component_1_is_not_flagged()
    {
        Assert.Empty(Findings(ImageWith(("ImageMask", PdfBoolean.True), ("BitsPerComponent", new PdfInteger(1)))));
    }

    [Fact]
    public void An_image_mask_with_bits_per_component_not_1_is_flagged()
    {
        // BitsPerComponent 8 is valid for an ordinary image but not for an image mask, which must be 1.
        Finding f = Assert.Single(
            Findings(ImageWith(("ImageMask", PdfBoolean.True), ("BitsPerComponent", new PdfInteger(8)))));
        Assert.Contains("BitsPerComponent", f.Message);
    }

    [Fact]
    public void A_clean_image_is_not_flagged()
    {
        Assert.Empty(Findings(ImageWith(("BitsPerComponent", new PdfInteger(8)))));
    }

    [Fact]
    public void A_form_xobject_with_Alternates_is_not_flagged()
    {
        // 6.2.8 is scoped to image XObjects; a form XObject with /Alternates must not trip this rule.
        var dict = new PdfDictionary
        {
            [N("Type")] = N("XObject"),
            [N("Subtype")] = N("Form"),
            [N("Alternates")] = new PdfArray(),
        };
        var doc = new PdfDocument();
        doc.AddObject(5, 0, new PdfStream(dict, Encoding.ASCII.GetBytes(" ")));
        Assert.Empty(Findings(doc));
    }
}
