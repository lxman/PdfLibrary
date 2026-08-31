using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

public sealed class SignatureByteRangeRuleTests
{
    [Fact]
    public void A_range_from_byte_zero_through_the_physical_end_passes()
    {
        using PdfDocument document = DocumentWithSignature(
            new PdfArray(I(0), I(20), I(40), I(60)));

        Assert.Empty(Findings(document, sourceLength: 100));
    }

    [Fact]
    public void Bytes_appended_after_the_signed_revision_are_reported()
    {
        using PdfDocument document = DocumentWithSignature(
            new PdfArray(I(0), I(20), I(40), I(60)));

        Finding finding = Assert.Single(Findings(document, sourceLength: 101));

        Assert.Equal("signature-byte-range", finding.RuleId);
        Assert.Equal("ISO 19005-2:2011, 6.4.3", finding.Clause);
        Assert.Equal(5, finding.ObjectNumber);
        Assert.Contains("physical end", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nonzero-start")]
    [InlineData("overlap")]
    [InlineData("past-eof")]
    [InlineData("odd-count")]
    [InlineData("non-integer")]
    public void Malformed_or_physically_impossible_ranges_are_left_to_a_signature_aware_validator(string kind)
    {
        PdfArray range = kind switch
        {
            "nonzero-start" => new PdfArray(I(1), I(19), I(40), I(60)),
            "overlap" => new PdfArray(I(0), I(20), I(10), I(90)),
            "past-eof" => new PdfArray(I(0), I(20), I(40), I(61)),
            "odd-count" => new PdfArray(I(0), I(20), I(40)),
            "non-integer" => new PdfArray(I(0), I(20), I(40), new PdfReal(60)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        using PdfDocument document = DocumentWithSignature(range);
        Assert.Empty(Findings(document, sourceLength: 100));
    }

    [Fact]
    public void A_field_reached_signature_without_a_Type_marker_is_still_checked()
    {
        using PdfDocument document = DocumentWithSignature(
            new PdfArray(I(0), I(20), I(40), I(60)), signatureHasType: false);

        Assert.Single(Findings(document, sourceLength: 101));
    }

    [Fact]
    public void In_memory_preflight_without_source_bytes_skips_the_byte_level_rule()
    {
        using PdfDocument document = DocumentWithSignature(
            new PdfArray(I(0), I(20), I(40), I(60)));
        var context = new ConformanceContext(document, ConformanceProfile.PdfA2b);

        Assert.Empty(new SignatureByteRangeRule().Check(context));
    }

    [Fact]
    public void Rule_targets_all_pdfa_profiles_only() =>
        Assert.Equal(ConformanceProfile.AllPdfA, new SignatureByteRangeRule().AppliesToProfiles);

    [Fact]
    [Trait("Category", "LocalOnly")]
    public void Transcript_source_covers_EOF_but_an_ordinary_full_save_leaves_the_signature_behind()
    {
        const string variable = "PDFLIBRARY_LOCAL708_CORPUS";
        const string defaultCorpus = @"D:\PdfCorpora\real-world\local-708";
        string root = Environment.GetEnvironmentVariable(variable) ?? defaultCorpus;
        string path = Path.Combine(root, "Transcript_MICHAELJORDAN.pdf");
        Assert.SkipWhen(!File.Exists(path), $"production signature witness not present at {path} (LocalOnly)");

        byte[] source = File.ReadAllBytes(path);
        Assert.DoesNotContain(Preflighter.Check(source, ConformanceProfile.PdfA2b).Findings,
            finding => finding.RuleId == "signature-byte-range");

        using PdfDocument document = PdfDocument.Load(new MemoryStream(source, writable: false));
        using PdfDocumentEditor editor = document.Edit();
        using var output = new MemoryStream();
        editor.Save(output);
        byte[] saved = output.ToArray();

        Assert.True(saved.Length > source.Length);
        Finding finding = Assert.Single(Preflighter.Check(saved, ConformanceProfile.PdfA2b).Findings,
            item => item.RuleId == "signature-byte-range");
        Assert.Equal("ISO 19005-2:2011, 6.4.3", finding.Clause);
    }

    private static Finding[] Findings(PdfDocument document, int sourceLength) =>
        new SignatureByteRangeRule().Check(
            new ConformanceContext(document, ConformanceProfile.PdfA2b, new byte[sourceLength])).ToArray();

    private static PdfDocument DocumentWithSignature(PdfArray range, bool signatureHasType = true)
    {
        var document = new PdfDocument();
        var signature = new PdfDictionary
        {
            [N("ByteRange")] = range,
            [N("Contents")] = new PdfString([]),
        };
        if (signatureHasType)
            signature[N("Type")] = N("Sig");

        document.AddObject(5, 0, signature);
        document.AddObject(4, 0, new PdfDictionary
        {
            [N("FT")] = N("Sig"),
            [N("V")] = R(5),
        });
        document.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = R(2),
            [N("MediaBox")] = new PdfArray(I(0), I(0), I(612), I(792)),
        });
        document.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(R(3)),
            [N("Count")] = I(1),
        });
        document.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Pages")] = R(2),
            [N("AcroForm")] = new PdfDictionary
            {
                [N("Fields")] = new PdfArray(R(4)),
            },
        });
        document.Trailer.Dictionary[N("Root")] = R(1);
        return document;
    }

    private static PdfName N(string value) => new(value);
    private static PdfInteger I(int value) => new(value);
    private static PdfIndirectReference R(int number) => new(number, 0);
}
