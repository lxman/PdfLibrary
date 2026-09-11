using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts.Remediation;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Issue 51: referenced-font discovery already walked annotation appearance streams, but the separate
/// character-code usage walk stopped at page content and Form XObjects. A font drawn only by an /AP
/// stream was therefore inventoried with empty UsedCodes and could never produce a font-program finding.
/// </summary>
public sealed class FontProgramRuleAnnotationAppearanceTests
{
    private static PdfName N(string value) => new(value);
    private static PdfIndirectReference Ref(int number) => new(number, 0);

    [Fact]
    public void Notdef_drawn_only_in_an_annotation_appearance_is_reported()
    {
        using PdfDocument document = WidthPatchFixtures.NotdefOnlyDoc();
        var page = Assert.IsType<PdfDictionary>(document.ResolveReference(Ref(22)));

        document.AddObject(12, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("q Q")));
        document.AddObject(13, 0, new PdfStream(
            new PdfDictionary
            {
                [N("Type")] = N("XObject"), [N("Subtype")] = N("Form"),
                [N("BBox")] = new PdfArray(
                    new PdfInteger(0), new PdfInteger(0), new PdfInteger(20), new PdfInteger(20)),
                [N("Resources")] = new PdfDictionary
                {
                    [N("Font")] = new PdfDictionary { [N("F0")] = Ref(1) },
                },
            },
            Encoding.ASCII.GetBytes("BT /F0 12 Tf <41> Tj ET")));
        document.AddObject(14, 0, new PdfDictionary
        {
            [N("Type")] = N("Annot"), [N("Subtype")] = N("Widget"),
            [N("Rect")] = new PdfArray(
                new PdfInteger(0), new PdfInteger(0), new PdfInteger(20), new PdfInteger(20)),
            [N("AP")] = new PdfDictionary { [N("N")] = Ref(13) },
        });
        page[N("Contents")] = Ref(12);
        page[N("Annots")] = new PdfArray(Ref(14));

        Finding finding = Assert.Single(
            new FontProgramRule().Check(new ConformanceContext(document, ConformanceProfile.PdfA2b)),
            f => ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.8");

        Assert.Equal(1, finding.ObjectNumber);
    }
}
