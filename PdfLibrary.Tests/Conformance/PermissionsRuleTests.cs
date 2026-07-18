using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// PDF/A-2/3 clause 6.1.12 (<see cref="PermissionsRule"/>), calibrated against veraPDF's PDFA-2 rules:
/// (test 1) the permissions dictionary (catalog /Perms) shall contain no keys other than /UR3 and /DocMDP;
/// (test 2) if /Perms declares /DocMDP, no signature reference dictionary may contain /DigestLocation,
/// /DigestMethod, or /DigestValue.
/// </summary>
public class PermissionsRuleTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    private static PdfDocument Doc(PdfDictionary perms, params (int num, PdfObject obj)[] extra)
    {
        var doc = new PdfDocument();
        foreach ((int num, PdfObject obj) in extra)
            doc.AddObject(num, 0, obj);
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Perms")] = perms });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static Finding[] Findings(PdfDocument doc) =>
        [.. new PermissionsRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))];

    // ── test 1: permissions-dictionary keys ──────────────────────────────────

    [Fact]
    public void A_permissions_dict_with_a_disallowed_key_is_flagged()
    {
        Finding f = Assert.Single(Findings(Doc(new PdfDictionary { [N("Foo")] = new PdfDictionary() })));
        Assert.Equal("permissions", f.RuleId);
        Assert.Equal(ConformanceClauses.For(ConformanceProfile.PdfA2b, "6.1.12"), f.Clause);
        Assert.Contains("Foo", f.Message);
    }

    [Fact]
    public void A_permissions_dict_with_only_UR3_and_DocMDP_is_not_flagged()
    {
        var perms = new PdfDictionary { [N("UR3")] = new PdfDictionary(), [N("DocMDP")] = new PdfDictionary() };
        Assert.Empty(Findings(Doc(perms)));
    }

    // ── test 2: signature-reference Digest keys under DocMDP ──────────────────

    [Fact]
    public void A_sig_reference_with_a_digest_key_under_DocMDP_is_flagged()
    {
        var perms = new PdfDictionary { [N("DocMDP")] = Ref(10) };
        var sig = new PdfDictionary { [N("Type")] = N("Sig"), [N("Reference")] = new PdfArray(Ref(11)) };
        var sigRef = new PdfDictionary { [N("Type")] = N("SigRef"), [N("DigestMethod")] = N("MD5") };

        Finding f = Assert.Single(Findings(Doc(perms, (10, sig), (11, sigRef))));
        Assert.Contains("DigestMethod", f.Message);
    }

    [Fact]
    public void A_sig_reference_without_digest_keys_under_DocMDP_is_not_flagged()
    {
        var perms = new PdfDictionary { [N("DocMDP")] = Ref(10) };
        var sig = new PdfDictionary { [N("Reference")] = new PdfArray(Ref(11)) };
        var sigRef = new PdfDictionary { [N("Type")] = N("SigRef"), [N("TransformMethod")] = N("DocMDP") };

        Assert.Empty(Findings(Doc(perms, (10, sig), (11, sigRef))));
    }

    [Fact]
    public void A_digest_key_without_DocMDP_is_not_flagged()
    {
        // test 2 is gated on the permissions dictionary declaring DocMDP; without it, Digest keys are allowed.
        var perms = new PdfDictionary { [N("UR3")] = Ref(10) };
        var sig = new PdfDictionary { [N("Reference")] = new PdfArray(Ref(11)) };
        var sigRef = new PdfDictionary { [N("Type")] = N("SigRef"), [N("DigestMethod")] = N("MD5") };

        Assert.Empty(Findings(Doc(perms, (10, sig), (11, sigRef))));
    }

    [Fact]
    public void A_document_without_a_permissions_dictionary_is_not_flagged()
    {
        var doc = new PdfDocument();
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog") });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        Assert.Empty(new PermissionsRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));
    }
}
