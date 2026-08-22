using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Tests for <see cref="PdfDocumentEditor.PreviewImageDictionaryRepairs"/> — the read-only preview of
/// PDF/A clause 6.2.8 image-dictionary repairs (<c>PdfLibrary.Conformance.Rules.ImageDictionaryRule</c>).
/// Only <c>/Interpolate</c> and <c>/OPI</c> are exercised here; <c>/Alternates</c>'s predicate
/// (<c>AlternatesSafeToRemove</c>) is written now but tested in Task 2, which adds the write side and
/// fixtures carrying <c>/Alternates</c>.
/// </summary>
public class ImageDictionaryRepairTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    /// <summary>Builds a one-page document whose single image XObject carries the given extra keys, and
    /// returns it serialized to bytes so callers exercise the real load/save path. Mirrors
    /// <c>PdfDocumentEditorFontsTests.BuildType0Document</c>'s convention (hand-built <see cref="PdfDocument"/>
    /// via <c>AddObject</c> at fixed numbers, wired into a reachable page/pages/catalog trailer) — the
    /// established fixture shape in this test project (no <c>TestFixtures.Path(...)</c> helper or vendored
    /// PDF exists for this). The image is referenced from the page's /Resources /XObject dictionary (not
    /// just registered) so it survives <see cref="PdfDocumentEditor.Save(System.IO.Stream, PdfSaveOptions?)"/>'s
    /// default orphan removal.</summary>
    private static byte[] DocWithImageKeys(Action<PdfDictionary> decorate)
    {
        var doc = new PdfDocument();

        var imageDict = new PdfDictionary
        {
            [N("Type")] = N("XObject"),
            [N("Subtype")] = N("Image"),
            [N("Width")] = new PdfInteger(1),
            [N("Height")] = new PdfInteger(1),
            [N("ColorSpace")] = N("DeviceGray"),
            [N("BitsPerComponent")] = new PdfInteger(8),
        };
        decorate(imageDict);
        doc.AddObject(10, 0, new PdfStream(imageDict, [0x00]));

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("q 1 0 0 1 0 0 cm /Im0 Do Q")));

        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary
            {
                [N("XObject")] = new PdfDictionary { [N("Im0")] = Ref(10) },
            },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);

        using var ms = new MemoryStream();
        doc.Edit().Save(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Preview_lists_interpolate_true_as_a_candidate()
    {
        byte[] pdf = DocWithImageKeys(d => d[new PdfName("Interpolate")] = PdfBoolean.True);
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        using PdfDocumentEditor editor = doc.Edit();
        ImageDictionaryRepairPreview preview = editor.PreviewImageDictionaryRepairs();

        ImageDictionaryRepairCandidate candidate = Assert.Single(preview.Candidates);
        Assert.Equal(ImageDictionaryRepairKind.NeutralizeInterpolate, Assert.Single(candidate.WouldApply));
        Assert.Empty(preview.Refused);
    }

    [Fact]
    public void Preview_ignores_interpolate_false_and_absent()
    {
        foreach (byte[] pdf in new[]
                 {
                     DocWithImageKeys(d => d[new PdfName("Interpolate")] = PdfBoolean.False),
                     DocWithImageKeys(_ => { }),
                 })
        {
            using var ms = new MemoryStream(pdf);
            using PdfDocument doc = PdfDocument.Load(ms);
            Assert.Empty(doc.Edit().PreviewImageDictionaryRepairs().Candidates);
        }
    }

    [Fact]
    public void Preview_writes_nothing_to_the_document()
    {
        byte[] pdf = DocWithImageKeys(d => d[new PdfName("Interpolate")] = PdfBoolean.True);
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        using PdfDocumentEditor editor = doc.Edit();

        editor.PreviewImageDictionaryRepairs();
        editor.PreviewImageDictionaryRepairs();   // twice: an idempotency guard must not have tripped

        using var after = new MemoryStream();
        editor.Save(after);
        using var reloaded = PdfDocument.Load(new MemoryStream(after.ToArray()));
        Assert.NotEmpty(reloaded.Edit().PreviewImageDictionaryRepairs().Candidates);
    }

    [Fact]
    public void Preview_reports_one_candidate_carrying_both_keys_once()
    {
        byte[] pdf = DocWithImageKeys(d =>
        {
            d[new PdfName("Interpolate")] = PdfBoolean.True;
            d[new PdfName("OPI")] = new PdfDictionary();
        });
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);

        ImageDictionaryRepairCandidate candidate =
            Assert.Single(doc.Edit().PreviewImageDictionaryRepairs().Candidates);
        Assert.Equal(2, candidate.WouldApply.Count);
    }
}
