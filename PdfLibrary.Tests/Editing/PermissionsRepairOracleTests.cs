using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing;

[Trait("Category", "LocalOnly")]
public sealed class PermissionsRepairOracleTests
{
    private const string RelativeFolder = @"PDF_A-2b\6.1 File structure\6.1.12 Permissions";

    [Theory]
    [InlineData("veraPDF test suite 6-1-12-t01-fail-a.pdf")]
    [InlineData("veraPDF test suite 6-1-12-t02-fail-a.pdf")]
    [InlineData("veraPDF test suite 6-1-12-t02-fail-b.pdf")]
    [InlineData("veraPDF test suite 6-1-12-t02-fail-c.pdf")]
    public void VeraPdf_failure_fixture_becomes_an_unsigned_unencrypted_permissions_pass(string fileName)
    {
        string? path = Fixture(fileName);
        Assert.SkipWhen(path is null, "veraPDF corpus not present at ../veraPDF-corpus");

        using (PdfDocument source = PdfDocument.Load(path!))
        {
            Assert.False(source.IsEncrypted);
            Assert.Contains(Preflighter.Check(source, ConformanceProfile.PdfA2b).Findings,
                finding => finding.RuleId == "permissions");
        }

        using PdfDocumentEditor editor = PdfDocumentEditor.Open(path!);
        Assert.True(editor.PreviewPermissionsRepair().IsCandidate);
        Assert.True(editor.RepairPermissions().Repaired);
        using var output = new MemoryStream();
        editor.Save(output);

        byte[] saved = output.ToArray();
        using PdfDocument repaired = PdfDocument.Load(new MemoryStream(saved));
        Assert.False(repaired.IsEncrypted);
        Assert.DoesNotContain(Preflighter.Check(repaired, ConformanceProfile.PdfA2b, saved).Findings,
            finding => finding.RuleId == "permissions");
        Assert.False(new PdfDocumentEditor(repaired).PreviewPermissionsRepair().IsCandidate);
    }

    [Theory]
    [InlineData("veraPDF test suite 6-1-12-t01-pass-a.pdf")]
    [InlineData("veraPDF test suite 6-1-12-t02-pass-a.pdf")]
    public void VeraPdf_pass_fixture_is_not_a_repair_candidate(string fileName)
    {
        string? path = Fixture(fileName);
        Assert.SkipWhen(path is null, "veraPDF corpus not present at ../veraPDF-corpus");

        using PdfDocumentEditor editor = PdfDocumentEditor.Open(path!);
        Assert.False(editor.PreviewPermissionsRepair().IsCandidate);
        Assert.False(editor.RepairPermissions().Repaired);
    }

    private static string? Fixture(string fileName)
    {
        string? root = Environment.GetEnvironmentVariable("VERAPDF_CORPUS");
        if (!string.IsNullOrWhiteSpace(root))
        {
            string fromEnvironment = Path.Combine(root, RelativeFolder, fileName);
            if (File.Exists(fromEnvironment)) return fromEnvironment;
        }

        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "veraPDF-corpus", RelativeFolder, fileName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
