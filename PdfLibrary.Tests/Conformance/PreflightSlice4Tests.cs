using ICCSharp.Profile;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering.Icc;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 4 of the preflight: output intent structure rules. Covers <c>output-intent-profile</c>
/// (the /DestOutputProfile of each output intent must be a valid ICC profile with an acceptable
/// header) and <c>output-intent-single</c> (multiple output intents must share one destination
/// profile).
/// </summary>
public class PreflightSlice4Tests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// An in-memory document whose catalog has an /OutputIntents array with one intent dict per
    /// element of <paramref name="profiles"/>. A null element produces an intent with no
    /// /DestOutputProfile at all. Passing the same byte[] instance twice makes both intents share
    /// one indirect profile object; passing different instances gives each its own object.
    /// </summary>
    private static PdfDocument DocWithOutputIntents(params byte[]?[] profiles)
    {
        var doc = new PdfDocument();
        var intents = new PdfArray();
        var refsByProfile = new Dictionary<byte[], PdfIndirectReference>(ReferenceEqualityComparer.Instance);
        var nextObjectNumber = 2;

        foreach (byte[]? profileBytes in profiles)
        {
            var intentDict = new PdfDictionary { [new PdfName("S")] = new PdfName("GTS_PDFA1") };

            if (profileBytes is not null)
            {
                if (!refsByProfile.TryGetValue(profileBytes, out PdfIndirectReference? reference))
                {
                    int objectNumber = nextObjectNumber++;
                    doc.AddObject(objectNumber, 0, new PdfStream(new PdfDictionary(), profileBytes));
                    reference = new PdfIndirectReference(objectNumber, 0);
                    refsByProfile[profileBytes] = reference;
                }
                intentDict[new PdfName("DestOutputProfile")] = reference;
            }

            intents.Add(intentDict);
        }

        var catalog = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("OutputIntents")] = intents,
        };
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    /// <summary>An in-memory document with a catalog but no /OutputIntents key at all.</summary>
    private static PdfDocument DocWithoutOutputIntents()
    {
        var catalog = new PdfDictionary { [new PdfName("Type")] = new PdfName("Catalog") };
        var doc = new PdfDocument();
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfA2b);

    // ICC header field offsets (ISO 15076-1): version @8, device class @12, data colour space @16.
    private static byte[] WithHeaderSignature(byte[] valid, int offset, string fourChars)
    {
        var copy = (byte[])valid.Clone();
        System.Text.Encoding.ASCII.GetBytes(fourChars).CopyTo(copy, offset);
        return copy;
    }

    // ── output-intent-profile: passing cases ────────────────────────────────

    [Fact]
    public void ValidRgbProfile_passes()
    {
        PdfDocument doc = DocWithOutputIntents(BuiltInProfiles.Srgb.Bytes.ToArray());
        Assert.Empty(new OutputIntentProfileRule().Check(Ctx(doc)));
    }

    [Fact]
    public void ValidCmykProfile_passes()
    {
        PdfDocument doc = DocWithOutputIntents(IccResources.ReadDefaultCmykProfile());
        Assert.Empty(new OutputIntentProfileRule().Check(Ctx(doc)));
    }

    [Fact]
    public void NoOutputIntents_output_intent_profile_is_silent()
    {
        PdfDocument doc = DocWithoutOutputIntents();
        Assert.Empty(new OutputIntentProfileRule().Check(Ctx(doc)));
    }

    // ── output-intent-profile: failing cases ────────────────────────────────

    [Fact]
    public void GarbageProfileBytes_is_error()
    {
        PdfDocument doc = DocWithOutputIntents([1, 2, 3]);
        Finding finding = Assert.Single(new OutputIntentProfileRule().Check(Ctx(doc)));

        Assert.Equal("output-intent-profile", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    [Fact]
    public void ValidProfileWithWrongDeviceClass_is_header_error()
    {
        // Parses fine, but device class 'scnr' (Input) is neither Output nor Display.
        byte[] patched = WithHeaderSignature(BuiltInProfiles.Srgb.Bytes.ToArray(), 12, "scnr");
        Finding finding = Assert.Single(new OutputIntentProfileRule().Check(Ctx(DocWithOutputIntents(patched))));

        Assert.Equal("output-intent-profile", finding.RuleId);
        Assert.Contains("header", finding.Message);
    }

    [Fact]
    public void ValidProfileWithDisallowedColourSpace_is_header_error()
    {
        byte[] patched = WithHeaderSignature(BuiltInProfiles.Srgb.Bytes.ToArray(), 16, "Lab ");
        Finding finding = Assert.Single(new OutputIntentProfileRule().Check(Ctx(DocWithOutputIntents(patched))));

        Assert.Contains("header", finding.Message);
    }

    [Fact]
    public void IccVersion5Profile_is_header_error()
    {
        // Patch the version major byte (offset 8) to 5 (ICC v5 / iccMAX is not permitted).
        byte[] patched = (byte[])BuiltInProfiles.Srgb.Bytes.ToArray().Clone();
        patched[8] = 0x05;
        Finding finding = Assert.Single(new OutputIntentProfileRule().Check(Ctx(DocWithOutputIntents(patched))));

        Assert.Contains("header", finding.Message);
    }

    [Fact]
    public void ValidGrayProfile_passes()
    {
        // A Gray data colour space is permitted; the header rule only inspects the space signature.
        byte[] gray = WithHeaderSignature(BuiltInProfiles.Srgb.Bytes.ToArray(), 16, "GRAY");
        Assert.Empty(new OutputIntentProfileRule().Check(Ctx(DocWithOutputIntents(gray))));
    }

    // ── output-intent-single: passing cases ─────────────────────────────────

    [Fact]
    public void NoOutputIntents_output_intent_single_is_silent()
    {
        PdfDocument doc = DocWithoutOutputIntents();
        Assert.Empty(new OutputIntentSingleProfileRule().Check(Ctx(doc)));
    }

    [Fact]
    public void SingleOutputIntent_passes()
    {
        PdfDocument doc = DocWithOutputIntents(BuiltInProfiles.Srgb.Bytes.ToArray());
        Assert.Empty(new OutputIntentSingleProfileRule().Check(Ctx(doc)));
    }

    [Fact]
    public void TwoIntentsSharingOneIndirectProfile_passes()
    {
        byte[] shared = BuiltInProfiles.Srgb.Bytes.ToArray();
        PdfDocument doc = DocWithOutputIntents(shared, shared);
        Assert.Empty(new OutputIntentSingleProfileRule().Check(Ctx(doc)));
    }

    // ── output-intent-single: failing cases ─────────────────────────────────

    [Fact]
    public void TwoIntentsWithDifferentProfiles_is_error()
    {
        PdfDocument doc = DocWithOutputIntents(
            BuiltInProfiles.Srgb.Bytes.ToArray(),
            IccResources.ReadDefaultCmykProfile());
        Finding finding = Assert.Single(new OutputIntentSingleProfileRule().Check(Ctx(doc)));

        Assert.Equal("output-intent-single", finding.RuleId);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
    }

    [Fact]
    public void TwoIntentsWithDirectProfiles_is_error()
    {
        // Two intents whose DestOutputProfile is a direct (non-indirect) stream cannot share an
        // indirect key — exercises the sentinel path (defensive; direct streams don't occur in
        // parsed PDFs, where streams are always indirect).
        var doc = new PdfDocument();
        var intents = new PdfArray();
        foreach (byte[] bytes in new[] { BuiltInProfiles.Srgb.Bytes.ToArray(), IccResources.ReadDefaultCmykProfile() })
        {
            intents.Add(new PdfDictionary
            {
                [new PdfName("S")] = new PdfName("GTS_PDFA1"),
                [new PdfName("DestOutputProfile")] = new PdfStream(new PdfDictionary(), bytes),
            });
        }
        doc.AddObject(1, 0, new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("OutputIntents")] = intents,
        });
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);

        Assert.Single(new OutputIntentSingleProfileRule().Check(Ctx(doc)));
    }

    [Fact]
    public void IntentWithoutProfileMixedWithOne_single_rule_silent()
    {
        // Only one intent actually carries a profile, so there is nothing to disagree with.
        PdfDocument doc = DocWithOutputIntents(null, BuiltInProfiles.Srgb.Bytes.ToArray());
        Assert.Empty(new OutputIntentSingleProfileRule().Check(Ctx(doc)));
    }
}
