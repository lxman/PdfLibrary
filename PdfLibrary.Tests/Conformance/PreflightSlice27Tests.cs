using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 27 — subset CharSet/CIDSet completeness (<c>font-subset-coverage</c>), shared by PDF/A-2
/// (ISO 19005-2, 6.2.11.4.2) and PDF/UA-1 (ISO 14289-1, 7.21.4.2). These pin the rule's logic — the subset
/// guard, the descriptor-entry / embedding skips, and the flag/clean verdict — against REAL embedded
/// programs: a synthetic classic Type1 program (built here, glyphs {.notdef, a, b, c}) for the /CharSet
/// check, and the CC0 <c>PublicPixel.ttf</c> as a CIDFontType2 <c>/FontFile2</c> for the /CIDSet check.
/// Per-font-type detection breadth on real fonts (Type1C/CFF, CID-keyed CFF, Identity vs custom
/// CIDToGIDMap) is measured by the veraPDF parity + reference-file oracles; these lock the clause mapping,
/// message, and FP-safe skips.
/// </summary>
public class PreflightSlice27Tests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    // ── document scaffold (font reachable via the page /Resources) ──────────────────────────────────────
    private static PdfDocument DocWith(PdfDictionary font, params (int, PdfObject)[] extra)
    {
        var doc = new PdfDocument();
        doc.AddObject(1, 0, font);
        foreach ((int num, PdfObject obj) in extra)
            doc.AddObject(num, 0, obj);

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT ET")));
        doc.AddObject(22, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(21),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F0")] = Ref(1) },
            },
        });
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(22)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(20, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(21) });
        doc.Trailer.Dictionary[N("Root")] = Ref(20);
        return doc;
    }

    private static Finding[] Run(PdfDocument doc, ConformanceProfile profile = ConformanceProfile.PdfA2b) =>
        new FontSubsetCoverageRule().Check(new ConformanceContext(doc, profile)).ToArray();

    private static string? Clause(Finding f) => ParitySnapshot.ClauseKey(f.Clause);

    // ── synthetic classic Type1 program with a known glyph set {.notdef, a, b, c} ───────────────────────
    private static PdfStream Type1Program()
    {
        byte[] header = Encoding.ASCII.GetBytes(
            "%!PS-AdobeFont-1.0: Test 001.000\n" +
            "/FontMatrix [0.001 0 0 0.001 0 0] readonly def\n" +
            "currentfile eexec\n");

        var priv = new List<byte>();
        void A(string s) => priv.AddRange(Encoding.ASCII.GetBytes(s));
        A("dup /Private 3 dict dup begin\n/lenIV 4 def\n/CharStrings 4 dict dup begin\n");
        foreach (string g in new[] { ".notdef", "a", "b", "c" })
        {
            A($"/{g} 1 RD ");
            priv.Add(0x01); // one arbitrary charstring byte (never interpreted for name enumeration)
            A(" ND\n");
        }
        A("end\nend\n");

        // eexec plaintext is 4 discardable bytes + the private dict; encrypt it (key 55665).
        var plain = new List<byte> { 0, 0, 0, 0 };
        plain.AddRange(priv);
        byte[] eexec = Eexec(plain.ToArray());

        byte[] all = new byte[header.Length + eexec.Length];
        Buffer.BlockCopy(header, 0, all, 0, header.Length);
        Buffer.BlockCopy(eexec, 0, all, header.Length, eexec.Length);

        var dict = new PdfDictionary
        {
            [N("Length1")] = new PdfInteger(header.Length),
            [N("Length2")] = new PdfInteger(eexec.Length),
            [N("Length3")] = new PdfInteger(0),
        };
        return new PdfStream(dict, all);
    }

    private static byte[] Eexec(byte[] plain)
    {
        ushort r = 55665;
        const ushort c1 = 52845, c2 = 22719;
        var outp = new byte[plain.Length];
        for (int i = 0; i < plain.Length; i++)
        {
            var cipher = (byte)(plain[i] ^ (r >> 8));
            r = (ushort)((cipher + r) * c1 + c2);
            outp[i] = cipher;
        }
        return outp;
    }

    private static PdfDocument Type1Doc(string baseFont, string? charSet)
    {
        var descriptor = new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N(baseFont),
            [N("Flags")] = new PdfInteger(4),
            [N("FontFile")] = Ref(3),
        };
        if (charSet is not null)
            descriptor[N("CharSet")] = new PdfString(Encoding.Latin1.GetBytes(charSet));

        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N(baseFont),
            [N("FontDescriptor")] = Ref(2),
        };
        return DocWith(font, (2, descriptor), (3, Type1Program()));
    }

    // ── CIDFontType2 (PublicPixel) with a custom 4-entry CIDToGIDMap → program CIDs {1,2,3} ──────────────
    private static byte[] FontBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    private static PdfDocument CidDoc(byte[]? cidSetBytes, bool embed = true)
    {
        var descriptor = new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+PublicPixel"),
            [N("Flags")] = new PdfInteger(4),
        };
        if (embed)
            descriptor[N("FontFile2")] = Ref(3);
        if (cidSetBytes is not null)
            descriptor[N("CIDSet")] = Ref(5);

        // CIDToGIDMap stream mapping CIDs 0..3 → GIDs 0..3 (all < numGlyphs) ⇒ program CIDs {1,2,3}.
        byte[] map = { 0x00, 0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0x03 };

        var cidFont = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("ABCDEF+PublicPixel"),
            [N("CIDToGIDMap")] = Ref(6),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = new PdfString(Encoding.Latin1.GetBytes("Adobe")),
                [N("Ordering")] = new PdfString(Encoding.Latin1.GetBytes("Identity")),
                [N("Supplement")] = new PdfInteger(0),
            },
            [N("FontDescriptor")] = Ref(2),
        };
        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+PublicPixel"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
        };

        var extra = new List<(int, PdfObject)>
        {
            (2, descriptor),
            (3, new PdfStream(new PdfDictionary(), FontBytes())),
            (4, cidFont),
            (6, new PdfStream(new PdfDictionary(), map)),
        };
        if (cidSetBytes is not null)
            extra.Add((5, new PdfStream(new PdfDictionary(), cidSetBytes)));

        return DocWith(font, extra.ToArray());
    }

    // ── test 1 — simple Type1 /CharSet ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Subset_Type1_CharSet_missing_a_program_glyph_is_flagged()
    {
        // Program has {a, b, c}; /CharSet omits 'c'.
        Finding f = Assert.Single(Run(Type1Doc("ABCDEF+Test", "/a/b")));
        Assert.Equal("font-subset-coverage", f.RuleId);
        Assert.Equal("6.2.11.4.2", Clause(f));
        Assert.Contains("/CharSet", f.Message);
    }

    [Fact]
    public void Subset_Type1_CharSet_listing_all_glyphs_is_clean()
    {
        Assert.Empty(Run(Type1Doc("ABCDEF+Test", "/a/b/c")));
    }

    [Fact]
    public void NonSubset_Type1_with_incomplete_CharSet_is_not_flagged()
    {
        // Same incomplete /CharSet, but the BaseFont carries no subset prefix ⇒ the subset guard skips it.
        Assert.Empty(Run(Type1Doc("Test", "/a/b")));
    }

    [Fact]
    public void Type1_without_CharSet_is_not_flagged()
    {
        Assert.Empty(Run(Type1Doc("ABCDEF+Test", charSet: null)));
    }

    [Fact]
    public void Type1_CharSet_finding_is_profile_aware_under_pdfua1()
    {
        Finding f = Assert.Single(Run(Type1Doc("ABCDEF+Test", "/a/b"), ConformanceProfile.PdfUA1));
        Assert.Equal("7.21.4.2", Clause(f));
        Assert.Contains("ISO 14289-1", f.Clause);
    }

    // ── test 2 — CID /CIDSet ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Subset_Cid_CIDSet_missing_a_program_cid_is_flagged()
    {
        // Program CIDs {1,2,3}; /CIDSet lists only {1,2} (bit for CID 3 clear).
        Finding f = Assert.Single(Run(CidDoc(new byte[] { 0x60 })));
        Assert.Equal("font-subset-coverage", f.RuleId);
        Assert.Equal("6.2.11.4.2", Clause(f));
        Assert.Contains("/CIDSet", f.Message);
    }

    [Fact]
    public void Subset_Cid_CIDSet_identifying_all_cids_is_clean()
    {
        // /CIDSet lists {1,2,3} — exactly the program CIDs.
        Assert.Empty(Run(CidDoc(new byte[] { 0x70 })));
    }

    [Fact]
    public void NonEmbedded_Cid_with_CIDSet_is_not_flagged()
    {
        // Subset name + /CIDSet present, but no embedded program ⇒ FP-safe skip.
        Assert.Empty(Run(CidDoc(new byte[] { 0x60 }, embed: false)));
    }
}
