using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Tests for <see cref="PdfDocumentEditor.PreviewImageDictionaryRepairs"/> and
/// <see cref="PdfDocumentEditor.RepairImageDictionaries"/> against the veraPDF 6.2.8 image-dictionary
/// corpus fixtures. These are zero-population defects (/Alternates, /OPI) in the 708-document
/// corpus; oracle fixtures from veraPDF's test suite (6.2 Graphics, 6.2.8 Images, 6.2.8.1 General)
/// are the only realistic way to validate the repair logic.
///
/// Important: /Alternates fixtures may legitimately REFUSE rather than repair when the image
/// carries /OC or when any /Alternates entry carries /DefaultForPrinting true
/// (ISO 32000-2 §8.9.5.4). veraPDF fixtures were authored to trip veraPDF's rule, not ours,
/// so a t01 fixture may well carry either condition. When a fixture refuses, assert the refusal
/// and its reason instead of the repair.
/// </summary>
[Trait("Category", "LocalOnly")]
public class ImageDictionaryOracleFixtureTests
{
    private const string Root =
        @"C:\Users\jorda\RiderProjects\veraPDF-corpus\PDF_A-2b\6.2 Graphics\6.2.8 Images\6.2.8.1 General";

    private static string Fixture(string name)
    {
        string path = Path.Combine(Root, $"veraPDF test suite {name}.pdf");
        Assert.True(File.Exists(path), $"veraPDF corpus fixture missing: {path}");
        return path;
    }

    [Fact]
    public void T01_fail_a_refuses_remove_alternates_due_to_defaultforprinting()
    {
        using PdfDocument doc = PdfDocument.Load(Fixture("6-2-8-1-t01-fail-a"));
        using PdfDocumentEditor editor = doc.Edit();

        ImageDictionaryRepairPreview preview = editor.PreviewImageDictionaryRepairs();

        // This fixture carries one or more /Alternates entries with /DefaultForPrinting true.
        // ISO 32000-2 §8.9.5.4 (route d): printing would select a designated print master instead of
        // the base image, so deleting /Alternates is unsafe. The /OC route (a-c) is also exercised
        // by AlternatesSafeToRemove but has no corpus-fixture coverage; only synthetic tests
        // (ImageDictionaryRepairTests) exercise the /OC refusal.
        ImageDictionaryRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(ImageDictionaryRepairKind.RemoveAlternates, refusal.Kind);
        Assert.Contains("printing", refusal.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void T01_fail_b_removes_opi()
    {
        using PdfDocument doc = PdfDocument.Load(Fixture("6-2-8-1-t01-fail-b"));
        using PdfDocumentEditor editor = doc.Edit();

        ImageDictionaryRepairPreview preview = editor.PreviewImageDictionaryRepairs();

        Assert.Contains(ImageDictionaryRepairKind.RemoveOpi,
            Assert.Single(preview.Candidates).WouldApply);

        // Repair and verify the preview is now empty
        editor.RepairImageDictionaries();
        Assert.Empty(editor.PreviewImageDictionaryRepairs().Candidates);
    }

    [Fact]
    public void T02_fail_a_neutralizes_interpolate()
    {
        using PdfDocument doc = PdfDocument.Load(Fixture("6-2-8-1-t02-fail-a"));
        using PdfDocumentEditor editor = doc.Edit();

        ImageDictionaryRepairPreview preview = editor.PreviewImageDictionaryRepairs();

        Assert.Contains(ImageDictionaryRepairKind.NeutralizeInterpolate,
            Assert.Single(preview.Candidates).WouldApply);

        // Repair and verify the preview is now empty
        editor.RepairImageDictionaries();
        Assert.Empty(editor.PreviewImageDictionaryRepairs().Candidates);
    }

    [Theory]
    [InlineData("6-2-8-1-t03-fail-a")]
    public void Fail_fixtures_with_no_relevant_defects_offer_no_repair(string fixture)
    {
        using PdfDocument doc = PdfDocument.Load(Fixture(fixture));
        Assert.Empty(doc.Edit().PreviewImageDictionaryRepairs().Candidates);
    }

    // Note: t02-fail-b contains no image XObject at all, so Assert.Empty(...Candidates) passes
    // trivially against any implementation and proves nothing about AlternatesSafeToRemove. Fixture
    // is veraPDF's own test file and exists to test veraPDF's rule, not ours; we skip it to avoid
    // committing a vacuous test.

    [Theory]
    [InlineData("6-2-8-1-t02-pass-a")]
    [InlineData("6-2-8-1-t03-pass-a")]
    public void Pass_fixtures_offer_no_repair(string fixture)
    {
        using PdfDocument doc = PdfDocument.Load(Fixture(fixture));
        Assert.Empty(doc.Edit().PreviewImageDictionaryRepairs().Candidates);
    }

    // Note: t02-pass-b contains no image XObject at all, so Assert.Empty(...Candidates) passes
    // trivially and proves nothing. Fixture is veraPDF's own test file; we skip it to avoid a
    // vacuous test.
}
