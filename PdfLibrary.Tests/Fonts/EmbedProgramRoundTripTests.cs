using System.Text;
using FontParser;
using PdfLibrary.Builder;
using PdfLibrary.Conformance;
using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// F-2's definition of done (design §11): stage the fix, apply it, re-run preflight, assert the
/// finding is gone — and assert the text did not move while doing it.
/// </summary>
public class EmbedProgramRoundTripTests
{
    /// <summary>
    /// A one-page document drawing text in Helvetica with no embedded program. PdfDocumentBuilder
    /// emits a bare /Type1 /BaseFont /Helvetica dict with no /FontFile when LoadFont was never
    /// called, which is exactly the defect font-embedded fires on — so no corpus file is needed
    /// and this runs identically on a dev machine and a Linux CI runner.
    /// </summary>
    private static byte[] UnembeddedDocument() =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Wave the quick brown fox 12345", 72, 700).Font("Helvetica", 18))
            .ToByteArray();

    private static IReadOnlyList<Finding> Preflight(PdfDocument document) =>
        Preflighter.Check(document, ConformanceProfile.PdfA2b).Findings;

    /// <summary>
    /// Resolves the sole embed proposal for a <c>font-embedded</c> finding, or skips — but with the
    /// planner's own reason, not a bare "resolved to null". Fix round 1 finding: folding both "no
    /// substitute installed" and "the planner declined for a reason worth hearing" into the same
    /// <c>SingleOrDefault() is null</c> skip means a locator regression turns every downstream gate
    /// green-by-skipping with nothing distinguishing it from routine machine variance. Asserting
    /// there is no <see cref="DeclineProposal"/> before skipping — and naming its <c>Reason</c> in
    /// the skip message when there is one — keeps "not installed" and "declined, and here is why"
    /// visibly different outcomes.
    /// </summary>
    private static EmbedProposal ResolveEmbedProposal(PdfDocument document, int objectNumber)
    {
        FontRemediationProposal proposal = new FontRemediationPlanner(SystemFontLocator.Default)
            .Propose(document, [("font-embedded", objectNumber)]);

        FontProposal single = Assert.Single(proposal.Fonts);
        if (single is DeclineProposal decline)
        {
            Assert.Skip(
                $"The planner declined to propose an embed for rule '{decline.RuleId}' rather than "
                + $"failing to resolve a substitute: {decline.Reason}");
        }

        return Assert.IsType<EmbedProposal>(single);
    }

    [Fact]
    public void The_fixture_actually_fails_the_rule()
    {
        // Validate the fixture before trusting it. A synthetic fixture that fails for the wrong
        // reason — or does not fail at all — proves nothing, and this project has been caught by
        // exactly that before (design §11, "Fixtures").
        using PdfDocument document = PdfDocument.Load(new MemoryStream(UnembeddedDocument()));

        Assert.Contains(Preflight(document), f => f.RuleId == "font-embedded");
    }

    [Fact]
    public void Staging_and_applying_the_embed_closes_the_finding()
    {
        byte[] original = UnembeddedDocument();
        using var output = new MemoryStream();

        using (PdfDocument document = PdfDocument.Load(new MemoryStream(original)))
        {
            Finding finding = Preflight(document).First(f => f.RuleId == "font-embedded");
            Assert.NotNull(finding.ObjectNumber);

            EmbedProposal embed = ResolveEmbedProposal(document, finding.ObjectNumber!.Value);

            using var editor = new PdfDocumentEditor(document);
            editor.EmbedProgram(embed.Font, embed.Program, embed.Format);
            editor.Save(output);
        }

        output.Position = 0;
        using PdfDocument after = PdfDocument.Load(output);
        Assert.DoesNotContain(Preflight(after), f => f.RuleId == "font-embedded");
        AssertPermittedTable124Pair(after);
    }

    /// <summary>
    /// Every simple font in <paramref name="document"/> declares a <c>/Subtype</c> that ISO 32000-2
    /// §9.9.1 Table 124 actually pairs with the font-file key on its descriptor.
    ///
    /// <para>THIS is the assertion whose absence let the original defect through: preflight's
    /// <c>font-embedded</c> rule only asks whether SOME program is present, so a <c>/FontFile2</c>
    /// written into a <c>/Type1</c> descriptor closed the finding while producing a combination the
    /// table permits nowhere. Written out from the table's own text rather than by calling the
    /// production reconciliation, so it can disagree with it.</para>
    /// </summary>
    private static void AssertPermittedTable124Pair(PdfDocument document)
    {
        var checkedAny = false;

        foreach (FontInventoryEntry entry in FontInventory.Read(document))
        {
            int number = (entry.ProgramHolderId ?? entry.Id).ObjectNumber;
            if (document.GetObject(number) is not PdfDictionary font) continue;

            var subtype = (Deref(document, font.Get("Subtype")) as PdfName)?.Value;
            if (subtype is null or "Type0" or "CIDFontType0" or "CIDFontType2") continue; // not simple

            if (Deref(document, font.Get("FontDescriptor")) is not PdfDictionary descriptor) continue;

            if (descriptor.Get("FontFile2") is not null)
            {
                // "FontFile2 … may appear in the font descriptor for a TrueType font dictionary."
                // ACCEPTED LIMITATION: this does not discriminate the /Type1 -> /TrueType rewrite
                // itself on a machine whose Helvetica substitute resolves CFF-flavoured (plausibly
                // including CI) — it only checks the pair is consistent, not which rewrite produced
                // it. The deterministic pin for that rewrite is the editor unit test
                // A_TrueType_program_in_a_Type1_dictionary_rewrites_the_dictionary_subtype, which
                // passes FontProgramFormat.TrueType explicitly.
                Assert.Equal("TrueType", subtype);
                checkedAny = true;
            }

            if (descriptor.Get("FontFile") is not null)
            {
                // "FontFile … a Type 1 font program" — a Type1 or MMType1 font dictionary.
                Assert.Contains(subtype, new[] { "Type1", "MMType1" });
                checkedAny = true;
            }

            if (Deref(document, descriptor.Get("FontFile3")) is not PdfStream stream) continue;

            string? streamSubtype = (stream.Dictionary.Get("Subtype") as PdfName)?.Value;
            switch (streamSubtype)
            {
                case "Type1C":
                    Assert.Contains(subtype, new[] { "Type1", "MMType1" });
                    break;
                case "CIDFontType0C":
                    Assert.Fail($"/CIDFontType0C is permitted only for a CIDFontType0 CIDFont, not /{subtype}.");
                    break;
                case "OpenType":
                    // "a TrueType font dictionary … if the program contains a 'glyf' table; a Type1
                    // font dictionary … if it contains a 'CFF ' table without CIDFont operators."
                    // Note /MMType1 does NOT appear in this row (unlike /FontFile and /FontFile3
                    // /Type1C above, which explicitly say "a Type1 or MMType1 font dictionary") — so
                    // a CFF-flavoured OpenType program permits only /Type1, never /MMType1. Read
                    // straight off the embedded bytes, independently of what wrote them.
                    Assert.Contains(subtype, new SfntFont(stream.Data, 0).TableTags.Contains("glyf")
                        ? new[] { "TrueType" }
                        : new[] { "Type1" });
                    break;
                default:
                    Assert.Fail($"/FontFile3 carries an unrecognised /Subtype /{streamSubtype}.");
                    break;
            }
            checkedAny = true;
        }

        Assert.True(checkedAny, "No embedded simple font was found to check — the gate proved nothing.");
    }

    private static PdfObject? Deref(PdfDocument document, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? document.GetObject(reference.ObjectNumber) : obj;

    [Fact]
    public void Embedding_does_not_move_the_text()
    {
        // Design §11's non-regression gate, pinning §5.1. A test that only checked conformance
        // would pass a fix that embedded a font and quietly reflowed the page.
        //
        // IMPORTANT SCOPE NOTE (fix round 1): this fixture has no /Widths at all, so EmbedProgram
        // always takes the DERIVE branch (PdfDocumentEditor.Fonts.cs WriteDerivedWidths) and writes
        // /Widths FROM the very program it just embedded. Both the /Widths-first and program-first
        // orderings therefore terminate in the same lookup for ordinary Latin glyphs, so this test
        // alone cannot tell "the precedence fix is in effect" from "it was reverted" — it only
        // proves the /Widths-ABSENT path is internally self-consistent. The path the original defect
        // actually bit — a substitute overwriting an ALREADY-correct /Widths — is covered separately
        // by Embedding_does_not_move_the_text_when_Widths_is_already_present below, which is the
        // test that can actually discriminate the two orderings.
        byte[] original = UnembeddedDocument();

        List<TextFragment> before;
        using (PdfDocument document = PdfDocument.Load(new MemoryStream(original)))
            before = document.GetPage(0)!.ExtractTextWithFragments().Fragments;

        using var output = new MemoryStream();
        using (PdfDocument document = PdfDocument.Load(new MemoryStream(original)))
        {
            Finding finding = Preflight(document).First(f => f.RuleId == "font-embedded");
            EmbedProposal embed = ResolveEmbedProposal(document, finding.ObjectNumber!.Value);

            using var editor = new PdfDocumentEditor(document);
            editor.EmbedProgram(embed.Font, embed.Program, embed.Format);
            editor.Save(output);
        }

        output.Position = 0;
        using PdfDocument afterDocument = PdfDocument.Load(output);
        List<TextFragment> after = afterDocument.GetPage(0)!.ExtractTextWithFragments().Fragments;

        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].Text, after[i].Text);
            Assert.Equal(before[i].X, after[i].X, precision: 4);
            Assert.Equal(before[i].Y, after[i].Y, precision: 4);
            Assert.Equal(before[i].Width, after[i].Width, precision: 4);
        }
    }

    [Fact]
    public void Embedding_does_not_move_the_text_when_Widths_is_already_present()
    {
        // Fix round 1 Critical: this is the gate the task is named for. Unlike
        // Embedding_does_not_move_the_text above, this fixture's font dict ALREADY carries a
        // /Widths entry (EmbeddedWidthsFixture.Build), so EmbedProgram takes the "never touched when
        // present" branch (design §5.1, ISO 32000-2 §9.2.4 NOTE 2/3) rather than deriving from the
        // program. The fixture builder asserts its declared width provably DIFFERS from the resolved
        // substitute's own advance, so — unlike the /Widths-absent fixture — the two precedence
        // orderings genuinely disagree here: a processor reading /Widths first reports one number: a
        // processor that fell through to the embedded program (the reverted defect) would report
        // another. "Before" (no program embedded yet) can only ever come from /Widths; if "after"
        // (program now present) still equals "before", the fix is reading /Widths first for real —
        // not simply because the two routes happened to agree.
        byte[] original = EmbeddedWidthsFixture.Build(out int declaredWidth);

        // Sanity-check the fixture's own wiring: before any embedding, GetCharacterWidth must
        // already report the declared /Widths value (there is no program yet to read from, so this
        // also confirms FirstChar/LastChar/Widths line up before the round trip is attempted).
        using (PdfDocument checkDocument = PdfDocument.Load(new MemoryStream(original)))
        {
            var fontDict = (PdfDictionary)checkDocument.GetObject(EmbeddedWidthsFixture.FontObjectNumber)!;
            PdfFont? font = PdfFont.Create(fontDict, checkDocument);
            Assert.NotNull(font);
            Assert.Equal(declaredWidth, font!.GetCharacterWidth(EmbeddedWidthsFixture.Code), precision: 3);
        }

        List<TextFragment> before;
        using (PdfDocument document = PdfDocument.Load(new MemoryStream(original)))
            before = document.GetPage(0)!.ExtractTextWithFragments().Fragments;
        Assert.NotEmpty(before);

        using var output = new MemoryStream();
        using (PdfDocument document = PdfDocument.Load(new MemoryStream(original)))
        {
            Finding finding = Preflight(document).First(f => f.RuleId == "font-embedded");
            EmbedProposal embed = ResolveEmbedProposal(document, finding.ObjectNumber!.Value);

            using var editor = new PdfDocumentEditor(document);
            editor.EmbedProgram(embed.Font, embed.Program, embed.Format);
            editor.Save(output);
        }

        output.Position = 0;
        using PdfDocument afterDocument = PdfDocument.Load(output);
        List<TextFragment> after = afterDocument.GetPage(0)!.ExtractTextWithFragments().Fragments;

        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].Text, after[i].Text);
            Assert.Equal(before[i].X, after[i].X, precision: 4);
            Assert.Equal(before[i].Y, after[i].Y, precision: 4);
            Assert.Equal(before[i].Width, after[i].Width, precision: 4);
        }

        // Requirement 6: EmbedProgram must leave the pre-existing /Widths array's VALUES untouched —
        // compared element-by-element, not by reference (the array object itself may legitimately be
        // re-serialized across a save/reload).
        var afterFontDict = (PdfDictionary)afterDocument.GetObject(EmbeddedWidthsFixture.FontObjectNumber)!;
        var afterWidths = Assert.IsType<PdfArray>(afterFontDict.Get("Widths"));
        int[] widthValues = afterWidths.Select(w => ((PdfInteger)w).Value).ToArray();
        Assert.Equal([declaredWidth], widthValues);
    }

    [Fact]
    public void The_embedded_program_is_a_full_program_carrying_no_subset_tag()
    {
        // §1.1: F-2 embeds the FULL program, which produces no subset tag, which is what keeps
        // F-2's output outside F-3's scope by construction. A subset tag here would mean someone
        // added subsetting to the embed path and silently coupled the two increments.
        byte[] original = UnembeddedDocument();
        using var output = new MemoryStream();

        using (PdfDocument document = PdfDocument.Load(new MemoryStream(original)))
        {
            Finding finding = Preflight(document).First(f => f.RuleId == "font-embedded");
            EmbedProposal embed = ResolveEmbedProposal(document, finding.ObjectNumber!.Value);

            using var editor = new PdfDocumentEditor(document);
            editor.EmbedProgram(embed.Font, embed.Program, embed.Format);
            editor.Save(output);
        }

        output.Position = 0;
        using PdfDocument after = PdfDocument.Load(output);
        Assert.All(FontInventory.Read(after), entry => Assert.Null(entry.SubsetTag));
    }
}

/// <summary>
/// Hand-built fixture for the /Widths-PRESENT gate (fix round 1 Critical finding). Follows the
/// established hand-built-dictionary pattern (<c>PdfDocumentEditorEmbedProgramTests</c>,
/// <c>WidthPrecedenceTests</c>'s <c>WidthFixtures</c>) because <c>PdfDocumentBuilder</c> cannot
/// express a pre-existing <c>/Widths</c> array — it only ever emits a bare Standard-14 dict.
///
/// <para>The declared width for 'A' is deliberately set to PROVABLY diverge from the resolved
/// Helvetica substitute's own advance for 'A' (asserted in <see cref="Build"/>, not assumed) — the
/// entire point of this fixture, per <c>WidthFixtures.TrueTypeWithWidthsAndProgram</c>'s own
/// precedent: if the two agreed, a test built on this fixture would prove nothing either.</para>
/// </summary>
internal static class EmbeddedWidthsFixture
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    /// <summary>The single character code this fixture declares /Widths for and draws on the page.</summary>
    internal const int Code = 'A';

    /// <summary>The font dictionary's object number — fixed, matching the repo's fixture convention
    /// of literal object numbers per fixture (<c>EmbedFixtures</c>, <c>WidthFixtures</c>).</summary>
    internal const int FontObjectNumber = 30;

    private const int DescriptorObjectNumber = 31;
    private const int ContentObjectNumber = 11;
    private const int PageObjectNumber = 3;
    private const int PagesObjectNumber = 2;
    private const int CatalogObjectNumber = 1;

    /// <summary>Resolves the same substitute <see cref="FontRemediationPlanner"/> will independently
    /// resolve for this fixture's <c>/BaseFont /Helvetica</c> (deterministic per machine/install), or
    /// skips — matching the convention in <c>PdfDocumentEditorEmbedProgramTests.ArialProgram</c> and
    /// <c>FontDescriptorMetricsTests</c>.</summary>
    private static (byte[] Program, FontProgramFormat Format) ResolveSubstitute()
    {
        FontMatch? match = SystemFontLocator.Default.Resolve(new FontRequest("Helvetica", Bold: false, Italic: false));
        Assert.SkipWhen(match is null,
            "No substitute for Helvetica resolved on this machine; the /Widths-present gate cannot run.");
        ClassifiedProgram? classified = FontProgramClassifier.Classify(match!.Data, match.FaceIndex);
        Assert.SkipWhen(classified is null,
            "The resolved substitute for Helvetica is not in a format Pellucid can embed.");
        return (classified!.Program, classified.Format);
    }

    /// <summary>The resolved substitute's own advance for <see cref="Code"/>, in PDF 1000-unit space
    /// — computed via the exact same <see cref="EmbeddedFontMetrics"/> construction
    /// <c>PdfDocumentEditor.WriteDerivedWidths</c> uses, so this cannot disagree with what the DERIVE
    /// branch would have written had /Widths been absent.</summary>
    private static double ResolvedProgramAdvance(out byte[] program, out FontProgramFormat format)
    {
        (program, format) = ResolveSubstitute();
        EmbeddedFontMetrics metrics = format == FontProgramFormat.Type1
            ? new EmbeddedFontMetrics(program, length1: 0, length2: 0, length3: 0)
            : new EmbeddedFontMetrics(program);
        Assert.True(metrics.IsValid, "Resolved Helvetica substitute did not parse as a valid font program.");
        ushort raw = metrics.GetCharacterAdvanceWidth((ushort)Code);
        Assert.True(raw > 0, "Resolved Helvetica substitute has no usable advance for 'A'.");
        return raw * 1000.0 / metrics.UnitsPerEm;
    }

    /// <summary>
    /// A one-page document whose Helvetica font dict (object <see cref="FontObjectNumber"/>) already
    /// carries a <c>/Widths</c> entry for <see cref="Code"/> that is deliberately, provably wrong —
    /// set to a value that differs from the resolved substitute's own advance by construction, and
    /// asserted to differ before the fixture is handed back. The page draws "AAA" in that font so
    /// <c>ExtractTextWithFragments</c> has real glyphs to report on.
    /// </summary>
    internal static byte[] Build(out int declaredWidth)
    {
        double programAdvance = ResolvedProgramAdvance(out _, out _);
        // +300 keeps this comfortably outside plausible rounding/scaling noise between UnitsPerEm
        // conventions, and is trivially unambiguous either way since the assertion below re-checks it.
        declaredWidth = (int)Math.Round(programAdvance) + 300;
        Assert.NotEqual(programAdvance, declaredWidth, 3);

        var doc = new PdfDocument();
        doc.AddObject(DescriptorObjectNumber, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("Helvetica"),
            [N("Flags")] = new PdfInteger(32),
        });
        doc.AddObject(FontObjectNumber, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("Helvetica"),
            [N("Encoding")] = N("WinAnsiEncoding"),
            [N("FirstChar")] = new PdfInteger(Code),
            [N("LastChar")] = new PdfInteger(Code),
            [N("Widths")] = new PdfArray(new PdfInteger(declaredWidth)),
            [N("FontDescriptor")] = Ref(DescriptorObjectNumber),
        });
        doc.AddObject(ContentObjectNumber, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 18 Tf 72 700 Td (AAA) Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(PagesObjectNumber),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(ContentObjectNumber),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(FontObjectNumber) } },
        };
        doc.AddObject(PageObjectNumber, 0, page);
        doc.AddObject(PagesObjectNumber, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(PageObjectNumber)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(CatalogObjectNumber, 0, new PdfDictionary
        {
            [N("Type")] = N("Catalog"), [N("Pages")] = Ref(PagesObjectNumber),
        });
        doc.Trailer.Dictionary[N("Root")] = Ref(CatalogObjectNumber);

        using var buffer = new MemoryStream();
        doc.Save(buffer);
        return buffer.ToArray();
    }
}
