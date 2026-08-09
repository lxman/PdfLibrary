using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Tests for <see cref="FontRemediationPlanner"/>.
///
/// <para>No <c>TestFixtures.Path(...)</c> helper exists in this project (confirmed by
/// <c>FontInventoryTests.cs</c>'s own comment on the point, and by
/// <c>PdfDocumentEditorFontsTests.cs</c> following the same convention). These tests build fixtures
/// directly with <see cref="PdfDocument.AddObject"/>, mirroring both files, and run them through
/// <see cref="Preflighter.Check(PdfDocument, ConformanceProfile)"/> - the only entry point the rest
/// of the suite uses (confirmed by grep; <c>Preflighter.Run</c> does not exist as a public
/// member).</para>
///
/// <para><b>Why the "all provable" fixtures use the VALUES rule, not the missing-mapping rule:</b>
/// <c>Pdfa2uToUnicodeRule</c> ("pdfa2u-tounicode") only fires when
/// <c>FontUnicodeMapping.HasReliableUnicode</c> is FALSE for some code actually drawn - i.e. a code
/// with a real, non-empty glyph name that does NOT resolve through the Adobe Glyph List or the
/// uniXXXX/uXXXXXX convention. <see cref="FontRemediationPlanner"/>'s <c>ProvableUnicode</c> derives
/// a value through that exact same mechanism (Step 5 of the task brief: reuse
/// <c>FontUnicodeMapping</c>'s own building blocks so the planner and the rule cannot disagree).
/// That means a code the rule complained about can never turn out provable, and a font that fired
/// "pdfa2u-tounicode" always has AT LEAST ONE code in <c>NeedsUserInput</c> - verified empirically
/// while writing this file: an all-WinAnsiEncoding, all-AGL-coverable fixture with no /ToUnicode
/// produces ZERO findings at all, not a finding with an empty NeedsUserInput. The "all provable, none
/// needs the user" scenario is only reachable through
/// <c>Pdfa2uToUnicodeValuesRule</c> ("pdfa2u-tounicode-values"), which fires on an EXISTING but
/// forbidden /ToUnicode value - a finding the planner then answers with a FRESH derivation from the
/// encoding, independent of the bad value that triggered it.</para>
/// </summary>
public class FontRemediationPlannerTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    // -- A finding on a font whose used codes ALL re-derive cleanly: everything is provable --------

    [Fact]
    public void Propose_MapsAProvableCodeThroughItsGlyphName()
    {
        // WinAnsiEncoding font whose /ToUnicode maps code 'A' to a forbidden value (U+FFFF), firing
        // pdfa2u-tounicode-values. WinAnsiEncoding's own glyph name for 'A' ("A") resolves through
        // the Adobe Glyph List, so the planner's fresh derivation succeeds for the only code used.
        (PdfDocument doc, PreflightResult findings) = Run(BuildForbiddenValueDocument());
        using PdfDocument document = doc;

        ToUnicodeProposal proposal = Assert.IsType<ToUnicodeProposal>(
            Assert.Single(new FontRemediationPlanner().Propose(document, findings).Fonts));

        Assert.Equal("pdfa2u-tounicode-values", proposal.RuleId);
        Assert.NotEmpty(proposal.Provable);
        Assert.Empty(proposal.NeedsUserInput);
        Assert.Equal("A", proposal.Provable[0x41]);
    }

    // The direction a single well-aimed test misses: an unprovable code must NOT be invented.
    [Fact]
    public void Propose_LeavesAnUnprovableCodeToTheUser()
    {
        // A symbolic font: code 'A' is remapped via /Differences to a made-up glyph name that is
        // neither an AGL name nor the uniXXXX/uXXXXXX convention (positive evidence of no mapping -
        // fires pdfa2u-tounicode). Code 'B' is left as a genuine, AGL-derivable glyph name, so the
        // fixture proves the planner does not lump every code into NeedsUserInput out of caution -
        // ONLY the genuinely unprovable one lands there, and it never leaks into Provable.
        (PdfDocument doc, PreflightResult findings) = Run(BuildSymbolicDifferencesDocument());
        using PdfDocument document = doc;

        ToUnicodeProposal proposal = Assert.IsType<ToUnicodeProposal>(
            Assert.Single(new FontRemediationPlanner().Propose(document, findings).Fonts));

        Assert.Equal("pdfa2u-tounicode", proposal.RuleId);
        Assert.NotEmpty(proposal.NeedsUserInput);
        Assert.Contains(0x41, proposal.NeedsUserInput);       // 'customGlyph1' - no honest answer
        Assert.Equal("B", proposal.Provable[0x42]);            // 'B' - still derived normally
        Assert.DoesNotContain(proposal.NeedsUserInput, code => proposal.Provable.ContainsKey(code));
    }

    [Fact]
    public void Propose_NeverProposesBackAValueTheRuleRejected()
    {
        // pdfa2u-tounicode-values fires on an EXISTING mapping to a forbidden code point.
        // Re-proposing it would stage the very value that produced the finding.
        (PdfDocument doc, PreflightResult findings) = Run(BuildForbiddenValueDocument());
        using PdfDocument document = doc;

        ToUnicodeProposal proposal = Assert.IsType<ToUnicodeProposal>(
            Assert.Single(new FontRemediationPlanner().Propose(document, findings).Fonts));

        Assert.NotEmpty(proposal.Provable);
        char[] forbidden = ['\u0000', '\uFEFF', '\uFFFE', '\uFFFF'];
        foreach (string value in proposal.Provable.Values)
        {
            Assert.NotEmpty(value);
            Assert.DoesNotContain(value, c => Array.IndexOf(forbidden, c) >= 0);
        }
    }

    [Fact]
    public void Propose_DeclinesAnUnaddressableFont()
    {
        // A Type0 font (indirect, object 20) over a descendant CIDFont embedded DIRECTLY in
        // /DescendantFonts (ISO 32000-1 Table 121 requires the array, not that its element be
        // indirect) with an Identity ordering - FontUnicodeMapping treats Identity as having no
        // derivable CID-to-Unicode mapping, so pdfa2u-tounicode fires with the Type0's OWN object
        // number (20, indirect - Finding.ObjectNumber is only ever set from an indirect
        // FontDictionary). FontInventory then reports IsAddressable=false because the PROGRAM
        // HOLDER (the descendant) has no object number of its own to write a /ToUnicode onto.
        (PdfDocument doc, PreflightResult findings) = Run(BuildDirectDescendantType0Document());
        using PdfDocument document = doc;

        DeclineProposal decline = Assert.IsType<DeclineProposal>(
            Assert.Single(new FontRemediationPlanner().Propose(document, findings).Fonts));

        Assert.NotEmpty(decline.Reason);
        Assert.Equal(20, decline.Font.ObjectNumber);
    }

    // Step 5's second constraint: a value the derivation itself produces can be forbidden (the
    // uXXXXXX convention derives U+FFFF directly from the glyph name "uFFFF"). Proposing it back
    // would stage exactly the kind of value pdfa2u-tounicode-values rejects — this must land in
    // NeedsUserInput, not Provable, even though a real derivation exists for it.
    [Fact]
    public void Propose_TreatsAForbiddenDerivedValueAsUnprovable()
    {
        (PdfDocument doc, PreflightResult findings) = Run(BuildForbiddenUConventionDocument());
        using PdfDocument document = doc;

        ToUnicodeProposal proposal = Assert.IsType<ToUnicodeProposal>(
            Assert.Single(new FontRemediationPlanner().Propose(document, findings).Fonts));

        Assert.Contains(0x41, proposal.NeedsUserInput);
        Assert.DoesNotContain(0x41, proposal.Provable.Keys);
    }

    // Important-1 fix: a partial /ToUnicode CMap (routine in subset fonts) must not be re-derived
    // and lost. pdfa2u-tounicode only flags a font's UNCOVERED codes (HasReliableUnicode is true for
    // any code that already has a mapping), but a proposal spans every drawn code — so the covered
    // code here, whose glyph name is NOT AGL-derivable on its own, must still surface with its
    // EXISTING value rather than falling into NeedsUserInput (which would then make
    // PdfDocumentEditor.SetToUnicode, a REPLACE not a merge, destroy the correct existing entry).
    [Fact]
    public void Propose_KeepsAnExistingCoveredCodeFromAPartialCMap()
    {
        (PdfDocument doc, PreflightResult findings) = Run(BuildPartialCMapDocument());
        using PdfDocument document = doc;

        ToUnicodeProposal proposal = Assert.IsType<ToUnicodeProposal>(
            Assert.Single(new FontRemediationPlanner().Propose(document, findings).Fonts));

        // Covered by the existing (partial) CMap, non-AGL glyph name: must come from the existing
        // entry, not be abandoned to the user just because a fresh glyph-name derivation would fail.
        Assert.Equal("A", proposal.Provable[0x41]);
        Assert.DoesNotContain(0x41, proposal.NeedsUserInput);

        // NOT covered by the CMap, ALSO non-AGL: genuinely unprovable, and the reason the finding
        // fired in the first place.
        Assert.Contains(0x42, proposal.NeedsUserInput);
        Assert.DoesNotContain(0x42, proposal.Provable.Keys);
    }

    [Fact]
    public void Propose_IgnoresFindingsFromOtherRuleFamilies()
    {
        // A single, fully AGL-derivable font: zero tounicode-family findings. The fixture is
        // otherwise a bare hand-built document (no XMP, no OutputIntent, no PDF/A identification),
        // so it is guaranteed to produce SOME other finding - guarding this test's own premise that
        // there is something for the planner to legitimately ignore, not an empty findings list.
        (PdfDocument doc, PreflightResult findings) = Run(BuildFullyProvableNoToUnicodeDocument());
        using PdfDocument document = doc;

        Assert.Contains(findings.Findings, f => f.RuleId is not "pdfa2u-tounicode" and not "pdfa2u-tounicode-values");
        Assert.DoesNotContain(findings.Findings, f => f.RuleId is "pdfa2u-tounicode" or "pdfa2u-tounicode-values");

        Assert.Empty(new FontRemediationPlanner().Propose(document, findings).Fonts);
    }

    // Minor-2 fix: FontId(0) is an overloaded sentinel FontInventory assigns to every DIRECT
    // (non-indirect) logical font dictionary. Two distinct such fonts, each with an INDIRECT program
    // holder of its own, collide on Id alone; keying the dedup set on
    // (Id.ObjectNumber, ProgramHolderId.ObjectNumber, RuleId) tells them apart. Findings are
    // hand-constructed (bypassing Preflighter) because the two rules this planner currently handles
    // only ever report a WRAPPER's own object number, never a descendant's — so this exact collision
    // needs a Finding shaped the way a FUTURE rule (naming the descendant, per FontEmbeddingRule's own
    // convention) would produce; PreflightResult/Finding are ordinary public types, so building it
    // directly is the natural way to test a case current rules cannot yet trigger end to end.
    [Fact]
    public void Propose_DoesNotDropASecondDirectFontWithADifferentProgramHolder()
    {
        using PdfDocument document = BuildTwoDirectType0WrappersDocument();
        var findings = new PreflightResult
        {
            Profile = ConformanceProfile.PdfA2u,
            Findings =
            [
                new Finding
                {
                    RuleId = "pdfa2u-tounicode", Severity = FindingSeverity.Error,
                    Clause = "test", Message = "test", ObjectNumber = 21, // first font's descendant
                },
                new Finding
                {
                    RuleId = "pdfa2u-tounicode", Severity = FindingSeverity.Error,
                    Clause = "test", Message = "test", ObjectNumber = 41, // second font's descendant
                },
            ],
        };

        IReadOnlyList<FontProposal> proposals = new FontRemediationPlanner().Propose(document, findings).Fonts;

        // Both are unaddressable (direct wrapper) so both are declines, not ToUnicode proposals - the
        // point under test is that there are TWO of them, not one silently swallowed by the other.
        Assert.Equal(2, proposals.Count);
        Assert.All(proposals, p => Assert.IsType<DeclineProposal>(p));
    }

    private static (PdfDocument, PreflightResult) Run(PdfDocument document)
    {
        PreflightResult findings = Preflighter.Check(document, ConformanceProfile.PdfA2u);
        return (document, findings);
    }

    /// <summary>A Type1 font (object 30) with WinAnsiEncoding and a /ToUnicode CMap (object 31)
    /// mapping the only drawn code, 'A' (0x41), to U+FFFF - forbidden by PDF/A-2u
    /// (<see cref="FontUnicodeMapping.IsForbiddenUnicodeValue"/>). Fires
    /// pdfa2u-tounicode-values.</summary>
    private static PdfDocument BuildForbiddenValueDocument()
    {
        var doc = new PdfDocument();
        doc.AddObject(31, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(
            "/CIDInit /ProcSet findresource begin\n"
            + "1 begincodespacerange\n<00> <FF>\nendcodespacerange\n"
            + "1 beginbfchar\n<41> <FFFF>\nendbfchar\n"
            + "endcmap\nend\nend")));
        doc.AddObject(30, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("Helvetica"),
            [N("Encoding")] = N("WinAnsiEncoding"),
            [N("FirstChar")] = new PdfInteger(65),
            [N("LastChar")] = new PdfInteger(65),
            [N("Widths")] = new PdfArray(new PdfInteger(722)),
            [N("ToUnicode")] = Ref(31),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET")));
        AddSinglePageCatalog(doc, font: 30);
        return doc;
    }

    /// <summary>A Type1 font (object 30) whose /Encoding is WinAnsiEncoding overridden by
    /// /Differences: code 'A' (0x41) maps to the made-up name "customGlyph1" (not an AGL name, not
    /// uniXXXX/uXXXXXX), code 'B' (0x42) maps to the ordinary AGL name "B". No /ToUnicode. Fires
    /// pdfa2u-tounicode on code 'A'.</summary>
    private static PdfDocument BuildSymbolicDifferencesDocument()
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("CustomSymbolFont"),
            [N("Encoding")] = new PdfDictionary
            {
                [N("BaseEncoding")] = N("WinAnsiEncoding"),
                [N("Differences")] = new PdfArray(
                    new PdfInteger(0x41), N("customGlyph1"), N("B")),
            },
            [N("FirstChar")] = new PdfInteger(65),
            [N("LastChar")] = new PdfInteger(66),
            [N("Widths")] = new PdfArray(new PdfInteger(722), new PdfInteger(667)),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf (AB) Tj ET")));
        AddSinglePageCatalog(doc, font: 30);
        return doc;
    }

    /// <summary>A Type1 font (object 30) whose /Encoding overrides code 'A' (0x41) via /Differences
    /// to the glyph name "uFFFF" — a syntactically valid uXXXXXX-convention name that derives to
    /// U+FFFF, a PDF/A-2u-forbidden value. Code 'B' (0x42) is left at WinAnsiEncoding's default and
    /// carries an EXISTING /ToUnicode entry (object 31) mapping it to another forbidden value
    /// (U+FFFE), so pdfa2u-tounicode-values fires and the planner has a finding to act on — the
    /// interesting code (0x41) never gets its own finding, because
    /// <c>FontUnicodeMapping.HasReliableUnicode</c> treats ANY uXXXXXX-shaped name as reliable
    /// without checking whether the resulting code point is forbidden; that gap is exactly why
    /// <c>FontRemediationPlanner.Provable</c> re-checks <see cref="FontUnicodeMapping.IsForbiddenUnicodeValue"/>
    /// on every derived value, not only on values the rule already flagged.</summary>
    private static PdfDocument BuildForbiddenUConventionDocument()
    {
        var doc = new PdfDocument();
        doc.AddObject(31, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(
            "/CIDInit /ProcSet findresource begin\n"
            + "1 begincodespacerange\n<00> <FF>\nendcodespacerange\n"
            + "1 beginbfchar\n<42> <FFFE>\nendbfchar\n"
            + "endcmap\nend\nend")));
        doc.AddObject(30, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("CustomUConventionFont"),
            [N("Encoding")] = new PdfDictionary
            {
                [N("BaseEncoding")] = N("WinAnsiEncoding"),
                [N("Differences")] = new PdfArray(new PdfInteger(0x41), N("uFFFF")),
            },
            [N("FirstChar")] = new PdfInteger(65),
            [N("LastChar")] = new PdfInteger(66),
            [N("Widths")] = new PdfArray(new PdfInteger(722), new PdfInteger(667)),
            [N("ToUnicode")] = Ref(31),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf (AB) Tj ET")));
        AddSinglePageCatalog(doc, font: 30);
        return doc;
    }

    /// <summary>A Type1 font (object 30) with a PARTIAL /ToUnicode CMap (object 31) — routine in
    /// subset fonts. /Differences gives BOTH drawn codes non-AGL glyph names ("g41", "g42"), so
    /// neither can be freshly re-derived from the encoding alone. Only code 'A' (0x41) is covered by
    /// the existing CMap (correctly, to "A"); code 'B' (0x42) is not. Fires pdfa2u-tounicode on 0x42
    /// only — HasReliableUnicode returns true for 0x41 purely because it already has a /ToUnicode
    /// entry (checked before the glyph-name path), regardless of whether that glyph name is
    /// AGL-derivable.</summary>
    private static PdfDocument BuildPartialCMapDocument()
    {
        var doc = new PdfDocument();
        doc.AddObject(31, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(
            "/CIDInit /ProcSet findresource begin\n"
            + "1 begincodespacerange\n<00> <FF>\nendcodespacerange\n"
            + "1 beginbfchar\n<41> <0041>\nendbfchar\n"
            + "endcmap\nend\nend")));
        doc.AddObject(30, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("PartialCMapFont"),
            [N("Encoding")] = new PdfDictionary
            {
                [N("BaseEncoding")] = N("WinAnsiEncoding"),
                [N("Differences")] = new PdfArray(new PdfInteger(0x41), N("g41"), N("g42")),
            },
            [N("FirstChar")] = new PdfInteger(65),
            [N("LastChar")] = new PdfInteger(66),
            [N("Widths")] = new PdfArray(new PdfInteger(722), new PdfInteger(667)),
            [N("ToUnicode")] = Ref(31),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf (AB) Tj ET")));
        AddSinglePageCatalog(doc, font: 30);
        return doc;
    }

    /// <summary>A Type0 font (object 20, indirect) whose /DescendantFonts[0] is a CIDFontType2
    /// dictionary embedded DIRECTLY (not by reference) with Ordering "Identity" and no /ToUnicode -
    /// mirrors <c>FontInventoryTests.BuildDirectDescendantType0Document</c>. Fires pdfa2u-tounicode
    /// with Finding.ObjectNumber = 20 (the Type0 wrapper, which IS indirect); the direct descendant
    /// leaves FontInventoryEntry.IsAddressable false.</summary>
    private static PdfDocument BuildDirectDescendantType0Document()
    {
        var directDescendant = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("CIDFontX"),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = new PdfString(Encoding.ASCII.GetBytes("Adobe")),
                [N("Ordering")] = new PdfString(Encoding.ASCII.GetBytes("Identity")),
                [N("Supplement")] = new PdfInteger(0),
            },
            [N("FontDescriptor")] = new PdfDictionary
            {
                [N("Type")] = N("FontDescriptor"),
                [N("FontName")] = N("CIDFontX"),
                [N("FontFile2")] = Ref(22),
            },
        };
        Assert.False(directDescendant.IsIndirect); // guards the fixture's own premise

        var doc = new PdfDocument();
        doc.AddObject(22, 0, new PdfStream(new PdfDictionary { [N("Length1")] = new PdfInteger(0) }, []));
        doc.AddObject(20, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("CIDFontX"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(directDescendant),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf <0001> Tj ET")));
        AddSinglePageCatalog(doc, font: 20);
        return doc;
    }

    /// <summary>Two Type0 wrappers, BOTH embedded DIRECTLY (non-indirect) in the page's /Font
    /// resources — so both get FontId(0) as their <c>Id</c> — each over its OWN INDIRECT descendant
    /// CIDFont (objects 21 and 41, distinct). Neither wrapper is ever registered via
    /// <c>doc.AddObject</c>; only their descendants are.</summary>
    private static PdfDocument BuildTwoDirectType0WrappersDocument()
    {
        var doc = new PdfDocument();
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("CIDFontA"),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = new PdfString(Encoding.ASCII.GetBytes("Adobe")),
                [N("Ordering")] = new PdfString(Encoding.ASCII.GetBytes("Identity")),
                [N("Supplement")] = new PdfInteger(0),
            },
            [N("FontDescriptor")] = new PdfDictionary
            {
                [N("Type")] = N("FontDescriptor"), [N("FontName")] = N("CIDFontA"),
            },
        });
        doc.AddObject(41, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("CIDFontB"),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = new PdfString(Encoding.ASCII.GetBytes("Adobe")),
                [N("Ordering")] = new PdfString(Encoding.ASCII.GetBytes("Identity")),
                [N("Supplement")] = new PdfInteger(0),
            },
            [N("FontDescriptor")] = new PdfDictionary
            {
                [N("Type")] = N("FontDescriptor"), [N("FontName")] = N("CIDFontB"),
            },
        });

        var directWrapperA = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("CIDFontA"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(21)),
        };
        var directWrapperB = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("CIDFontB"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(41)),
        };
        Assert.False(directWrapperA.IsIndirect); // guards the fixture's own premise
        Assert.False(directWrapperB.IsIndirect);

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 12 Tf <0001> Tj /F1 12 Tf <0001> Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F0")] = directWrapperA, [N("F1")] = directWrapperB },
            },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    /// <summary>A single Type1 font (object 30), WinAnsiEncoding, drawing only 'A' - a code whose
    /// glyph name ("A") resolves through the Adobe Glyph List, so pdfa2u-tounicode never fires (and
    /// there is no /ToUnicode entry for pdfa2u-tounicode-values to object to either).</summary>
    private static PdfDocument BuildFullyProvableNoToUnicodeDocument()
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("Helvetica"),
            [N("Encoding")] = N("WinAnsiEncoding"),
            [N("FirstChar")] = new PdfInteger(65),
            [N("LastChar")] = new PdfInteger(65),
            [N("Widths")] = new PdfArray(new PdfInteger(722)),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET")));
        AddSinglePageCatalog(doc, font: 30);
        return doc;
    }

    private static void AddSinglePageCatalog(PdfDocument doc, int font)
    {
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(font) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
    }
}
