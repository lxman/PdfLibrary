using System.IO;
using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Conformance;
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
    /// <summary>The corpus fixture for a veraPDF test name, or null when the corpus is not on this
    /// machine. Routed through <see cref="CorpusHarness"/> (a sibling <c>../veraPDF-corpus</c> checkout,
    /// overridable with the <c>VERAPDF_CORPUS</c> environment variable) instead of the hardcoded
    /// user-profile path this class shipped with, and returning null instead of asserting the file
    /// exists: <c>tools/gate.sh</c> runs <c>PdfLibrary.Tests</c> under exactly
    /// <c>Category=LocalOnly</c>, so an <c>Assert.True(File.Exists(...))</c> here is a FAILURE, not a
    /// skip, and reddened the gate on the project's other two boxes for a reason unrelated to any
    /// commit (2026-08-21 whole-branch review, Important 2). Matches the discipline the app side of this
    /// same branch already had (<c>Pellucid.App.Tests.ImageDictionaryCorpusFixTests</c>: env var plus a
    /// skip) and mirrors the needle-matching lookup in
    /// <see cref="Conformance.ExplicitResourcesAndFontGlyphFixtureTests"/>.</summary>
    private static string? Fixture(string name)
    {
        if (!CorpusHarness.IsAvailable) return null;

        string needle = $"veraPDF test suite {name}.pdf";
        return CorpusHarness.AllPdfPaths(ConformanceProfile.PdfA2b)
                            .FirstOrDefault(p => Path.GetFileName(p) == needle);
    }

    private const string CorpusMissing =
        "veraPDF corpus not present at ../veraPDF-corpus (set VERAPDF_CORPUS)";

    [Fact]
    public void T01_fail_a_refuses_remove_alternates_due_to_defaultforprinting()
    {
        string? path = Fixture("6-2-8-1-t01-fail-a");
        Assert.SkipUnless(path is not null, CorpusMissing);

        using PdfDocument doc = PdfDocument.Load(path!);
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
        string? path = Fixture("6-2-8-1-t01-fail-b");
        Assert.SkipUnless(path is not null, CorpusMissing);

        using PdfDocument doc = PdfDocument.Load(path!);
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
        string? path = Fixture("6-2-8-1-t02-fail-a");
        Assert.SkipUnless(path is not null, CorpusMissing);

        using PdfDocument doc = PdfDocument.Load(path!);
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
        string? path = Fixture(fixture);
        Assert.SkipUnless(path is not null, CorpusMissing);

        using PdfDocument doc = PdfDocument.Load(path!);
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
        string? path = Fixture(fixture);
        Assert.SkipUnless(path is not null, CorpusMissing);

        using PdfDocument doc = PdfDocument.Load(path!);
        Assert.Empty(doc.Edit().PreviewImageDictionaryRepairs().Candidates);
    }

    // Note: t02-pass-b contains no image XObject at all, so Assert.Empty(...Candidates) passes
    // trivially and proves nothing. Fixture is veraPDF's own test file; we skip it to avoid a
    // vacuous test.
}
