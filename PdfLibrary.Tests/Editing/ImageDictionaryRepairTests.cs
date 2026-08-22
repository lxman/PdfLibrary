using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Tests for <see cref="PdfDocumentEditor.PreviewImageDictionaryRepairs"/> — the read-only preview of
/// PDF/A clause 6.2.8 image-dictionary repairs (<c>PdfLibrary.Conformance.Rules.ImageDictionaryRule</c>).
/// Covers <c>/Interpolate</c>, <c>/OPI</c>, and <c>/Alternates</c> — including
/// <c>AlternatesSafeToRemove</c>'s two refusal routes (ISO 32000-2 8.9.5.4's /OC route and its
/// /DefaultForPrinting route) and its malformed-input handling.
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

    /// <summary>Builds a one-page document with TWO distinct indirect image XObjects (objects 10 and 12),
    /// both carrying <c>/Interpolate true</c>, both referenced from the page's /Resources /XObject
    /// dictionary (so both survive save's orphan removal) and both drawn from content. Used by the write
    /// tests (Task 2) to prove <see cref="PdfDocumentEditor.RepairImageDictionaries"/>'s object-number
    /// filter touches only the named object and leaves the other a candidate.</summary>
    private static byte[] DocWithTwoImagesBothInterpolating()
    {
        var doc = new PdfDocument();

        PdfDictionary ImageDict() => new()
        {
            [N("Type")] = N("XObject"),
            [N("Subtype")] = N("Image"),
            [N("Width")] = new PdfInteger(1),
            [N("Height")] = new PdfInteger(1),
            [N("ColorSpace")] = N("DeviceGray"),
            [N("BitsPerComponent")] = new PdfInteger(8),
            [N("Interpolate")] = PdfBoolean.True,
        };
        doc.AddObject(10, 0, new PdfStream(ImageDict(), [0x00]));
        doc.AddObject(12, 0, new PdfStream(ImageDict(), [0x00]));

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("q 1 0 0 1 0 0 cm /Im0 Do Q q 1 0 0 1 0 0 cm /Im1 Do Q")));

        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary
            {
                [N("XObject")] = new PdfDictionary { [N("Im0")] = Ref(10), [N("Im1")] = Ref(12) },
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

    // ── AlternatesSafeToRemove (ISO 32000-2 8.9.5.4 routes (a)-(c) /OC and (d) /DefaultForPrinting) ──

    [Fact]
    public void Preview_allows_removing_alternates_with_no_OC_and_no_default_for_printing()
    {
        byte[] pdf = DocWithImageKeys(d =>
            d[new PdfName("Alternates")] = new PdfArray(new PdfDictionary()));
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        ImageDictionaryRepairPreview preview = doc.Edit().PreviewImageDictionaryRepairs();

        ImageDictionaryRepairCandidate candidate = Assert.Single(preview.Candidates);
        Assert.Contains(ImageDictionaryRepairKind.RemoveAlternates, candidate.WouldApply);
        Assert.Empty(preview.Refused);
    }

    [Fact]
    public void Preview_refuses_alternates_with_a_default_for_printing_entry_even_without_OC()
    {
        byte[] pdf = DocWithImageKeys(d =>
            d[new PdfName("Alternates")] = new PdfArray(
                new PdfDictionary { [new PdfName("DefaultForPrinting")] = PdfBoolean.True }));
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        ImageDictionaryRepairPreview preview = doc.Edit().PreviewImageDictionaryRepairs();

        Assert.Empty(preview.Candidates);
        ImageDictionaryRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(ImageDictionaryRepairKind.RemoveAlternates, refusal.Kind);
        Assert.Contains("printing", refusal.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preview_refuses_alternates_when_the_image_carries_OC()
    {
        byte[] pdf = DocWithImageKeys(d =>
        {
            d[new PdfName("Alternates")] = new PdfArray(new PdfDictionary());
            d[new PdfName("OC")] = new PdfDictionary();
        });
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        ImageDictionaryRepairPreview preview = doc.Edit().PreviewImageDictionaryRepairs();

        Assert.Empty(preview.Candidates);
        ImageDictionaryRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(ImageDictionaryRepairKind.RemoveAlternates, refusal.Kind);
        Assert.Contains("optional content", refusal.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // A malformed /Alternates (present but not an array) must not throw: AlternatesSafeToRemove cannot
    // know what a non-array value means, so it degrades to the /OC-only check rather than guessing —
    // see that method's doc comment. With no /OC present here, that means it is still treated as safe.
    [Fact]
    public void Preview_does_not_throw_on_a_malformed_non_array_alternates()
    {
        byte[] pdf = DocWithImageKeys(d => d[new PdfName("Alternates")] = new PdfInteger(42));
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);

        ImageDictionaryRepairPreview preview = doc.Edit().PreviewImageDictionaryRepairs();

        Assert.Empty(preview.Refused);
        ImageDictionaryRepairCandidate candidate = Assert.Single(preview.Candidates);
        Assert.Contains(ImageDictionaryRepairKind.RemoveAlternates, candidate.WouldApply);
    }

    // ── RepairImageDictionaries (Task 2, 2026-08-21 image-dictionary remediation) — the write side, ──
    // ── object-filtered, sharing EnumerateImageXObjects/ClassifyImageDictionary with the preview above.

    /// <summary>Like <see cref="DocWithImageKeys"/>, but the image's /Alternates is an INDIRECT reference
    /// to the array (object 21), and the array's one entry is itself an INDIRECT reference to the
    /// alternate-image dictionary (object 20) — no /OC anywhere, so the repair should still apply. Pins
    /// the indirect-resolution path inside <c>AlternatesSafeToRemove</c>
    /// (<c>ResolveObject(imageDict.Get("Alternates"))</c> and <c>ResolveObject(alternates[i])</c>): Task
    /// 1's own tests only ever built /Alternates with direct values, so that path was correct but
    /// untested before this fixture (carry-forward from Task 1's review).</summary>
    private static byte[] DocWithIndirectAlternatesNoOc()
    {
        var doc = new PdfDocument();

        var altImageDict = new PdfDictionary
        {
            [N("Type")] = N("XObject"),
            [N("Subtype")] = N("Image"),
            [N("Width")] = new PdfInteger(1),
            [N("Height")] = new PdfInteger(1),
            [N("ColorSpace")] = N("DeviceGray"),
            [N("BitsPerComponent")] = new PdfInteger(8),
        };
        doc.AddObject(20, 0, new PdfStream(altImageDict, [0x00]));
        doc.AddObject(21, 0, new PdfArray(Ref(20)));

        var imageDict = new PdfDictionary
        {
            [N("Type")] = N("XObject"),
            [N("Subtype")] = N("Image"),
            [N("Width")] = new PdfInteger(1),
            [N("Height")] = new PdfInteger(1),
            [N("ColorSpace")] = N("DeviceGray"),
            [N("BitsPerComponent")] = new PdfInteger(8),
            [N("Alternates")] = Ref(21),
        };
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
    public void Repair_applies_only_to_the_named_objects()
    {
        byte[] pdf = DocWithTwoImagesBothInterpolating();
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        using PdfDocumentEditor editor = doc.Edit();

        int first = editor.PreviewImageDictionaryRepairs().Candidates[0].ObjectNumber;
        ImageDictionaryRepairReport report = editor.RepairImageDictionaries(new HashSet<int> { first });

        Assert.Equal(first, Assert.Single(report.Repaired).ObjectNumber);
        // the other image is untouched, so it is still a candidate
        ImageDictionaryRepairCandidate left = Assert.Single(editor.PreviewImageDictionaryRepairs().Candidates);
        Assert.NotEqual(first, left.ObjectNumber);
    }

    [Fact]
    public void Repair_with_null_filter_repairs_every_image()
    {
        byte[] pdf = DocWithTwoImagesBothInterpolating();
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        using PdfDocumentEditor editor = doc.Edit();

        Assert.Equal(2, editor.RepairImageDictionaries().Repaired.Count);
        Assert.Empty(editor.PreviewImageDictionaryRepairs().Candidates);
    }

    [Fact]
    public void Repair_refuses_Alternates_on_an_OC_guarded_image_but_still_fixes_its_Interpolate()
    {
        byte[] pdf = DocWithImageKeys(d =>
        {
            d[new PdfName("Alternates")] = new PdfArray();
            d[new PdfName("OC")] = new PdfDictionary();
            d[new PdfName("Interpolate")] = PdfBoolean.True;
        });
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        using PdfDocumentEditor editor = doc.Edit();

        ImageDictionaryRepairReport report = editor.RepairImageDictionaries();

        ImageDictionaryRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal(ImageDictionaryRepairKind.RemoveAlternates, refusal.Kind);
        Assert.Contains("optional content", refusal.Reason, StringComparison.OrdinalIgnoreCase);
        // the same image's Interpolate was still repaired — per-object partial repair, not per-object refusal
        Assert.Equal(ImageDictionaryRepairKind.NeutralizeInterpolate,
                     Assert.Single(Assert.Single(report.Repaired).Applied));
    }

    [Fact]
    public void Repair_removes_Alternates_when_the_image_has_no_OC()
    {
        byte[] pdf = DocWithIndirectAlternatesNoOc();
        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        using PdfDocumentEditor editor = doc.Edit();

        Assert.Equal(ImageDictionaryRepairKind.RemoveAlternates,
                     Assert.Single(Assert.Single(editor.RepairImageDictionaries().Repaired).Applied));
        Assert.Empty(editor.RepairImageDictionaries().Refused);
    }
}
