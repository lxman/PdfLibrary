using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// PDF/A-2/3 clause 6.2.8.3 (<see cref="Jpeg2000Rule"/>): constraints on a JPXDecode image's embedded
/// JPEG2000 data, calibrated against veraPDF's PDFA-2 <c>JPEG2000</c> rules — colour-channel count ∈ {1,3,4}
/// (t1); if more than one colour-space specification, exactly one must carry APPROX 0x01 (t2); the colr
/// box METH ∈ {1,2,3} (t3); enumerated colour space 19 (CIEJab) is forbidden (t4); bit depth ∈ [1,38] and
/// uniform across channels — no bpcc box (t5). t2/t3/t4 are escaped when the image XObject carries an
/// explicit /ColorSpace (it overrides the JPEG2000 internal colour spec). Tests build minimal JP2 box
/// structures — the fields the rule reads — inside an image XObject.
/// </summary>
public class Jpeg2000RuleTests
{
    private static PdfName N(string s) => new(s);

    // ── JP2 box construction ────────────────────────────────────────────────────────────────────────
    private static byte[] Box(string type, byte[] payload)
    {
        var b = new List<byte>();
        int len = 8 + payload.Length;
        b.AddRange([(byte)(len >> 24), (byte)(len >> 16), (byte)(len >> 8), (byte)len]);
        b.AddRange(type.Select(c => (byte)c));
        b.AddRange(payload);
        return [.. b];
    }

    private static byte[] U16(int v) => [(byte)(v >> 8), (byte)v];
    private static byte[] U32(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static byte[] Ihdr(int nc, int bpc) =>
        Box("ihdr", [.. U32(64), .. U32(64), .. U16(nc), (byte)bpc, 7, 0, 0]); // H W NC BPC C UnkC IPR

    /// <summary>A colr box: METH 1 carries a 4-byte enumerated colour space; METH 2/3 carry an ICC blob.</summary>
    private static byte[] Colr(int meth, int approx, int enumCs = 16) =>
        meth == 1
            ? Box("colr", [(byte)meth, 0, (byte)approx, .. U32(enumCs)])
            : Box("colr", [(byte)meth, 0, (byte)approx, 1, 2, 3, 4]);

    /// <summary>Assembles a JP2: signature box then a jp2h super-box holding the given sub-boxes.</summary>
    private static byte[] Jp2(params byte[][] jp2hChildren)
    {
        byte[] signature = Box("jP  ", [0x0D, 0x0A, 0x87, 0x0A]);
        byte[] jp2h = Box("jp2h", jp2hChildren.SelectMany(x => x).ToArray());
        return [.. signature, .. jp2h];
    }

    // ── document assembly ───────────────────────────────────────────────────────────────────────────
    private static Finding[] Run(byte[] jp2Data, bool withColorSpace = false)
    {
        var dict = new PdfDictionary
        {
            [N("Type")] = N("XObject"),
            [N("Subtype")] = N("Image"),
            [N("Filter")] = N("JPXDecode"),
            [N("Width")] = new PdfInteger(64),
            [N("Height")] = new PdfInteger(64),
        };
        if (withColorSpace)
            dict[N("ColorSpace")] = N("DeviceRGB");

        var doc = new PdfDocument();
        doc.AddObject(5, 0, new PdfStream(dict, jp2Data));
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog") });
        doc.Trailer.Dictionary[N("Root")] = new PdfIndirectReference(1, 0);
        return [.. new Jpeg2000Rule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))];
    }

    private static void AssertFlagged(Finding[] findings)
    {
        Finding f = Assert.Single(findings);
        Assert.Equal("jpeg2000", f.RuleId);
        Assert.EndsWith("6.2.8.3", f.Clause);
        Assert.Equal(FindingSeverity.Error, f.Severity);
    }

    // BPC encodes (bit depth − 1); 7 → 8-bit unsigned.
    private const int Bpc8 = 7;

    [Fact]
    public void A_valid_jpeg2000_image_passes()
    {
        Assert.Empty(Run(Jp2(Ihdr(3, Bpc8), Colr(1, 0))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void One_three_or_four_channels_pass(int nc)
    {
        Assert.Empty(Run(Jp2(Ihdr(nc, Bpc8), Colr(1, 0))));
    }

    [Fact]
    public void Channel_count_other_than_1_3_4_is_flagged()
    {
        AssertFlagged(Run(Jp2(Ihdr(5, Bpc8), Colr(1, 0))));
    }

    [Fact]
    public void Colr_method_outside_1_2_3_is_flagged()
    {
        AssertFlagged(Run(Jp2(Ihdr(3, Bpc8), Colr(4, 0))));
    }

    [Fact]
    public void Enumerated_colour_space_19_ciejab_is_flagged()
    {
        AssertFlagged(Run(Jp2(Ihdr(3, Bpc8), Colr(1, 0, enumCs: 19))));
    }

    [Fact]
    public void Multiple_colr_specs_without_a_single_approx_1_are_flagged()
    {
        AssertFlagged(Run(Jp2(Ihdr(3, Bpc8), Colr(1, 0), Colr(1, 0))));
    }

    [Fact]
    public void Multiple_colr_specs_with_exactly_one_approx_1_pass()
    {
        Assert.Empty(Run(Jp2(Ihdr(3, Bpc8), Colr(1, 1), Colr(1, 0))));
    }

    [Fact]
    public void Bit_depth_above_38_is_flagged()
    {
        AssertFlagged(Run(Jp2(Ihdr(3, 40), Colr(1, 0)))); // BPC 40 → 41-bit
    }

    [Fact]
    public void A_bpcc_box_signalling_non_uniform_bit_depth_is_flagged()
    {
        byte[] bpcc = Box("bpcc", [Bpc8, Bpc8, Bpc8]);
        AssertFlagged(Run(Jp2(Ihdr(3, 0xFF), Colr(1, 0), bpcc)));
    }

    [Fact]
    public void An_explicit_image_colorspace_escapes_the_colr_checks()
    {
        // METH 4 would fail t3, but an explicit /ColorSpace overrides the JPEG2000 internal colour spec.
        Assert.Empty(Run(Jp2(Ihdr(3, Bpc8), Colr(4, 0)), withColorSpace: true));
    }

    [Fact]
    public void An_explicit_colorspace_does_not_escape_the_channel_count_check()
    {
        // t1 (channels) is about the image data, not colour, so /ColorSpace does not excuse it.
        AssertFlagged(Run(Jp2(Ihdr(5, Bpc8), Colr(1, 0)), withColorSpace: true));
    }

    [Fact]
    public void Method_is_read_from_the_approx_1_colr_box_not_the_first()
    {
        // colr0 has METH 5 (invalid) but APPROX 2; colr1 is the best-fidelity spec (APPROX 1, METH 1).
        // veraPDF drives METH from the APPROX-0x01 box, so this image is conformant.
        Assert.Empty(Run(Jp2(Ihdr(3, Bpc8), Colr(5, approx: 2), Colr(1, approx: 1))));
    }

    [Fact]
    public void Enum_cs_is_read_from_the_approx_1_colr_box_not_the_first()
    {
        // colr0 is CIEJab (EnumCS 19) but APPROX 2; the APPROX-0x01 box is sRGB (16) — conformant.
        Assert.Empty(Run(Jp2(Ihdr(3, Bpc8), Colr(1, approx: 2, enumCs: 19), Colr(1, approx: 1, enumCs: 16))));
    }

    [Fact]
    public void A_truncated_colr_box_aborts_the_box_walk_like_verapdf()
    {
        // A METH-1 colr box too short to hold its 4-byte EnumCS: veraPDF stops parsing there, counting one
        // spec, so t2 passes. The rule must not keep counting the following box and mis-fire t2.
        byte[] truncated = Box("colr", [1, 0, 1]); // METH 1, PREC 0, APPROX 1, no EnumCS
        Assert.Empty(Run(Jp2(Ihdr(3, Bpc8), truncated, Colr(1, approx: 1))));
    }

    [Fact]
    public void A_non_jp2_payload_is_skipped()
    {
        Assert.Empty(Run([0x00, 0x01, 0x02, 0x03, 0x04]));
    }

    [Fact]
    public void A_non_jpx_image_is_ignored()
    {
        var dict = new PdfDictionary
        {
            [N("Type")] = N("XObject"),
            [N("Subtype")] = N("Image"),
            [N("Filter")] = N("FlateDecode"),
        };
        var doc = new PdfDocument();
        doc.AddObject(5, 0, new PdfStream(dict, Jp2(Ihdr(5, Bpc8), Colr(1, 0))));
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog") });
        doc.Trailer.Dictionary[N("Root")] = new PdfIndirectReference(1, 0);
        Assert.Empty(new Jpeg2000Rule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)));
    }
}
