using System.IO;
using System.Linq;
using PdfLibrary.Builder;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 1 of the PDF/A + PDF/X preflight: the rule engine skeleton plus the first two
/// document-level rules (no encryption, trailer file identifier). Rule-level tests drive the
/// trailer state directly; the end-to-end tests run the whole <see cref="Preflighter"/> over
/// documents produced by the builder.
/// </summary>
public class PreflightSlice1Tests
{
    private static PdfString Str(params byte[] bytes) => new(bytes);

    private static PdfArray ValidFileId() => new(Str(0x01, 0x02, 0x03, 0x04), Str(0x01, 0x02, 0x03, 0x04));

    /// <summary>
    /// A minimal, well-formed document from the builder: has a /ID, no /Encrypt, and a valid PDF/A
    /// XMP /Metadata stream so it satisfies the metadata rules added in later slices.
    /// </summary>
    private static PdfDocument CleanBuilderDoc()
    {
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(new PdfSize(612, 792), p => p.AddText("hello", 100, 700))
            .ToByteArray();
        var doc = PdfDocument.Load(new MemoryStream(bytes));
        ConformanceXmp.AttachValidPdfaMetadata(doc);
        return doc;
    }

    // ── NoEncryptionRule ────────────────────────────────────────────────────

    [Fact]
    public void Encryption_rule_passes_when_no_encrypt_entry()
    {
        var doc = new PdfDocument();
        var ctx = new ConformanceContext(doc, ConformanceProfile.PdfA2b);

        Assert.Empty(new NoEncryptionRule().Check(ctx));
    }

    [Fact]
    public void Encryption_rule_flags_encrypt_entry()
    {
        var doc = new PdfDocument();
        doc.Trailer.Encrypt = new PdfIndirectReference(3, 0);
        var ctx = new ConformanceContext(doc, ConformanceProfile.PdfA2b);

        Finding finding = Assert.Single(new NoEncryptionRule().Check(ctx));
        Assert.Equal("encrypt", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    // ── FileIdentifierRule ──────────────────────────────────────────────────

    [Fact]
    public void FileId_rule_passes_with_two_string_id()
    {
        var doc = new PdfDocument();
        doc.Trailer.Id = ValidFileId();
        var ctx = new ConformanceContext(doc, ConformanceProfile.PdfA2b);

        Assert.Empty(new FileIdentifierRule().Check(ctx));
    }

    [Fact]
    public void FileId_rule_flags_missing_id()
    {
        var doc = new PdfDocument();
        var ctx = new ConformanceContext(doc, ConformanceProfile.PdfA2b);

        Finding finding = Assert.Single(new FileIdentifierRule().Check(ctx));
        Assert.Equal("file-id", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    [Fact]
    public void FileId_rule_flags_wrong_element_count()
    {
        var doc = new PdfDocument();
        doc.Trailer.Id = new PdfArray(Str(0x01, 0x02)); // only one element
        var ctx = new ConformanceContext(doc, ConformanceProfile.PdfA2b);

        Finding finding = Assert.Single(new FileIdentifierRule().Check(ctx));
        Assert.Equal("file-id", finding.RuleId);
    }

    [Fact]
    public void FileId_rule_flags_wrong_element_types()
    {
        var doc = new PdfDocument();
        doc.Trailer.Id = new PdfArray(new PdfInteger(1), new PdfInteger(2)); // right count, wrong types
        var ctx = new ConformanceContext(doc, ConformanceProfile.PdfA2b);

        Finding finding = Assert.Single(new FileIdentifierRule().Check(ctx));
        Assert.Equal("file-id", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    // ── Preflighter (end-to-end) ────────────────────────────────────────────

    [Fact]
    public void Clean_document_conforms()
    {
        using PdfDocument doc = CleanBuilderDoc();

        PreflightResult result = Preflighter.Check(doc, ConformanceProfile.PdfA2b);

        Assert.True(result.Conforms);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Encrypted_document_does_not_conform()
    {
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(new PdfSize(612, 792), p => p.AddText("secret", 100, 700))
            .WithPassword("pw")
            .ToByteArray();
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(bytes), "pw");

        PreflightResult result = Preflighter.Check(doc, ConformanceProfile.PdfA2b);

        Assert.False(result.Conforms);
        Assert.Contains(result.Findings, f => f is { RuleId: "encrypt", Severity: FindingSeverity.Error });
    }

    [Fact]
    public void Check_rejects_combined_profile()
    {
        using var doc = new PdfDocument();

        Assert.Throws<ArgumentException>(() => Preflighter.Check(doc, ConformanceProfile.AllPdfA));
    }

    [Theory]
    [InlineData(ConformanceProfile.PdfA2b)]
    [InlineData(ConformanceProfile.PdfA2u)]
    [InlineData(ConformanceProfile.PdfA3b)]
    [InlineData(ConformanceProfile.PdfX4)]
    public void Both_rules_apply_to_every_profile(ConformanceProfile profile)
    {
        // A doc that is encrypted and has no /ID violates both rules under every profile.
        var doc = new PdfDocument();
        doc.Trailer.Encrypt = new PdfIndirectReference(3, 0);

        PreflightResult result = Preflighter.Check(doc, profile);

        Assert.Contains(result.Findings, f => f.RuleId == "encrypt");
        Assert.Contains(result.Findings, f => f.RuleId == "file-id");
    }

    [Fact]
    public void Clause_reference_reflects_target_standard()
    {
        var doc = new PdfDocument();
        doc.Trailer.Encrypt = new PdfIndirectReference(3, 0);

        string aClause = Preflighter.Check(doc, ConformanceProfile.PdfA2b).Findings.First(f => f.RuleId == "encrypt").Clause;
        string xClause = Preflighter.Check(doc, ConformanceProfile.PdfX4).Findings.First(f => f.RuleId == "encrypt").Clause;

        Assert.Contains("19005-2", aClause);
        Assert.Contains("15930-7", xClause);
    }
}
