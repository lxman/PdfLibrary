using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CffTestFixtures;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts;
using PdfLibrary.Tests.Fonts.Embedded;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>
/// F-4b Task 5/6 shared fixture: the dead-CID Type0 documents (CIDFontType2 and CIDFontType0
/// descendants) used by both <see cref="ReplaceProgramProposalTests"/> (Task 5, planner) and
/// <c>ReplaceProgramApplyTests</c> (Task 6, editor write + close-by-construction gate) so the two
/// suites cannot silently diverge on the document shape the gate relies on — the same reason
/// <see cref="WidthPatchFixtures"/> exists for F-4a.
/// </summary>
internal static class ReplaceProgramFixtures
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    public static FontRemediationPlanner Planner(ISystemFontProvider? provider = null) =>
        new(provider ?? new StubFontProvider(null));

    public static byte[] LiberationSansBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "Liberation", "LiberationSans-Regular.ttf"));

    /// <summary>Same bfchar-block builder as <c>CidReplacementMapTests.BfChar</c>, but returning raw
    /// stream bytes (rather than a parsed <see cref="ToUnicodeCMap"/>) so a fixture can attach it as a
    /// <c>/ToUnicode</c> stream object.</summary>
    public static byte[] BfCharBytes(IReadOnlyList<(int Code, string Hex)> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/CIDInit /ProcSet findresource begin");
        sb.AppendLine("12 dict begin");
        sb.AppendLine("begincmap");
        sb.AppendLine($"{entries.Count} beginbfchar");
        foreach ((int code, string hex) in entries)
            sb.AppendLine($"<{code:X4}> <{hex}>");
        sb.AppendLine("endbfchar");
        sb.AppendLine("endcmap");
        sb.AppendLine("CMapName currentdict /CMap defineresource pop");
        sb.AppendLine("end");
        sb.AppendLine("end");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static void AddCidSystemInfo(PdfDictionary descendant)
    {
        descendant[N("CIDSystemInfo")] = new PdfDictionary
        {
            [N("Registry")] = new PdfString(Encoding.ASCII.GetBytes("Adobe")),
            [N("Ordering")] = new PdfString(Encoding.ASCII.GetBytes("Identity")),
            [N("Supplement")] = new PdfInteger(0),
        };
    }

    /// <summary>Explicit CIDToGIDMap stream bytes: big-endian ushort per CID, index = CID.</summary>
    private static byte[] CidToGidBytes(params ushort[] gidByCid)
    {
        var b = new byte[gidByCid.Length * 2];
        for (var i = 0; i < gidByCid.Length; i++)
        {
            b[i * 2] = (byte)(gidByCid[i] >> 8);
            b[i * 2 + 1] = (byte)gidByCid[i];
        }
        return b;
    }

    /// <summary>An explicit /CIDToGIDMap covering codes 0..<paramref name="deadCode"/> with gid 1 at
    /// <paramref name="liveCode"/> and gid 0 (.notdef) everywhere else, including
    /// <paramref name="deadCode"/> itself — computed rather than hand-listed so the map's length always
    /// matches its highest code.</summary>
    private static byte[] LiveDeadCidToGidBytes(int liveCode, int deadCode)
    {
        var gidByCid = new ushort[Math.Max(liveCode, deadCode) + 1];
        gidByCid[liveCode] = 1;
        return CidToGidBytes(gidByCid);
    }

    /// <summary>
    /// The shared dead-CID Type0/CIDFontType2 fixture (spec brief): program =
    /// <see cref="ZeroAdvanceSfntFixture.FontBytes"/> (2 glyphs), descendant /CIDToGIDMap an EXPLICIT
    /// stream (gid 1 at CID 0x41, gid 0 — dead — at CID 0x42), /DW 1000, /W [65 [500]]; wrapper
    /// /Encoding /Identity-H, /BaseFont /ABCDEF+DeadFace. Content shows <paramref name="contentHex"/>
    /// (default <c>0042 0041</c>: CID 0x42 → .notdef → the 6.2.11.8 finding; CID 0x41 → a
    /// live-by-the-rule glyph). /ToUnicode carries <paramref name="toUnicodeEntries"/> (default CID
    /// 0x42 → 'B', CID 0x41 → 'A') unless <paramref name="includeToUnicode"/> is false.
    ///
    /// <para>Issue 40 migration: the dead code used to be CID 0 itself (<c>contentHex: "0000 0041"</c>,
    /// <c>/CIDToGIDMap /Identity</c>) — retired because a USED CID 0 is now honestly declined by the
    /// planner (it can never be fixed by any replacement, per ISO 32000 §9.7.4.2), which would flip
    /// every ordinary replace test in this suite to a decline. <paramref name="cidToGid"/> overrides
    /// the default live/dead map — used by <c>FontProgramRuleCidZeroTests</c> to construct the
    /// pathological case an explicit map can produce that /Identity cannot: CID 0 mapped to a REAL,
    /// non-zero glyph. See <see cref="DeadCid2DocDrawingCidZero"/> for the retired CID-0-as-dead-code
    /// shape, kept only for the planner's cid0-only honesty tests.</para>
    ///
    /// <para><paramref name="baseFont"/>, <paramref name="flags"/> and <paramref name="italicAngle"/>
    /// exist for the issue-43 style-lie shape: a DECLARATION claiming a style (",Italic" name suffix,
    /// descriptor italic flag 0x40, non-zero /ItalicAngle) that the embedded program — whose
    /// <c>head.macStyle</c> is always regular in this fixture — does not carry.</para>
    /// </summary>
    public static PdfDocument DeadCid2Doc(
        IReadOnlyList<(int Code, string Hex)>? toUnicodeEntries = null,
        bool includeToUnicode = true,
        string contentHex = "0042 0041",
        string baseFont = "ABCDEF+DeadFace",
        int flags = 4, // symbolic
        int? italicAngle = null,
        ushort macStyle = 0,
        ushort[]? cidToGid = null)
    {
        IReadOnlyList<(int Code, string Hex)> entries = toUnicodeEntries ?? [(0x0042, "0042"), (0x0041, "0041")];

        byte[] font = ZeroAdvanceSfntFixture.FontBytes(gid1Advance: 450, macStyle: macStyle);
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        var descriptorDict = new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N(baseFont),
            [N("Flags")] = new PdfInteger(flags),
            [N("FontFile2")] = Ref(3),
        };
        if (italicAngle is { } angle)
            descriptorDict[N("ItalicAngle")] = new PdfInteger(angle);
        doc.AddObject(2, 0, descriptorDict);

        byte[] cidToGidBytes = cidToGid is null
            ? LiveDeadCidToGidBytes(liveCode: 0x41, deadCode: 0x42)
            : CidToGidBytes(cidToGid);
        doc.AddObject(6, 0, new PdfStream(new PdfDictionary(), cidToGidBytes));

        var descendant = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N(baseFont),
            [N("FontDescriptor")] = Ref(2),
            [N("CIDToGIDMap")] = Ref(6),
            [N("DW")] = new PdfInteger(1000),
            [N("W")] = new PdfArray(new PdfInteger(0x41), new PdfArray(new PdfInteger(500))),
        };
        AddCidSystemInfo(descendant);
        doc.AddObject(4, 0, descendant);

        var type0Dict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N(baseFont),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
        };
        if (includeToUnicode)
        {
            doc.AddObject(5, 0, new PdfStream(new PdfDictionary(), BfCharBytes(entries)));
            type0Dict[N("ToUnicode")] = Ref(5);
        }
        doc.AddObject(1, 0, type0Dict);

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes($"BT /F0 12 Tf <{contentHex}> Tj ET")));
        WidthPatchFixtures.AddSinglePageCatalog(doc, font: 1);
        return doc;
    }

    /// <summary>
    /// Issue 40 honesty tests ONLY: reproduces the RETIRED <see cref="DeadCid2Doc"/> default shape —
    /// <c>/CIDToGIDMap /Identity</c>, CID 0 as the dead code. <paramref name="onlyCidZeroDead"/> true
    /// (default) draws <c>0000 0041</c>: CID 0 is dead (identity maps it to GID 0, the ONLY code that
    /// can be under a bare Identity map), and CID 0x41 identity-maps to GID 0x41 — nonzero, so the
    /// OLD dead-code predicate (<c>gid == 0</c>) already called it "live" (a pre-existing quirk: GID
    /// 0x41 does not really exist in this 2-glyph program, but nothing here checks bounds) — so CID 0
    /// is the font's ONLY actually-dead code and the planner must decline rather than propose a
    /// replacement that can never close the finding. <paramref name="onlyCidZeroDead"/> false swaps in
    /// an explicit map (<see cref="DeadCid2Doc"/>'s own live-0x41/dead-0x42 shape) and draws CID 0
    /// alongside it (<c>0000 0042 0041</c>), so CID 0x42 is ALSO genuinely dead — the mixed case this
    /// task's honesty gate leaves proposing (Task 3 adds the not-fully-closing flag).
    /// </summary>
    public static PdfDocument DeadCid2DocDrawingCidZero(bool onlyCidZeroDead = true)
    {
        const string baseFont = "ABCDEF+DeadFace";
        string contentHex = onlyCidZeroDead ? "0000 0041" : "0000 0042 0041";
        IReadOnlyList<(int Code, string Hex)> entries = onlyCidZeroDead
            ? [(0x0000, "0041"), (0x0041, "0042")]
            : [(0x0000, "0041"), (0x0042, "0042"), (0x0041, "0043")];

        byte[] font = ZeroAdvanceSfntFixture.FontBytes(gid1Advance: 450);
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N(baseFont),
            [N("Flags")] = new PdfInteger(4), // symbolic
            [N("FontFile2")] = Ref(3),
        });

        var descendant = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N(baseFont),
            [N("FontDescriptor")] = Ref(2),
            [N("DW")] = new PdfInteger(1000),
            [N("W")] = new PdfArray(new PdfInteger(0x41), new PdfArray(new PdfInteger(500))),
        };
        if (onlyCidZeroDead)
        {
            descendant[N("CIDToGIDMap")] = N("Identity");
        }
        else
        {
            doc.AddObject(6, 0, new PdfStream(new PdfDictionary(),
                LiveDeadCidToGidBytes(liveCode: 0x41, deadCode: 0x42)));
            descendant[N("CIDToGIDMap")] = Ref(6);
        }
        AddCidSystemInfo(descendant);
        doc.AddObject(4, 0, descendant);

        var type0Dict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N(baseFont),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
        };
        doc.AddObject(5, 0, new PdfStream(new PdfDictionary(), BfCharBytes(entries)));
        type0Dict[N("ToUnicode")] = Ref(5);
        doc.AddObject(1, 0, type0Dict);

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes($"BT /F0 12 Tf <{contentHex}> Tj ET")));
        WidthPatchFixtures.AddSinglePageCatalog(doc, font: 1);
        return doc;
    }

    /// <summary>A dead-CID Type0/CIDFontType0 (CID-keyed CFF descendant) fixture, with a LIVE CID
    /// drawn alongside the dead one (as the CID2 fixture does), so <c>RestoredCodeCount</c> can
    /// discriminate 1 restored from 2. The dead code is CID 0x42 — absent from
    /// <c>customCharsetSids</c>, so <see cref="EmbeddedFontMetrics.GetGlyphIdByCid"/> falls through
    /// its charset walk and returns 0 (.notdef) for it — built the same way
    /// <c>ResolveGlyphIdCid2OttoTests.DivergentCharsetCff</c> does (a non-CID
    /// <see cref="MinimalCff.Build"/> whose charset entries <see cref="EmbeddedFontMetrics.GetGlyphIdByCid"/>
    /// reads as CIDs regardless of the CFF's own CID-ness): gid 1 ↔ CID 0x41 (present, live);
    /// CID 0x42 has no charset entry (dead). /ToUnicode maps CID 0x42 → 'B', CID 0x41 → 'A'.
    ///
    /// <para>Issue 40 migration: the dead code used to be CID 0 itself (a redundant example — CID 0
    /// is ALWAYS .notdef per <see cref="EmbeddedFontMetrics.GetGlyphIdByCid"/>'s own hardcoded rule,
    /// regardless of the charset — so it never actually exercised this fixture's "absent from the
    /// charset" mechanism). Retired for the same reason as <see cref="DeadCid2Doc"/>'s migration: a
    /// used CID 0 now honestly declines rather than proposing.</para>
    /// </summary>
    public static PdfDocument DeadCid0Doc()
    {
        byte[] font = MinimalCff.Build(charsetOperand: null, numGlyphs: 2, customCharsetSids: [0x41]);
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+DeadCid0"),
            [N("Flags")] = new PdfInteger(4), // symbolic
            [N("FontFile3")] = Ref(3),
        });
        var descendant = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType0"),
            [N("BaseFont")] = N("ABCDEF+DeadCid0"),
            [N("FontDescriptor")] = Ref(2),
            [N("DW")] = new PdfInteger(1000),
            [N("W")] = new PdfArray(new PdfInteger(0x41), new PdfArray(new PdfInteger(500))),
        };
        AddCidSystemInfo(descendant);
        doc.AddObject(4, 0, descendant);

        var type0Dict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+DeadCid0"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
        };
        doc.AddObject(5, 0, new PdfStream(new PdfDictionary(),
            BfCharBytes([(0x0042, "0042"), (0x0041, "0041")])));
        type0Dict[N("ToUnicode")] = Ref(5);
        doc.AddObject(1, 0, type0Dict);

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 12 Tf <0042 0041> Tj ET")));
        WidthPatchFixtures.AddSinglePageCatalog(doc, font: 1);
        return doc;
    }

    /// <summary>
    /// Per-holder merge program, Task 1: two Type0 wrappers (objects 1 and 7) SHARING ONE descendant
    /// CIDFont (object 4) whose descriptor (object 2) carries the program (object 3) — the
    /// <c>ProgramHolderId != Id</c> composite fixture that reaches the controller's issue-38 guard
    /// through <c>Propose</c>. Unlike the F-4b-era <c>TwoWrappersSharedHolderDoc</c> it promotes, BOTH
    /// wrappers draw: wrapper 1 shows <c>&lt;0041 0042&gt;</c>, wrapper 2 shows <c>&lt;0042&gt;</c> — a
    /// merge needs both wrappers to contribute used codes, or the union degenerates and the merge
    /// tests prove nothing.
    ///
    /// <para>The descendant's <c>/CIDToGIDMap</c> is an explicit stream (never <c>/Identity</c>, never
    /// dead-at-CID-0): 0x41 maps to gid 1 (live), 0x42 maps to gid 0 (dead) — the fixture rule later
    /// merge tasks' dead-code assertions rely on. Wrapper 1's <c>/ToUnicode</c> maps 0x41→'A', 0x42→'B';
    /// wrapper 2's defaults to the SAME 0x42→'B' mapping (consistent), overridable via
    /// <paramref name="wrapper2ToUnicode"/> to a conflicting mapping (e.g. 0x42→'Z') for the conflict
    /// tests a later task adds.</para>
    ///
    /// <para>Task 4 additions, following this fixture's own idiom (an optional parameter per
    /// variant rather than a second fixture method): <paramref name="wrapper2HasToUnicode"/> false
    /// omits wrapper 2's <c>/ToUnicode</c> entry entirely (object 8 is not written, and the wrapper
    /// dictionary carries no <c>/ToUnicode</c> key) — the merge-blocked-sibling gate case, distinct
    /// from an EMPTY-but-present CMap. <paramref name="wrapper2Codes"/> overrides which codes wrapper
    /// 2's content stream draws (default <c>[0x42]</c>, matching the original shape) — used by the
    /// ClosesFinding amendment test to have wrapper 2 draw CID 0 alongside its own dead code.</para>
    /// </summary>
    public static PdfDocument SharedDescendantDoc(
        IReadOnlyList<(int Code, string Hex)>? wrapper2ToUnicode = null,
        bool wrapper2HasToUnicode = true,
        IReadOnlyList<int>? wrapper2Codes = null)
    {
        byte[] font = ZeroAdvanceSfntFixture.FontBytes(gid1Advance: 450);
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+SharedDescendant"),
            [N("Flags")] = new PdfInteger(4), // symbolic
            [N("FontFile2")] = Ref(3),
        });

        // Codes 0..0x42: gid 1 at the live code 0x41, gid 0 (.notdef) at the dead code 0x42. Never
        // /Identity, never CID 0 — the fixture rule later merge tasks' dead-code assertions rely on.
        doc.AddObject(6, 0, new PdfStream(new PdfDictionary(),
            LiveDeadCidToGidBytes(liveCode: 0x41, deadCode: 0x42)));

        var descendant = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("ABCDEF+SharedDescendant"),
            [N("FontDescriptor")] = Ref(2),
            [N("CIDToGIDMap")] = Ref(6),
            [N("DW")] = new PdfInteger(1000),
            [N("W")] = new PdfArray(new PdfInteger(0x41), new PdfArray(new PdfInteger(500))),
        };
        AddCidSystemInfo(descendant);
        doc.AddObject(4, 0, descendant);

        doc.AddObject(5, 0, new PdfStream(new PdfDictionary(),
            BfCharBytes([(0x41, "0041"), (0x42, "0042")])));
        var wrapper1 = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+SharedDescendant"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
            [N("ToUnicode")] = Ref(5),
        };
        doc.AddObject(1, 0, wrapper1);

        var wrapper2 = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+SharedDescendant"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
        };
        if (wrapper2HasToUnicode)
        {
            doc.AddObject(8, 0, new PdfStream(new PdfDictionary(),
                BfCharBytes(wrapper2ToUnicode ?? [(0x42, "0042")])));
            wrapper2[N("ToUnicode")] = Ref(8);
        }
        doc.AddObject(7, 0, wrapper2);

        string wrapper2Hex = string.Concat((wrapper2Codes ?? [0x42]).Select(c => $"{c:X4}"));
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes($"BT /F0 12 Tf <0041 0042> Tj /F1 12 Tf <{wrapper2Hex}> Tj ET")));
        WidthPatchFixtures.AddSinglePageCatalog(doc, font1: 1, font2: 7);
        return doc;
    }

    /// <summary>
    /// Per-holder merge program, Task 1: two Type0 wrappers (objects 1 and 7), each with its OWN,
    /// separately-numbered descendant CIDFont (objects 4 and 14) — so <c>ProgramHolderId</c> genuinely
    /// differs between the two logical fonts — but both descendants' <c>/FontDescriptor</c> entries
    /// resolve to the SAME descriptor object (2), and so the same <c>/FontFile2</c> (object 3): the
    /// descriptor-level sharing case <c>FontRemediationPlanner.SharedHolderReason</c>'s
    /// object-number-only comparison used to miss (tracker issue 38 follow-up). Promotes the F-4b-era
    /// <c>TwoWrappersSharedDescriptorDoc</c>, unchanged in shape but with explicit (never
    /// <c>/Identity</c>, never CID-0) <c>/CIDToGIDMap</c> streams so the dead codes match the fixture
    /// rule the other shared fixture in this file follows.
    ///
    /// <para>Wrapper 1 draws <c>&lt;0041 0042&gt;</c> (descendant 4's map: 0x41→gid 1 live, 0x42→gid 0
    /// dead; /ToUnicode 0x41→'A', 0x42→'B'). Wrapper 2 draws <c>&lt;0043 0044&gt;</c> (descendant 14's
    /// map: 0x43→gid 1 live, 0x44→gid 0 dead; /ToUnicode 0x43→'C', 0x44→'D'). Both wrappers drawing
    /// their own dead code means each gets its own genuine 6.2.11.8 finding from the real
    /// <c>FontProgramRule</c>, which is what routes <c>Propose</c> into <c>ProposeProgramReplace</c> —
    /// where the descriptor-collision guard lives — for both.</para>
    /// </summary>
    public static PdfDocument SharedDescriptorDoc()
    {
        byte[] font = ZeroAdvanceSfntFixture.FontBytes(gid1Advance: 450);
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+SharedDescriptor"),
            [N("Flags")] = new PdfInteger(4), // symbolic
            [N("FontFile2")] = Ref(3),
        });

        // Descendant 1 (object 4): live code 0x41 -> gid 1, dead code 0x42 -> gid 0.
        doc.AddObject(6, 0, new PdfStream(new PdfDictionary(),
            LiveDeadCidToGidBytes(liveCode: 0x41, deadCode: 0x42)));
        var descendant1 = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("ABCDEF+SharedDescriptor"),
            [N("FontDescriptor")] = Ref(2),
            [N("CIDToGIDMap")] = Ref(6),
            [N("DW")] = new PdfInteger(1000),
            [N("W")] = new PdfArray(new PdfInteger(0x41), new PdfArray(new PdfInteger(500))),
        };
        AddCidSystemInfo(descendant1);
        doc.AddObject(4, 0, descendant1);

        doc.AddObject(5, 0, new PdfStream(new PdfDictionary(),
            BfCharBytes([(0x41, "0041"), (0x42, "0042")])));
        var wrapper1 = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+SharedDescriptor"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
            [N("ToUnicode")] = Ref(5),
        };
        doc.AddObject(1, 0, wrapper1);

        // Descendant 2 (object 14): live code 0x43 -> gid 1, dead code 0x44 -> gid 0. Same shared
        // program (object 3, via the shared descriptor object 2), so 0x43 also lands on gid 1 — the
        // only non-.notdef glyph the 2-glyph program has.
        doc.AddObject(16, 0, new PdfStream(new PdfDictionary(),
            LiveDeadCidToGidBytes(liveCode: 0x43, deadCode: 0x44)));
        var descendant2 = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("ABCDEF+SharedDescriptor"),
            [N("FontDescriptor")] = Ref(2),
            [N("CIDToGIDMap")] = Ref(16),
            [N("DW")] = new PdfInteger(1000),
            [N("W")] = new PdfArray(new PdfInteger(0x43), new PdfArray(new PdfInteger(500))),
        };
        AddCidSystemInfo(descendant2);
        doc.AddObject(14, 0, descendant2);

        doc.AddObject(15, 0, new PdfStream(new PdfDictionary(),
            BfCharBytes([(0x43, "0043"), (0x44, "0044")])));
        var wrapper2 = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+SharedDescriptor"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(14)),
            [N("ToUnicode")] = Ref(15),
        };
        doc.AddObject(7, 0, wrapper2);

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 12 Tf <0041 0042> Tj /F1 12 Tf <0043 0044> Tj ET")));
        WidthPatchFixtures.AddSinglePageCatalog(doc, font1: 1, font2: 7);
        return doc;
    }
}
