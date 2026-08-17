using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Content;
using PdfLibrary.Editing;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>
/// F-4b Task 10 (spec 2026-08-17-f4b-notdef-program-replacement, §3 gate 2): the layout-invariance
/// gate for composite <c>.notdef</c> program replacement — mirrors
/// <see cref="EmbedProgramRoundTripTests.Embedding_does_not_move_the_text"/> (F-2's own non-regression
/// gate) for <see cref="PdfDocumentEditor.ReplaceCompositeProgram"/>: propose -&gt; apply -&gt; save
/// -&gt; reload, then fragment COUNT and every fragment's Text/X/Y/Width equal to precision 4. A whole-
/// face swap changes every glyph's SHAPE (that is the entire point of the remediation) but must never
/// change WHERE a glyph sits or what code point it decodes to — this gate proves the "never" half; the
/// render pairs this task also produces are the visual evidence for the changed-shapes half.
///
/// <para>Fixture-level fact uses <see cref="ReplaceProgramFixtures.DeadCid2Doc"/> — Task 5/6's shared
/// fixture, propose/apply pattern copied verbatim from <see cref="ReplaceProgramApplyTests"/>. The
/// LocalOnly Theory repeats the same assertion against 2 of Task 9's measured "fully closing" corpus
/// docs (<c>FontProgramReplaceCorpusTests.Fully_closing_documents_lose_every_notdef_finding_without_raising_width</c>),
/// on whichever page(s) <see cref="FontInventory"/> reports the replaced composite font is actually
/// used on (<c>PagesUsedOn</c>) — not page 0 by assumption, since <c>Finding.PageIndex</c> is always
/// null for <c>font-program</c> findings (<c>FontProgramRule.Make</c> only ever sets <c>ObjectNumber</c>).</para>
///
/// <para>Per the ambiguity resolution: a moved fragment here is a compose-step defect to report
/// BLOCKED with the numbers — never a precision to loosen.</para>
/// </summary>
public sealed class ReplaceProgramLayoutTests
{
    private const string CorpusVariable = "PDFLIBRARY_LOCAL708_CORPUS";
    private const string DefaultCorpus = @"D:\PdfCorpora\real-world\local-708";

    private const string CcMainCorpusVariable = "PDFLIBRARY_CCMAIN_CORPUS";
    private const string CcMainDefaultCorpus = @"D:\PdfCorpora\real-world\cc-main-2021-31-sample";

    // Corpus resolution copied verbatim from FontProgramReplaceCorpusTests.Corpus()/CcMainCorpus().
    private static string? Corpus()
    {
        string root = System.Environment.GetEnvironmentVariable(CorpusVariable) ?? DefaultCorpus;
        return Directory.Exists(root) ? root : null;
    }

    private static string? CcMainCorpus()
    {
        string root = System.Environment.GetEnvironmentVariable(CcMainCorpusVariable) ?? CcMainDefaultCorpus;
        return Directory.Exists(root) ? root : null;
    }

    private static void AssertSameGeometry(IReadOnlyList<TextFragment> before, IReadOnlyList<TextFragment> after)
    {
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
    public void A_replacement_does_not_move_any_text_fragment()
    {
        PdfDocument doc = ReplaceProgramFixtures.DeadCid2Doc();

        List<TextFragment> before = doc.GetPage(0)!.ExtractTextWithFragments().Fragments;
        Assert.NotEmpty(before);

        var provider = new StubFontProvider(ReplaceProgramFixtures.LiberationSansBytes());
        FontRemediationProposal result =
            ReplaceProgramFixtures.Planner(provider).Propose(doc, [("font-program", 1)]);
        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));

        using PdfDocumentEditor editor = doc.Edit();
        editor.ReplaceCompositeProgram(proposal);
        var ms = new MemoryStream();
        editor.Save(ms);
        ms.Position = 0;

        using PdfDocument reloaded = PdfDocument.Load(ms);
        List<TextFragment> after = reloaded.GetPage(0)!.ExtractTextWithFragments().Fragments;

        AssertSameGeometry(before, after);
    }

    /// <summary>
    /// note §6 rows 1, 3 (Task 9's measured "fully closing" set): one SCV CID0 doc and the
    /// issue-34 cc-main reproducer. Same propose/apply composition
    /// <c>FontProgramReplaceCorpusTests.ProposeFor</c>/<c>ApplyAndRecheck</c> uses
    /// (<see cref="EmbedProgramRoundTripTests.DeterministicFonts"/>), against a temp copy — the
    /// corpus root is READ-ONLY, never opened for write.
    /// </summary>
    [Trait("Category", "LocalOnly")]
    [Theory]
    [InlineData("local", "SCV~us~en~file=N0088673.pdf~gen~ref.pdf")]
    [InlineData("ccmain", "0000_0000024.pdf")]
    public void A_replaced_corpus_document_keeps_its_text_geometry(string corpus, string file)
    {
        string? root = corpus == "local" ? Corpus() : CcMainCorpus();
        string defaultPath = corpus == "local" ? DefaultCorpus : CcMainDefaultCorpus;
        Assert.SkipWhen(root is null, $"corpus not present at {defaultPath} (LocalOnly)");

        string path = Path.Combine(root!, file);
        PreflightResult before = Preflighter.Check(path, ConformanceProfile.PdfA2b);

        List<ReplaceProgramProposal> replacements;
        List<int> pageIndices;
        using (PdfDocument doc = PdfDocument.Load(path))
        {
            var planner = new FontRemediationPlanner(EmbedProgramRoundTripTests.DeterministicFonts);
            FontRemediationProposal proposed = planner.Propose(doc,
                before.Findings.Where(f => f.RuleId == "font-program" && f.ObjectNumber is not null)
                    .Select(f => (f.RuleId, f.ObjectNumber!.Value)));
            replacements = proposed.Fonts.OfType<ReplaceProgramProposal>().ToList();
            Assert.NotEmpty(replacements);

            // font-program findings never carry a PageIndex (FontProgramRule.Make sets ObjectNumber
            // only) — FontInventory's own PagesUsedOn, keyed off the SAME logical-font object number
            // ReplaceProgramProposal.CompositeFont names, is the real answer to "which page(s) use
            // this font".
            IReadOnlyList<FontInventoryEntry> inventory = FontInventory.Read(doc);
            pageIndices = replacements
                .SelectMany(r => inventory.Where(e => e.Id.ObjectNumber == r.CompositeFont.ObjectNumber))
                .SelectMany(e => e.PagesUsedOn)
                .Distinct()
                .OrderBy(p => p)
                .ToList();
        }
        Assert.NotEmpty(pageIndices);
        int pageIndex = pageIndices[0];

        List<TextFragment> beforeFragments;
        using (PdfDocument doc = PdfDocument.Load(path))
            beforeFragments = doc.GetPage(pageIndex)!.ExtractTextWithFragments().Fragments;
        Assert.NotEmpty(beforeFragments);

        string temp = Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(path))
            using (PdfDocumentEditor editor = doc.Edit())
            {
                foreach (ReplaceProgramProposal replacement in replacements)
                    editor.ReplaceCompositeProgram(replacement);
                using FileStream fs = File.Create(temp);
                editor.Save(fs);
            }

            List<TextFragment> afterFragments;
            using (PdfDocument reloaded = PdfDocument.Load(temp))
                afterFragments = reloaded.GetPage(pageIndex)!.ExtractTextWithFragments().Fragments;

            AssertSameGeometry(beforeFragments, afterFragments);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
