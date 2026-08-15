using CffTestFixtures;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>F-3: regenerating a subset declaration from the embedded program.
///
/// <para>The enumeration under test is SHARED with FontSubsetCoverageRule. That sharing is the
/// correctness guarantee, not a tidiness preference: the rule's comparison is bidirectional, so a
/// repair that enumerated the program even slightly differently would write a declaration the rule
/// still faults — a fix that reports success and changes nothing.</para></summary>
public sealed class SubsetDeclarationTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    // ── document scaffold, copied from PreflightSlice27Tests' CidDoc (the rule's own fixture) ─────────────
    private static PdfDocument DocWith(PdfDictionary font, string content, params (int, PdfObject)[] extra)
    {
        var doc = new PdfDocument();
        doc.AddObject(1, 0, font);
        foreach ((int num, PdfObject obj) in extra)
            doc.AddObject(num, 0, obj);

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(content)));
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

    private static byte[] FontBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    /// <summary>The fixture font with its <c>hhea</c> <c>numberOfHMetrics</c> overwritten with a value
    /// far beyond its real glyph count — a MALFORMED program whose own tables contradict each other.
    /// <c>EmbeddedFontMetrics</c> reads that field straight off <c>hhea</c> with no clamp, so the
    /// Identity branch of <c>SubsetProgramGlyphs.ProgramCids</c> enumerates <c>[0, numberOfHMetrics)</c>
    /// while its containment predicate is <c>cid != 0 &amp;&amp; cid &lt; NumGlyphs</c> — the two out of
    /// step. Patched here rather than vendored as a second binary: the ONE byte pair that matters is
    /// visible in the code, and it cannot drift from the real fixture it is derived from.</summary>
    private static byte[] FontBytesWithInflatedHMetrics()
    {
        byte[] bytes = FontBytes();
        int numTables = (bytes[4] << 8) | bytes[5];
        for (var i = 0; i < numTables; i++)
        {
            int record = 12 + (i * 16);
            if (Encoding.ASCII.GetString(bytes, record, 4) != "hhea") continue;
            int hhea = (bytes[record + 8] << 24) | (bytes[record + 9] << 16)
                | (bytes[record + 10] << 8) | bytes[record + 11];
            bytes[hhea + 34] = 0x7F; // numberOfHMetrics (uint16 at hhea+34) := 0x7FFF
            bytes[hhea + 35] = 0xFF;
            return bytes;
        }
        throw new InvalidOperationException("the fixture font has no hhea table to patch.");
    }

    /// <summary>Builds a CIDFontType2 (PublicPixel) document, matching the shape of PreflightSlice27Tests'
    /// CidDoc, and returns the document, the descendant CIDFont dictionary, and its parsed metrics.
    /// <paramref name="customMap"/> null → an Identity CIDToGIDMap (no entry at all); non-null → a
    /// CIDToGIDMap stream holding that mapping. <paramref name="content"/> is the page's content stream,
    /// which is what determines the font's <c>UsedCodes</c>; <paramref name="encoding"/> its
    /// <c>/Encoding</c> CMap name.</summary>
    private static (PdfDocument Doc, PdfDictionary CidDict, EmbeddedFontMetrics Metrics) TrueTypeCidFont(
        byte[]? customMap, string content = "BT ET", string encoding = "Identity-H",
        byte[]? fontProgram = null)
    {
        var descriptor = new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+PublicPixel"),
            [N("Flags")] = new PdfInteger(4),
            [N("FontFile2")] = Ref(3),
        };

        var cidFont = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("ABCDEF+PublicPixel"),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = new PdfString(Encoding.Latin1.GetBytes("Adobe")),
                [N("Ordering")] = new PdfString(Encoding.Latin1.GetBytes("Identity")),
                [N("Supplement")] = new PdfInteger(0),
            },
            [N("FontDescriptor")] = Ref(2),
        };
        if (customMap is not null)
            cidFont[N("CIDToGIDMap")] = Ref(6);

        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+PublicPixel"),
            [N("Encoding")] = N(encoding),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
        };

        var extra = new List<(int, PdfObject)>
        {
            (2, descriptor),
            (3, new PdfStream(new PdfDictionary(), fontProgram ?? FontBytes())),
            (4, cidFont),
        };
        if (customMap is not null)
            extra.Add((6, new PdfStream(new PdfDictionary(), customMap)));

        PdfDocument doc = DocWith(font, content, extra.ToArray());

        var type0Font = (Type0Font)PdfFont.Create(font, doc)!;
        EmbeddedFontMetrics metrics = type0Font.GetEmbeddedMetrics()!;

        return (doc, cidFont, metrics);
    }

    /// <summary>An Identity CIDToGIDMap yields CIDs [0, NumberOfHMetrics), matching what the rule
    /// computed before the extraction. PublicPixel.ttf is the same fixture the rule's own tests use.</summary>
    [Fact]
    public void Identity_cid_to_gid_map_enumerates_the_metric_range()
    {
        (PdfDocument doc, PdfDictionary cidDict, EmbeddedFontMetrics metrics) = TrueTypeCidFont(customMap: null);

        (IReadOnlySet<int>? cids, Func<int, bool> contains) =
            SubsetProgramGlyphs.ProgramCids(doc, cidDict, metrics);

        Assert.NotNull(cids);
        Assert.Equal(metrics.NumberOfHMetrics, cids!.Count);
        Assert.Contains(1, cids);
        Assert.True(contains(1));
        Assert.False(contains(0));
    }

    /// <summary>A custom CIDToGIDMap enumerates the mapping, not the metric range — the two differ,
    /// and picking the wrong one writes a declaration for glyphs the program does not have.
    ///
    /// <para>The EXCLUSIONS carry this test (final whole-branch review, 2026-08-14, deferred minor 1):
    /// the original asserted only that CIDs 1 and 2 were present, which an off-by-one in <c>Gid()</c>
    /// would satisfy just as happily — every GID in the old fixture was small enough to stay in range
    /// however it was misread. So the map now carries a CID whose GID is OUT of range, which is the
    /// only entry whose classification depends on <c>Gid()</c> reading the right byte pair.</para>
    ///
    /// <para>Note what is deliberately NOT asserted: CID 3, mapping to GID 0, IS enumerated. The
    /// review expected it excluded, but the predicate is veraPDF's <c>CIDFontType2Program</c> — "each
    /// in-range CID whose GID is below the glyph count" — and GID 0 is below the glyph count. The
    /// enumeration must mirror <c>FontSubsetCoverageRule</c> exactly (that sharing is the whole
    /// correctness guarantee), so pinning the opposite here would pin a divergence from the oracle
    /// the rule replicates.</para></summary>
    [Fact]
    public void A_custom_cid_to_gid_map_enumerates_the_mapping()
    {
        // CIDs 1 and 2 map to GIDs 1 and 2; CID 3 maps to GID 0; CID 4 maps to GID 0xFFFF, which is
        // beyond any real glyph count and so is the one entry the mapping excludes.
        byte[] map = [0, 0, 0, 1, 0, 2, 0, 0, 0xFF, 0xFF];
        (PdfDocument doc, PdfDictionary cidDict, EmbeddedFontMetrics metrics) = TrueTypeCidFont(map);
        Assert.True(metrics.NumGlyphs < 0xFFFF, "fixture assumes GID 0xFFFF is out of range");

        (IReadOnlySet<int>? cids, Func<int, bool> contains) =
            SubsetProgramGlyphs.ProgramCids(doc, cidDict, metrics);

        Assert.NotNull(cids);
        Assert.Contains(1, cids!);
        Assert.Contains(2, cids!);
        Assert.DoesNotContain(4, cids!);
        Assert.DoesNotContain(0, cids!); // CID 0 is never part of the agreement

        // The predicate is asserted alongside the set, not discarded: the rule's comparison is
        // BIDIRECTIONAL and reads both, so a set that agreed while the predicate disagreed would
        // still fault a declaration regenerated from that set.
        Assert.True(contains(1));
        Assert.True(contains(2));
        Assert.False(contains(4));
        Assert.False(contains(0));
        Assert.All(cids!, cid => Assert.True(contains(cid)));
    }

    // ── Task 2 write-op fixtures ─────────────────────────────────────────────────────────────────────────

    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? doc.GetObject(reference.ObjectNumber) : obj;

    /// <summary>A CIDFontType2 document (Task 1's <see cref="TrueTypeCidFont"/>) whose descendant CIDFont
    /// descriptor already carries a placeholder <c>/CIDSet</c> — required for <c>SetCidSet</c> to write,
    /// since it corrects an existing declaration and never introduces one. Returns the editor and the
    /// descendant CIDFont's id (object 4), the PROGRAM HOLDER.</summary>
    private static (PdfDocumentEditor Editor, FontId Holder) CidFontEditor()
    {
        (PdfDocument doc, PdfDictionary _, EmbeddedFontMetrics _) = TrueTypeCidFont(customMap: null);

        var descriptor = (PdfDictionary)doc.GetObject(2)!;
        doc.AddObject(7, 0, new PdfStream(new PdfDictionary(), []));
        descriptor.Set(N("CIDSet"), Ref(7));

        return (doc.Edit(), new FontId(4));
    }

    /// <summary>A minimal Type1 font document whose descriptor already carries a placeholder
    /// <c>/CharSet</c> — required for <c>SetCharSet</c> to write. Returns the editor and the font's id
    /// (object 1); a Type1 font is its own program holder.</summary>
    private static (PdfDocumentEditor Editor, FontId Holder) Type1FontEditor()
    {
        var descriptor = new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+Test"),
            [N("Flags")] = new PdfInteger(4),
            [N("CharSet")] = new PdfString(Encoding.Latin1.GetBytes("/placeholder")),
        };
        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("ABCDEF+Test"),
            [N("FontDescriptor")] = Ref(2),
        };
        PdfDocument doc = DocWith(font, "BT ET", (2, descriptor));
        return (doc.Edit(), new FontId(1));
    }

    private static byte[] CidSetBytes(PdfDocumentEditor editor, FontId holder)
    {
        var cidDict = (PdfDictionary)editor.Document.GetObject(holder.ObjectNumber)!;
        var descriptor = (PdfDictionary)Resolve(editor.Document, cidDict.Get(N("FontDescriptor")))!;
        var stream = (PdfStream)Resolve(editor.Document, descriptor.Get(N("CIDSet")))!;
        return stream.GetDecodedData(editor.Document.Decryptor);
    }

    private static string CharSetValue(PdfDocumentEditor editor, FontId holder)
    {
        var fontDict = (PdfDictionary)editor.Document.GetObject(holder.ObjectNumber)!;
        var descriptor = (PdfDictionary)Resolve(editor.Document, fontDict.Get(N("FontDescriptor")))!;
        var charSet = (PdfString)Resolve(editor.Document, descriptor.Get(N("CharSet")))!;
        return charSet.Value;
    }

    /// <summary>Copy of <c>FontSubsetCoverageRule.DecodeCidSet</c> (:207-215), deliberately duplicated so
    /// the test decodes independently of the writer rather than sharing its assumptions.</summary>
    private static HashSet<int> DecodeCidSet(byte[] bytes)
    {
        var set = new HashSet<int>();
        for (int i = 0; i < bytes.Length; i++)
            for (int bit = 0; bit < 8; bit++)
                if ((bytes[i] & (0x80 >> bit)) != 0)
                    set.Add(i * 8 + bit);
        return set;
    }

    /// <summary>The written /CIDSet is a bitmap the rule can read back: bit i set ⇒ CID i present,
    /// MSB-first within each byte. Asserted by decoding the bytes the same way the rule does.</summary>
    [Fact]
    public void Writing_a_cid_set_produces_the_bitmap_the_rule_decodes()
    {
        (PdfDocumentEditor editor, FontId holder) = CidFontEditor();

        editor.SetCidSet(holder, new HashSet<int> { 1, 2, 9 });

        Assert.Equal(new HashSet<int> { 1, 2, 9 }, DecodeCidSet(CidSetBytes(editor, holder)));
    }

    /// <summary>A /CharSet is written as a run of PDF name tokens, which is what the rule parses.</summary>
    [Fact]
    public void Writing_a_char_set_produces_parseable_name_tokens()
    {
        (PdfDocumentEditor editor, FontId holder) = Type1FontEditor();

        editor.SetCharSet(holder, new HashSet<string> { "a", "b" });

        string charSet = CharSetValue(editor, holder);
        Assert.Contains("/a", charSet);
        Assert.Contains("/b", charSet);
    }

    /// <summary>The write survives a save/reload — it edited the document, not a snapshot.</summary>
    [Fact]
    public void The_written_cid_set_survives_a_round_trip()
    {
        (PdfDocumentEditor editor, FontId holder) = CidFontEditor();
        editor.SetCidSet(holder, new HashSet<int> { 3 });

        using var saved = new MemoryStream();
        editor.Save(saved);
        var reloaded = PdfDocumentEditor.Open(new MemoryStream(saved.ToArray()));

        Assert.Equal(new HashSet<int> { 3 }, DecodeCidSet(CidSetBytes(reloaded, holder)));
    }

    // ── Task 3 planning fixtures ─────────────────────────────────────────────────────────────────────────

    /// <summary>Runs the planner over one <c>font-subset-coverage</c> finding attributed to
    /// <paramref name="fixture"/>'s object, using the tuple overload so no <c>PreflightResult</c> has to be
    /// constructed. The provider is never consulted for this rule.</summary>
    private static FontRemediationProposal PlanFor((PdfDocument Doc, int ObjectNumber) fixture) =>
        new FontRemediationPlanner(SystemFontLocator.Default)
            .Propose(fixture.Doc, new[] { ("font-subset-coverage", fixture.ObjectNumber) });

    /// <summary>A CID above everything the program contains, so declaring it is unambiguously surplus:
    /// <c>SubsetProgramGlyphs.ProgramCids</c>'s Identity predicate is <c>cid != 0 &amp;&amp; cid &lt; NumGlyphs</c>.</summary>
    private static int SurplusCid(EmbeddedFontMetrics metrics) => metrics.NumGlyphs + 8;

    /// <summary>Encodes CIDs as the /CIDSet bitmap the rule decodes: bit i set, MSB-first per byte.</summary>
    private static byte[] CidBitmap(params int[] cids)
    {
        var bytes = new byte[cids.Max() / 8 + 1];
        foreach (int cid in cids)
            bytes[cid / 8] |= (byte)(0x80 >> (cid % 8));
        return bytes;
    }

    /// <summary>Attaches a /CIDSet stream (object 7) carrying <paramref name="declared"/> to the descendant
    /// CIDFont's descriptor (object 2).</summary>
    private static void AttachCidSet(PdfDocument doc, byte[] declared)
    {
        doc.AddObject(7, 0, new PdfStream(new PdfDictionary(), declared));
        ((PdfDictionary)doc.GetObject(2)!).Set(N("CIDSet"), Ref(7));
    }

    /// <summary>The descendant CIDFont's object number — where <c>FontSubsetCoverageRule</c> attributes a
    /// CID finding, and the PROGRAM HOLDER a proposal must target.</summary>
    private const int CidHolderObject = 4;

    /// <summary>A /CIDSet declaring a CID the program does not contain, for a document that draws NO text —
    /// so the surplus entry is unreachable and the declaration is merely stale.</summary>
    private static (PdfDocument Doc, int ObjectNumber) StaleCidSetDocument()
    {
        (PdfDocument doc, PdfDictionary _, EmbeddedFontMetrics metrics) = TrueTypeCidFont(customMap: null);
        AttachCidSet(doc, CidBitmap(1, 2, SurplusCid(metrics)));
        return (doc, CidHolderObject);
    }

    /// <summary>The same surplus declaration, but the page DRAWS the surplus CID — under Identity-H the
    /// two-byte character code is the CID, so the glyph renders .notdef today.</summary>
    private static (PdfDocument Doc, int ObjectNumber) SurplusUsedCidDocument()
    {
        // Built twice: the surplus CID depends on the parsed program's glyph count, and the content stream
        // that USES it has to be in place before FontInventory walks the page.
        (PdfDocument _, PdfDictionary _, EmbeddedFontMetrics probe) = TrueTypeCidFont(customMap: null);
        int surplus = SurplusCid(probe);

        (PdfDocument doc, PdfDictionary _, EmbeddedFontMetrics _) =
            TrueTypeCidFont(customMap: null, content: $"BT /F0 12 Tf <{surplus:X4}> Tj ET");
        AttachCidSet(doc, CidBitmap(1, 2, surplus));
        return (doc, CidHolderObject);
    }

    /// <summary>The same font with neither /CIDSet nor /CharSet on its descriptor.</summary>
    private static (PdfDocument Doc, int ObjectNumber) NoDeclarationDocument()
    {
        (PdfDocument doc, PdfDictionary _, EmbeddedFontMetrics _) = TrueTypeCidFont(customMap: null);
        return (doc, CidHolderObject);
    }

    // ── Task 3 tests ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A stale /CIDSet — one declaring CIDs the program does not have, for codes nothing
    /// uses — regenerates to the program's own CIDs.</summary>
    [Fact]
    public void A_stale_cid_set_is_proposed_for_regeneration()
    {
        FontRemediationProposal proposed = PlanFor(StaleCidSetDocument());

        var regenerate = Assert.IsType<RegenerateDeclarationProposal>(Assert.Single(proposed.Fonts));
        Assert.NotNull(regenerate.Cids);
        Assert.Null(regenerate.GlyphNames);
    }

    /// <summary>The regenerated CIDs are the PROGRAM's, targeted at the program holder — the descendant
    /// CIDFont, where /FontDescriptor lives — so handing them to SetCidSet writes a declaration the rule
    /// accepts.</summary>
    [Fact]
    public void The_regenerated_cids_are_the_programs_and_target_the_program_holder()
    {
        (PdfDocument doc, PdfDictionary cidDict, EmbeddedFontMetrics metrics) =
            TrueTypeCidFont(customMap: null);
        AttachCidSet(doc, CidBitmap(1, 2, SurplusCid(metrics)));

        var regenerate = Assert.IsType<RegenerateDeclarationProposal>(
            Assert.Single(PlanFor((doc, CidHolderObject)).Fonts));

        (IReadOnlySet<int>? programCids, _) = SubsetProgramGlyphs.ProgramCids(doc, cidDict, metrics);
        Assert.Equal(programCids, regenerate.Cids);
        Assert.Equal(new FontId(CidHolderObject), regenerate.Font);
    }

    /// <summary>THE load-bearing refusal (spec §5.4). A declared CID the program does not contain,
    /// whose code the document actually USES, is not a stale declaration — it is a truncated program
    /// wearing a font-subset-coverage mask. Rewriting the declaration there would make the document
    /// assert conformance while the glyph still renders .notdef. That case is F-4's, so the planner
    /// declines rather than fixing it.</summary>
    [Fact]
    public void A_surplus_entry_for_a_used_code_is_declined()
    {
        FontRemediationProposal proposed = PlanFor(SurplusUsedCidDocument());

        var decline = Assert.IsType<DeclineProposal>(Assert.Single(proposed.Fonts));
        Assert.Contains("program", decline.Reason, StringComparison.OrdinalIgnoreCase);
        // Pins WHICH decline: several reasons here mention "program", and a fixture whose used code
        // silently failed to register would decline for an unrelated cause and still pass the line above.
        Assert.Contains("the document uses them", decline.Reason, StringComparison.Ordinal);
    }

    /// <summary>Under a non-Identity CMap the planner cannot cheaply prove which CID a used code selects,
    /// so ANY surplus entry is declined rather than assumed stale — the conservative direction, because
    /// the cost of being wrong is asserting conformance the file does not have.</summary>
    [Fact]
    public void A_surplus_entry_under_a_non_identity_cmap_is_declined()
    {
        (PdfDocument doc, PdfDictionary _, EmbeddedFontMetrics metrics) =
            TrueTypeCidFont(customMap: null, encoding: "UniJIS-UCS2-H");
        AttachCidSet(doc, CidBitmap(1, 2, SurplusCid(metrics)));

        var decline = Assert.IsType<DeclineProposal>(Assert.Single(PlanFor((doc, CidHolderObject)).Fonts));
        Assert.Contains("Identity", decline.Reason, StringComparison.Ordinal);
    }

    /// <summary>A font with no declaration at all is never touched: the rule is silent on it, so a
    /// proposal here would be editing a document with nothing wrong with it — and would create a
    /// conformance obligation it never had.</summary>
    [Fact]
    public void A_font_with_no_declaration_is_not_proposed()
    {
        FontRemediationProposal proposed = PlanFor(NoDeclarationDocument());

        Assert.Empty(proposed.Fonts);
    }

    /// <summary>A program whose own tables disagree — <c>numberOfHMetrics</c> beyond <c>numGlyphs</c>,
    /// so the Identity branch enumerates CIDs its own containment predicate rejects — is DECLINED, not
    /// regenerated (final whole-branch review, 2026-08-14, Important 2).
    ///
    /// <para>Why a decline is the only honest answer: the rule's <c>CidsAgree</c> is bidirectional, so
    /// for such a font it is UNSATISFIABLE — direction 1 demands the enumerated CIDs be declared and
    /// direction 2 rejects any declared CID the predicate refuses. A regenerated /CIDSet would
    /// therefore still be faulted, and <c>RegenerateDeclarationProposal</c>'s promise that applying it
    /// necessarily satisfies the rule would be false. The disagreement is caught in the PLANNER, never
    /// by "fixing" <c>SubsetProgramGlyphs</c>: that enumeration must keep mirroring
    /// <c>FontSubsetCoverageRule</c> exactly.</para></summary>
    [Fact]
    public void A_program_whose_metrics_count_exceeds_its_glyph_count_is_declined()
    {
        (PdfDocument doc, PdfDictionary cidDict, EmbeddedFontMetrics metrics) =
            TrueTypeCidFont(customMap: null, fontProgram: FontBytesWithInflatedHMetrics());

        // Pins the fixture actually being malformed the way this test claims: without this, a font
        // parser that silently clamped numberOfHMetrics would leave the test asserting a decline that
        // came from some unrelated cause — or make it fail for the right reason but the wrong one.
        Assert.True(metrics.NumberOfHMetrics > metrics.NumGlyphs,
            $"fixture patch did not take: numberOfHMetrics {metrics.NumberOfHMetrics} vs "
            + $"numGlyphs {metrics.NumGlyphs}");
        (IReadOnlySet<int>? programCids, Func<int, bool> contains) =
            SubsetProgramGlyphs.ProgramCids(doc, cidDict, metrics);
        Assert.Contains(programCids!, cid => cid != 0 && !contains(cid));

        AttachCidSet(doc, CidBitmap(1, 2));

        var decline = Assert.IsType<DeclineProposal>(Assert.Single(PlanFor((doc, CidHolderObject)).Fonts));
        Assert.Contains("disagree", decline.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── the /CharSet half ────────────────────────────────────────────────────────────────────────────────

    /// <summary>A glyph name the program does not contain. Reachable from code 65 through the font's
    /// /Differences, so a page that draws 'A' USES it and one that draws nothing does not — the single
    /// variable separating the two /CharSet fixtures below.</summary>
    private const string SurplusGlyphName = "zzzsurplus";

    /// <summary>The glyph names <c>MinimalCff.Build(charsetOperand: null, numGlyphs: 4)</c> contains:
    /// .notdef plus SIDs 1..3 of the default ISOAdobe charset. Pinned independently by
    /// <c>CffCharsetEnumerationTests.IsoAdobeCharset_GlyphNameEnumerationStillAnswers</c>.</summary>
    private static readonly string[] ProgramNames = ["exclam", "quotedbl", "space", ".notdef"];

    /// <summary>A subset Type1 font with a real Type1C (CFF) program, whose /CharSet declares the
    /// program's own names PLUS <see cref="SurplusGlyphName"/>. <paramref name="content"/> decides
    /// whether the surplus name is USED. Object 1 is the font — a simple font is its own program
    /// holder, so it is both the finding's object and the proposal's target.</summary>
    private static (PdfDocument Doc, int ObjectNumber) Type1CharSetDocument(string content)
    {
        var descriptor = new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+TestFont"),
            [N("Flags")] = new PdfInteger(4),
            [N("FontFile3")] = Ref(3),
            [N("CharSet")] = new PdfString(
                Encoding.Latin1.GetBytes($"/space/exclam/quotedbl/{SurplusGlyphName}")),
        };

        var font = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("ABCDEF+TestFont"),
            [N("FontDescriptor")] = Ref(2),
            [N("Encoding")] = new PdfDictionary
            {
                [N("Type")] = N("Encoding"),
                [N("Differences")] = new PdfArray(new PdfInteger(65), N(SurplusGlyphName)),
            },
        };

        var program = new PdfStream(
            new PdfDictionary { [N("Subtype")] = N("Type1C") },
            MinimalCff.Build(charsetOperand: null, numGlyphs: 4));

        return (DocWith(font, content, (2, descriptor), (3, program)), 1);
    }

    /// <summary>A stale /CharSet — declaring a name the program lacks, for a code nothing draws —
    /// regenerates to the PROGRAM's glyph names, with the surplus name dropped.</summary>
    [Fact]
    public void A_stale_char_set_regenerates_to_the_programs_glyph_names()
    {
        (PdfDocument doc, int objectNumber) = Type1CharSetDocument("BT ET");

        var regenerate = Assert.IsType<RegenerateDeclarationProposal>(
            Assert.Single(PlanFor((doc, objectNumber)).Fonts));

        Assert.Null(regenerate.Cids);
        Assert.NotNull(regenerate.GlyphNames);
        Assert.Equal(
            ProgramNames.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            regenerate.GlyphNames!.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal(new FontId(1), regenerate.Font);
    }

    /// <summary>The /CharSet mirror of <see cref="A_surplus_entry_for_a_used_code_is_declined"/>, and the
    /// reason it must exist separately: the two halves carry independent copies of the load-bearing
    /// refusal. Differs from the stale fixture in exactly one variable — the page draws code 65, which
    /// /Differences maps to the surplus name.</summary>
    [Fact]
    public void A_surplus_char_set_name_for_a_used_code_is_declined()
    {
        (PdfDocument doc, int objectNumber) = Type1CharSetDocument("BT /F0 12 Tf (A) Tj ET");

        var decline = Assert.IsType<DeclineProposal>(Assert.Single(PlanFor((doc, objectNumber)).Fonts));
        Assert.Contains("the document uses them", decline.Reason, StringComparison.Ordinal);
    }
}
