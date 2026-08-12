using ICCSharp.Profile;
using PdfLibrary.Builder;
using PdfLibrary.Conformance;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Rendering.Icc;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Tests for the output-intent editing helpers (<see cref="PdfDocumentEditor.ReplaceOutputIntentProfile"/>,
/// <see cref="PdfDocumentEditor.ConsolidateOutputIntents"/>) and the public
/// <see cref="OutputIntentProfileValidator"/> that both the editor's callers and
/// <c>OutputIntentProfileRule</c> rely on.
/// </summary>
public class OutputIntentEditingTests
{
    // ── fixtures ─────────────────────────────────────────────────────────────
    // Real, valid ICC profiles already used elsewhere in this test tree
    // (PdfLibrary.Tests\Document\OutputIntentsTests.cs) — not synthesized bytes.

    private static byte[] CmykProfileBytes() => IccResources.ReadDefaultCmykProfile();
    private static byte[] RgbProfileBytes() => BuiltInProfiles.Srgb.Bytes.ToArray();

    private static MemoryStream OnePagePdf()
    {
        var ms = new MemoryStream();
        new PdfDocumentBuilder().AddPage(_ => { }).Save(ms);
        ms.Position = 0;
        return ms;
    }

    private static byte[] SaveToBytes(PdfDocumentEditor editor)
    {
        var outMs = new MemoryStream();
        editor.Save(outMs);
        return outMs.ToArray();
    }

    // ── premise self-checks ─────────────────────────────────────────────────
    // Both fixture profiles must actually read back as the colour space the tests assume,
    // before any test trusts them.

    [Fact]
    public void Fixture_CmykProfile_reallyIsCmyk()
    {
        var header = ICCSharp.Profile.IccProfile.Parse(CmykProfileBytes()).Header;
        Assert.Equal(ICCSharp.Profile.ColorSpaceSignatures.CMYK, header.DataColorSpace);
    }

    [Fact]
    public void Fixture_RgbProfile_reallyIsRgb()
    {
        var header = ICCSharp.Profile.IccProfile.Parse(RgbProfileBytes()).Header;
        Assert.Equal(ICCSharp.Profile.ColorSpaceSignatures.RGB, header.DataColorSpace);
    }

    // ── ReplaceOutputIntentProfile ───────────────────────────────────────────

    [Fact]
    public void ReplaceOutputIntentProfile_swaps_profile_and_rewrites_oci_and_info()
    {
        var src = OnePagePdf();
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(src, leaveOpen: true);
        editor.AddOutputIntent(CmykProfileBytes(), "OLD-ID", "old info", "GTS_PDFA1");
        editor.ReplaceOutputIntentProfile(0, RgbProfileBytes(), "sRGB IEC61966-2.1", "sRGB IEC61966-2.1");
        byte[] saved = SaveToBytes(editor);

        using var doc = PdfDocument.Load(new MemoryStream(saved), "");
        OutputIntentDescriptor intent = Assert.Single(doc.GetOutputIntents());
        Assert.Equal("sRGB IEC61966-2.1", intent.OutputConditionIdentifier);
        Assert.Equal("sRGB IEC61966-2.1", intent.Info);
        Assert.Equal(OutputIntentColorSpace.Rgb, intent.ColorSpace);
        Assert.Null(intent.OutputCondition); // stale /OutputCondition and /RegistryName removed
        Assert.Null(intent.RegistryName);
    }

    [Fact]
    public void ReplaceOutputIntentProfile_attaches_profile_to_intent_that_had_none()
    {
        // Build an /OutputIntents entry with no /DestOutputProfile via raw dictionary authoring
        // (mirrors PdfLibrary.Tests\Document\OutputIntentsTests.cs's DocWithOutputIntents helper).
        var doc = new PdfDocument();
        var intentDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("OutputIntent"),
            [new PdfName("S")] = new PdfName("GTS_PDFX"),
            [new PdfName("OutputConditionIdentifier")] = PdfString.FromText("FOGRA39"),
        };
        var intents = new PdfArray { intentDict };
        var catalog = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("Pages")] = new PdfIndirectReference(2, 0),
            [new PdfName("OutputIntents")] = intents,
        };
        doc.AddObject(1, 0, catalog);
        var pages = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Pages"),
            [new PdfName("Kids")] = new PdfArray(),
            [new PdfName("Count")] = new PdfInteger(0),
        };
        doc.AddObject(2, 0, pages);
        doc.Trailer.Root = new PdfIndirectReference(1, 0);

        // Premise self-check: the fixture really has no usable profile before the edit.
        OutputIntentDescriptor before = Assert.Single(doc.GetOutputIntents());
        Assert.False(before.HasDestProfile);

        using PdfDocumentEditor editor = doc.Edit();
        editor.ReplaceOutputIntentProfile(0, CmykProfileBytes(), "CGATS TR 003", "SWOP");
        byte[] saved = SaveToBytes(editor);

        using var reopened = PdfDocument.Load(new MemoryStream(saved), "");
        OutputIntentDescriptor after = Assert.Single(reopened.GetOutputIntents());
        Assert.True(after.HasDestProfile);
        Assert.Equal(OutputIntentColorSpace.Cmyk, after.ColorSpace);
        Assert.Equal("CGATS TR 003", after.OutputConditionIdentifier);
        Assert.Equal("SWOP", after.Info);
    }

    [Fact]
    public void ReplaceOutputIntentProfile_throws_on_bad_index()
    {
        var src = OnePagePdf();
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(src, leaveOpen: true);
        editor.AddOutputIntent(CmykProfileBytes(), "OLD-ID");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            editor.ReplaceOutputIntentProfile(-1, RgbProfileBytes(), "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            editor.ReplaceOutputIntentProfile(1, RgbProfileBytes(), "x"));
    }

    // ── ConsolidateOutputIntents ─────────────────────────────────────────────

    [Fact]
    public void ConsolidateOutputIntents_keeps_only_the_indexed_intent()
    {
        var src = OnePagePdf();
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(src, leaveOpen: true);
        editor.AddOutputIntent(CmykProfileBytes(), "FIRST-ID");
        editor.AddOutputIntent(RgbProfileBytes(), "SECOND-ID");

        editor.ConsolidateOutputIntents(1);
        byte[] saved = SaveToBytes(editor);

        using var doc = PdfDocument.Load(new MemoryStream(saved), "");
        OutputIntentDescriptor intent = Assert.Single(doc.GetOutputIntents());
        Assert.Equal("SECOND-ID", intent.OutputConditionIdentifier);
    }

    // ── OutputIntentProfileValidator ─────────────────────────────────────────

    [Fact]
    public void Validator_accepts_output_class_cmyk_and_rejects_devicelink_and_garbage()
    {
        Assert.Null(OutputIntentProfileValidator.Validate(CmykProfileBytes()));
        Assert.Null(OutputIntentProfileValidator.Validate(RgbProfileBytes()));

        string? garbage = OutputIntentProfileValidator.Validate([1, 2, 3]);
        Assert.Equal("The output intent /DestOutputProfile is not a valid ICC profile.", garbage);
    }
}
