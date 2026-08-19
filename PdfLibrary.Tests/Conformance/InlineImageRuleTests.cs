using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// PDF/A inline-image restrictions (<see cref="InlineImageRule"/>), calibrated against veraPDF 1.30.2's
/// PDFA-2 profile rather than against the clause text:
///
/// <list type="bullet">
/// <item>clause <b>6.1.10 test 1</b> (object <c>CosIIFilter</c>) is a WHITELIST, not an LZW blacklist —
/// "The value of the F key in the Inline Image dictionary shall not be LZW, LZWDecode, Crypt, a value not
/// listed in ISO 32000-1:2008, Table 6, or an array containing any such value". Permitted:
/// ASCIIHexDecode, ASCII85Decode, FlateDecode, RunLengthDecode, CCITTFaxDecode, DCTDecode and their
/// abbreviations AHx, A85, Fl, RL, CCF, DCT.</item>
/// <item>clause <b>6.2.8 test 3</b> (object <c>PDXImage</c>, test <c>Interpolate == false</c>) — "For an
/// inline image, the I key shall have a value of false". The XObject arm of this is already covered by
/// <c>ImageDictionaryRule</c>; only the inline arm was missing.</item>
/// </list>
///
/// Corpus fixtures behind these cases: <c>6-1-10-t01-fail-a</c> (<c>/F /LZW</c>), <c>-fail-b</c>
/// (<c>/F /LZWDecode</c>), <c>6-2-8-1-t02-fail-b</c> (<c>/I true /F /Fl</c>) and — the false-positive
/// guard — <c>6-2-8-1-t02-pass-b</c> (<c>/I false /F /Fl</c>).
/// </summary>
public class InlineImageRuleTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static byte[] Ops(string s) => Encoding.ASCII.GetBytes(s);

    private static Finding[] Findings(PdfDocument doc) =>
        new InlineImageRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)).ToArray();

    /// <summary>A one-page document whose /Contents is <paramref name="pageContent"/>; when
    /// <paramref name="formContent"/> is given a Form XObject (obj 10) carrying it is registered under /Fm0,
    /// reached only if the page actually issues <c>/Fm0 Do</c>.</summary>
    private static PdfDocument Doc(string pageContent, string? formContent = null)
    {
        var doc = new PdfDocument();
        var resources = new PdfDictionary();
        if (formContent is not null)
        {
            var formDict = new PdfDictionary
            {
                [N("Type")] = N("XObject"),
                [N("Subtype")] = N("Form"),
                [N("BBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(10), new PdfInteger(10)),
            };
            doc.AddObject(10, 0, new PdfStream(formDict, Ops(formContent)));
            resources[N("XObject")] = new PdfDictionary { [N("Fm0")] = Ref(10) };
        }

        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Ops(pageContent)));
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("Contents")] = Ref(4),
            [N("Resources")] = resources,
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private const string Head = "/W 1 /H 1 /CS /RGB /BPC 8";

    [Fact]
    public void An_inline_image_filtered_with_LZW_is_reported_under_6_1_10()
    {
        Finding[] findings = Findings(Doc($"q BI {Head} /F /LZW ID 12 EI Q"));

        Finding finding = Assert.Single(findings);
        Assert.Contains("6.1.10", finding.Clause);
        Assert.Contains("LZW", finding.Message);
    }

    [Fact]
    public void An_inline_image_filtered_with_the_full_LZWDecode_name_is_reported()
    {
        Finding[] findings = Findings(Doc($"q BI {Head} /F /LZWDecode ID 12 EI Q"));

        Assert.Contains("6.1.10", Assert.Single(findings).Clause);
    }

    [Fact]
    public void A_filter_array_containing_LZWDecode_is_reported()
    {
        // The rule text extends to "an array containing any such value".
        Finding[] findings = Findings(Doc($"q BI {Head} /F [/FlateDecode /LZWDecode] ID 12 EI Q"));

        Assert.Contains("6.1.10", Assert.Single(findings).Clause);
    }

    [Fact]
    public void The_abbreviated_flate_filter_is_permitted()
    {
        // Fl is FlateDecode's Table 6 abbreviation — flagging it would be a false positive, and every
        // corpus inline-image fixture uses exactly this spelling.
        Assert.Empty(Findings(Doc($"q BI {Head} /F /Fl ID 12 EI Q")));
    }

    [Fact]
    public void An_inline_image_with_interpolate_true_is_reported_under_6_2_8()
    {
        Finding[] findings = Findings(Doc($"q BI {Head} /I true /F /Fl ID 12 EI Q"));

        Assert.Contains("6.2.8", Assert.Single(findings).Clause);
    }

    [Fact]
    public void An_inline_image_with_interpolate_false_is_clean()
    {
        Assert.Empty(Findings(Doc($"q BI {Head} /I false /F /Fl ID 12 EI Q")));
    }

    [Fact]
    public void An_inline_image_reached_through_an_invoked_form_is_reported()
    {
        Finding[] findings = Findings(Doc("q /Fm0 Do Q", $"BI {Head} /F /LZW ID 12 EI"));

        Assert.Contains("6.1.10", Assert.Single(findings).Clause);
    }

    [Fact]
    public void An_inline_image_in_a_form_that_is_never_invoked_is_not_reported()
    {
        // Usage-sensitive, like the 6.2.2 walk: veraPDF only models content it reaches, and reporting
        // unreached content would break the corpus-wide zero-false-positive invariant.
        Assert.Empty(Findings(Doc("q Q", $"BI {Head} /F /LZW ID 12 EI")));
    }
}
