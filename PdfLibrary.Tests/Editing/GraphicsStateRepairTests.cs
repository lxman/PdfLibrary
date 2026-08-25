using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>Tests for <see cref="PdfDocumentEditor.PreviewGraphicsStateRepairs"/> and
/// <see cref="PdfDocumentEditor.RepairGraphicsState"/> -- the ISO 19005-2/3 6.2.5 extended-graphics-state
/// remediation (<see cref="ExtGStateRule"/>). One test per row of the classification table in
/// <c>docs/superpowers/specs/2026-08-24-graphics-state-and-optional-content-design.md</c> §4, plus the
/// two traps that table does not describe (the soft-mask <c>/TR</c>, and an ExtGState omitting
/// <c>/Type</c>) and a closure test tying every message <see cref="ExtGStateRule"/> can raise to a
/// deletion or a refusal.
///
/// <para><b>Every synthetic fixture here is validated against the real detector</b> --
/// <see cref="RuleMessages"/> runs <see cref="ExtGStateRule"/> over the same document the editor is
/// asked about, so a fixture that does not actually violate 6.2.5 cannot silently prove anything. Four
/// of the six repairable/refusable shapes (<c>/HTP</c>, a non-<c>Default</c> <c>/TR2</c>, and both
/// halftone refusals) have NO witness in the 708-document corpus, which is precisely why they get that
/// second check rather than the corpus one; the two that DO have witnesses are additionally pinned
/// against the real files in <see cref="GraphicsStateCorpusShapeTests"/>.</para></summary>
public class GraphicsStateRepairTests
{
    private static PdfName N(string s) => new(s);

    /// <summary>A bare, unwired document (no catalog or page tree -- neither is needed, because the
    /// classifier walks <c>_document.Objects.Values</c> directly and no test here saves or reloads)
    /// carrying <paramref name="objects"/> at the object numbers given, plus the editor over it.
    /// Mirrors <c>StreamFilterRepairTests.OneStream</c>'s convention: <c>new PdfDocument()</c> and
    /// <c>new PdfDocumentEditor(document)</c> rather than the file-loading path.</summary>
    private static PdfDocumentEditor EditorOver(params (int Number, PdfObject Object)[] objects)
    {
        var doc = new PdfDocument();
        foreach ((int number, PdfObject obj) in objects)
            doc.AddObject(number, 0, obj);
        return new PdfDocumentEditor(doc);
    }

    /// <summary>An ExtGState dictionary carrying <c>/Type /ExtGState</c> plus the entries given.</summary>
    private static PdfDictionary ExtGState(params (string Key, PdfObject Value)[] entries)
    {
        var gs = new PdfDictionary { [N("Type")] = N("ExtGState") };
        foreach ((string key, PdfObject value) in entries)
            gs[N(key)] = value;
        return gs;
    }

    /// <summary>A conforming Type 1 halftone: the three keys that actually DEFINE the screen
    /// (ISO 32000-1 Table 130 -- <c>Frequency</c>, <c>Angle</c>, <c>SpotFunction</c>) and nothing this
    /// repair touches. <paramref name="extra"/> adds the defect under test.</summary>
    private static PdfDictionary Halftone1(params (string Key, PdfObject Value)[] extra)
    {
        var ht = new PdfDictionary
        {
            [N("Type")] = N("Halftone"),
            [N("HalftoneType")] = new PdfInteger(1),
            [N("Frequency")] = new PdfInteger(60),
            [N("Angle")] = new PdfInteger(45),
            [N("SpotFunction")] = N("Round"),
        };
        foreach ((string key, PdfObject value) in extra)
            ht[N(key)] = value;
        return ht;
    }

    /// <summary>What <see cref="ExtGStateRule"/> -- the REAL detector, not a restatement of it --
    /// reports for this document under PDF/A-2b. Every fixture in this file is run through it, so a
    /// fixture that does not violate 6.2.5 fails its own test rather than quietly proving nothing.</summary>
    private static string[] RuleMessages(PdfDocumentEditor editor) =>
    [
        .. new ExtGStateRule()
            .Check(new ConformanceContext(editor.Document, ConformanceProfile.PdfA2b))
            .Select(f => f.Message),
    ];

    private static IReadOnlySet<int> Only(params int[] numbers) => new HashSet<int>(numbers);

    // ---- Repairable rows (spec §4, upper table) -------------------------------------------------

    [Fact]
    public void TR_Identity_is_a_candidate_and_the_repair_deletes_it()
    {
        PdfDictionary gs = ExtGState(("TR", N("Identity")));
        PdfDocumentEditor editor = EditorOver((7, gs));

        Assert.Contains(RuleMessages(editor), m => m.Contains("TR (transfer function)", StringComparison.Ordinal));

        GraphicsStateRepairPreview preview = editor.PreviewGraphicsStateRepairs();
        GraphicsStateRepairCandidate candidate = Assert.Single(preview.Candidates);
        Assert.Equal(7, candidate.ObjectNumber);
        Assert.Equal(["TR"], candidate.Keys);
        Assert.Empty(preview.Refused);

        GraphicsStateRepairReport report = editor.RepairGraphicsState();

        GraphicsStateRepair applied = Assert.Single(report.Applied);
        Assert.Equal(7, applied.ObjectNumber);
        Assert.Equal(["TR"], applied.DeletedKeys);
        Assert.Null(gs.Get("TR"));
        Assert.Empty(RuleMessages(editor));
    }

    [Fact]
    public void HTP_is_a_candidate_and_the_repair_deletes_it()
    {
        // No corpus witness: /HTP occurs in zero of the 708 documents (spec §1). Synthetic-only, so
        // the rule check above it is the whole of its validation.
        PdfDictionary gs = ExtGState(("HTP", new PdfArray(new PdfInteger(0), new PdfInteger(0))));
        PdfDocumentEditor editor = EditorOver((7, gs));

        Assert.Contains(RuleMessages(editor), m => m.Contains("HTP", StringComparison.Ordinal));

        Assert.Equal(["HTP"], Assert.Single(editor.PreviewGraphicsStateRepairs().Candidates).Keys);

        editor.RepairGraphicsState();

        Assert.Null(gs.Get("HTP"));
        Assert.Empty(RuleMessages(editor));
    }

    [Fact]
    public void A_TR2_other_than_Default_is_a_candidate_and_the_repair_deletes_it()
    {
        // Also no corpus witness: all 18 documents carrying /TR2 carry the name /Default (spec §1).
        PdfDictionary gs = ExtGState(("TR2", N("Foo")));
        PdfDocumentEditor editor = EditorOver((7, gs));

        Assert.Contains(RuleMessages(editor), m => m.Contains("TR2", StringComparison.Ordinal));

        Assert.Equal(["TR2"], Assert.Single(editor.PreviewGraphicsStateRepairs().Candidates).Keys);

        editor.RepairGraphicsState();

        Assert.Null(gs.Get("TR2"));
        Assert.Empty(RuleMessages(editor));
    }

    [Fact]
    public void A_TR2_of_Default_is_left_alone()
    {
        // 6.2.5 permits /TR2 /Default explicitly and the rule does not fire on it, so neither may the
        // repair: deleting a conforming key would be an edit nothing asked for.
        PdfDictionary gs = ExtGState(("TR2", N("Default")));
        PdfDocumentEditor editor = EditorOver((7, gs));

        Assert.Empty(RuleMessages(editor));
        Assert.Empty(editor.PreviewGraphicsStateRepairs().Candidates);
        Assert.Empty(editor.PreviewGraphicsStateRepairs().Refused);

        editor.RepairGraphicsState();

        Assert.Equal("Default", (gs.Get("TR2") as PdfName)?.Value);
    }

    [Fact]
    public void HalftoneName_is_deleted_and_the_screen_keys_are_untouched()
    {
        // The Faces.pdf / Standard.pdf shape: a Type 1 halftone whose only defect is the LABEL.
        PdfDictionary ht = Halftone1(("HalftoneName", PdfString.FromText("Default")));
        PdfDictionary gs = ExtGState(("HT", new PdfIndirectReference(31, 0)));
        PdfDocumentEditor editor = EditorOver((31, ht), (32, gs));

        Assert.Contains(RuleMessages(editor), m => m.Contains("HalftoneName", StringComparison.Ordinal));

        GraphicsStateRepairCandidate candidate =
            Assert.Single(editor.PreviewGraphicsStateRepairs().Candidates);
        // Keyed by the ExtGState, not the halftone -- the number ExtGStateRule puts on its Finding.
        Assert.Equal(32, candidate.ObjectNumber);
        Assert.Equal(["HalftoneName"], candidate.Keys);

        editor.RepairGraphicsState();

        Assert.Null(ht.Get("HalftoneName"));
        Assert.Equal(60, (ht.Get("Frequency") as PdfInteger)?.Value);
        Assert.Equal(45, (ht.Get("Angle") as PdfInteger)?.Value);
        Assert.Equal("Round", (ht.Get("SpotFunction") as PdfName)?.Value);
        Assert.Equal(1, (ht.Get("HalftoneType") as PdfInteger)?.Value);
        Assert.Empty(RuleMessages(editor));
    }

    [Fact]
    public void HalftoneName_on_a_halftone_STREAM_is_deleted_too()
    {
        // A Type 10/16 halftone is a stream; Table 130's keys live on its dictionary either way, and
        // ExtGStateRule resolves both shapes. HalftoneType 10 is itself a refusal (below), so this
        // fixture proves the two coexist: the label is deleted, the type is refused.
        var htDict = new PdfDictionary
        {
            [N("Type")] = N("Halftone"),
            [N("HalftoneType")] = new PdfInteger(10),
            [N("HalftoneName")] = PdfString.FromText("Screen"),
        };
        var ht = new PdfStream(htDict, " "u8.ToArray());
        PdfDictionary gs = ExtGState(("HT", new PdfIndirectReference(31, 0)));
        PdfDocumentEditor editor = EditorOver((31, ht), (32, gs));

        Assert.Equal(2, RuleMessages(editor).Length);

        GraphicsStateRepairPreview preview = editor.PreviewGraphicsStateRepairs();
        Assert.Equal(["HalftoneName"], Assert.Single(preview.Candidates).Keys);
        Assert.Contains("HalftoneType 10", Assert.Single(preview.Refused).Reason, StringComparison.Ordinal);

        editor.RepairGraphicsState();

        Assert.Null(htDict.Get("HalftoneName"));
        Assert.Equal(10, (htDict.Get("HalftoneType") as PdfInteger)?.Value); // refused, so untouched
    }

    // ---- Refusal rows (spec §4, lower table) ----------------------------------------------------

    [Fact]
    public void A_HalftoneType_outside_1_and_5_is_a_refusal_and_nothing_is_written()
    {
        PdfDictionary ht = Halftone1();
        ht[N("HalftoneType")] = new PdfInteger(3);
        PdfDictionary gs = ExtGState(("HT", new PdfIndirectReference(31, 0)));
        PdfDocumentEditor editor = EditorOver((31, ht), (32, gs));

        Assert.Contains(RuleMessages(editor), m => m.Contains("HalftoneType 3", StringComparison.Ordinal));

        GraphicsStateRepairPreview preview = editor.PreviewGraphicsStateRepairs();
        Assert.Empty(preview.Candidates);
        GraphicsStateRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(32, refusal.ObjectNumber);
        Assert.Contains("HalftoneType 3", refusal.Reason, StringComparison.Ordinal);

        GraphicsStateRepairReport report = editor.RepairGraphicsState();

        Assert.Empty(report.Applied);
        Assert.Contains("HalftoneType 3", Assert.Single(report.Refused).Reason, StringComparison.Ordinal);
        Assert.Equal(3, (ht.Get("HalftoneType") as PdfInteger)?.Value);
        Assert.Equal(5, ht.Count); // nothing added, nothing removed
    }

    [Fact]
    public void A_Type5_primary_component_carrying_a_TransferFunction_is_a_refusal()
    {
        PdfDictionary cyan = Halftone1(("TransferFunction", N("Identity")));
        var composite = new PdfDictionary
        {
            [N("Type")] = N("Halftone"),
            [N("HalftoneType")] = new PdfInteger(5),
            [N("Cyan")] = cyan,
            [N("Default")] = Halftone1(),
        };
        PdfDictionary gs = ExtGState(("HT", new PdfIndirectReference(31, 0)));
        PdfDocumentEditor editor = EditorOver((31, composite), (32, gs));

        Assert.Contains(RuleMessages(editor), m => m.Contains("'Cyan'", StringComparison.Ordinal));

        GraphicsStateRepairPreview preview = editor.PreviewGraphicsStateRepairs();
        Assert.Empty(preview.Candidates);
        GraphicsStateRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal(32, refusal.ObjectNumber);
        Assert.Contains("'Cyan'", refusal.Reason, StringComparison.Ordinal);

        editor.RepairGraphicsState();

        // Deleting a transfer function changes tone reproduction; the refusal exists so it stays.
        Assert.NotNull(cyan.Get("TransferFunction"));
    }

    [Fact]
    public void A_Type5_non_primary_component_missing_its_TransferFunction_is_a_refusal()
    {
        // Gray is non-primary per the veraPDF 6.2.5-6 split ExtGStateRule.PrimaryColourants encodes,
        // so it REQUIRES a TransferFunction. Pellucid cannot invent a transfer curve.
        PdfDictionary gray = Halftone1();
        var composite = new PdfDictionary
        {
            [N("Type")] = N("Halftone"),
            [N("HalftoneType")] = new PdfInteger(5),
            [N("Gray")] = gray,
            [N("Default")] = Halftone1(),
        };
        PdfDictionary gs = ExtGState(("HT", new PdfIndirectReference(31, 0)));
        PdfDocumentEditor editor = EditorOver((31, composite), (32, gs));

        Assert.Contains(RuleMessages(editor), m => m.Contains("missing the ", StringComparison.Ordinal));

        GraphicsStateRepairPreview preview = editor.PreviewGraphicsStateRepairs();
        Assert.Empty(preview.Candidates);
        Assert.Contains("'Gray'", Assert.Single(preview.Refused).Reason, StringComparison.Ordinal);

        editor.RepairGraphicsState();

        Assert.Null(gray.Get("TransferFunction")); // never invented
    }

    [Fact]
    public void A_standalone_halftone_carrying_a_TransferFunction_is_a_refusal()
    {
        // The colorantName-is-null arm of Table 130: a halftone named straight from /HT is treated as
        // primary, so a TransferFunction on it is forbidden -- and deleting one changes output.
        PdfDictionary ht = Halftone1(("TransferFunction", N("Identity")));
        PdfDictionary gs = ExtGState(("HT", new PdfIndirectReference(31, 0)));
        PdfDocumentEditor editor = EditorOver((31, ht), (32, gs));

        Assert.Contains(RuleMessages(editor), m => m.Contains("TransferFunction", StringComparison.Ordinal));

        GraphicsStateRepairPreview preview = editor.PreviewGraphicsStateRepairs();
        Assert.Empty(preview.Candidates);
        Assert.Contains("TransferFunction", Assert.Single(preview.Refused).Reason, StringComparison.Ordinal);

        editor.RepairGraphicsState();

        Assert.NotNull(ht.Get("TransferFunction"));
    }

    // ---- The two traps (spec §3.2, §3.3) --------------------------------------------------------

    [Fact]
    public void A_soft_mask_TR_survives_the_repair()
    {
        // Spec §3.2. The soft-mask dictionary's /TR (ISO 32000-1 Table 144) is LEGAL under PDF/A and
        // our renderer implements it (ExtGStateApplier stores it on the SoftMask). A repair written as
        // a recursive "strip every /TR" would silently change transparency output. The ExtGState here
        // ALSO carries its own /TR, so this fixture proves the two are told apart rather than proving
        // nothing happened at all.
        var softMask = new PdfDictionary
        {
            [N("Type")] = N("Mask"),
            [N("S")] = N("Luminosity"),
            [N("G")] = new PdfIndirectReference(40, 0),
            [N("TR")] = N("Identity"),
        };
        PdfDictionary gs = ExtGState(("TR", N("Identity")), ("SMask", softMask));
        PdfDocumentEditor editor = EditorOver((7, gs));

        editor.RepairGraphicsState();

        Assert.Null(gs.Get("TR"));                                   // the ExtGState's own key: gone
        Assert.Equal("Identity", (softMask.Get("TR") as PdfName)?.Value); // the soft mask's: kept
    }

    [Fact]
    public void An_ExtGState_with_no_Type_is_not_touched()
    {
        // Spec §3.3 -- Faces.pdf object 49 is exactly this shape (live as /R1, gs-invoked twice).
        // ExtGStateRule object-scans for /Type /ExtGState and never reports it, so the repair must not
        // find it either: editing an object the detector cannot see means editing a document over a
        // defect nothing told the user about.
        var gs = new PdfDictionary { [N("TR")] = N("Identity") };
        PdfDocumentEditor editor = EditorOver((49, gs));

        Assert.Empty(RuleMessages(editor));

        GraphicsStateRepairPreview preview = editor.PreviewGraphicsStateRepairs();
        Assert.Empty(preview.Candidates);
        Assert.Empty(preview.Refused);

        editor.RepairGraphicsState();

        Assert.Equal("Identity", (gs.Get("TR") as PdfName)?.Value);
    }

    // ---- Staging, sharing, and closure ----------------------------------------------------------

    [Fact]
    public void The_repair_touches_only_the_object_numbers_it_is_given()
    {
        PdfDictionary first = ExtGState(("TR", N("Identity")));
        PdfDictionary second = ExtGState(("TR", N("Identity")));
        PdfDocumentEditor editor = EditorOver((19, first), (20, second));

        GraphicsStateRepairReport report = editor.RepairGraphicsState(Only(19));

        Assert.Equal(19, Assert.Single(report.Applied).ObjectNumber);
        Assert.Null(first.Get("TR"));
        Assert.Equal("Identity", (second.Get("TR") as PdfName)?.Value);
    }

    [Fact]
    public void A_null_set_repairs_every_offending_ExtGState_including_ones_no_finding_names()
    {
        // The allmand-backhoe shape: /TR on TWO ExtGStates. ExtGStateRule deduplicates by MESSAGE per
        // document, first object wins, so it raises a SINGLE finding naming 19 -- object 20 has the
        // same defect and no finding of its own. A caller staging by Finding.ObjectNumber alone would
        // leave 20 unrepaired and the finding still open after the save; passing null does not.
        PdfDictionary first = ExtGState(("TR", N("Identity")));
        PdfDictionary second = ExtGState(("TR", N("Identity")));
        PdfDocumentEditor editor = EditorOver((19, first), (20, second));

        Assert.Single(RuleMessages(editor));                                 // one finding...
        Assert.Equal(2, editor.PreviewGraphicsStateRepairs().Candidates.Count); // ...two offenders

        GraphicsStateRepairReport report = editor.RepairGraphicsState();

        Assert.Equal(2, report.Applied.Count);
        Assert.Null(first.Get("TR"));
        Assert.Null(second.Get("TR"));
        Assert.Empty(RuleMessages(editor));
    }

    [Fact]
    public void The_preview_and_the_repair_report_the_same_keys_for_the_same_document()
    {
        // The invariant the shared classifier exists for: they cannot disagree, because the repair
        // applies the classifier's own deletion plan rather than deriving a second one.
        PdfDictionary ht = Halftone1(("HalftoneName", PdfString.FromText("Default")));
        PdfDictionary withKeys = ExtGState(("TR", N("Identity")), ("HTP", new PdfArray()), ("TR2", N("Foo")));
        PdfDictionary withHalftone = ExtGState(("HT", new PdfIndirectReference(31, 0)));
        PdfDocumentEditor editor = EditorOver((31, ht), (32, withHalftone), (7, withKeys));

        GraphicsStateRepairPreview preview = editor.PreviewGraphicsStateRepairs();
        GraphicsStateRepairReport report = editor.RepairGraphicsState();

        Assert.Equal(
            preview.Candidates.Select(c => (c.ObjectNumber, string.Join(',', c.Keys))).OrderBy(t => t.ObjectNumber),
            report.Applied.Select(a => (a.ObjectNumber, string.Join(',', a.DeletedKeys))).OrderBy(t => t.ObjectNumber));
        Assert.Equal(["TR", "HTP", "TR2"], preview.Candidates.Single(c => c.ObjectNumber == 7).Keys);
        Assert.Empty(RuleMessages(editor));
    }

    [Fact]
    public void The_preview_writes_nothing()
    {
        // Propose calls this; Propose must never write. A sibling domain learning its answer from the
        // mutating repair was graded Critical.
        PdfDictionary gs = ExtGState(("TR", N("Identity")));
        PdfDocumentEditor editor = EditorOver((7, gs));

        editor.PreviewGraphicsStateRepairs();
        editor.PreviewGraphicsStateRepairs();

        Assert.Equal("Identity", (gs.Get("TR") as PdfName)?.Value);
        Assert.Single(RuleMessages(editor));
    }

    [Fact]
    public void Every_message_ExtGStateRule_can_raise_lands_in_a_candidate_or_a_refusal()
    {
        // The closure contract (spec §4): a violation producing NEITHER reads as "nothing wrong" to a
        // caller checking only those two lists -- the defect image-dictionary had to be corrected out
        // of. One document carrying every shape at once, so a message added to the rule later without
        // a classifier branch fails here rather than silently escaping.
        PdfDictionary keys = ExtGState(("TR", N("Identity")), ("HTP", new PdfArray()), ("TR2", N("Foo")));
        PdfDictionary badType = ExtGState(("HT", new PdfIndirectReference(31, 0)));
        PdfDictionary named = ExtGState(("HT", new PdfIndirectReference(33, 0)));
        PdfDictionary standalone = ExtGState(("HT", new PdfIndirectReference(34, 0)));
        PdfDictionary composite = ExtGState(("HT", new PdfIndirectReference(35, 0)));

        var type3 = new PdfDictionary
        {
            [N("HalftoneType")] = new PdfInteger(3),
        };
        PdfDictionary labelled = Halftone1(("HalftoneName", PdfString.FromText("Default")));
        PdfDictionary standaloneWithTf = Halftone1(("TransferFunction", N("Identity")));
        var type5 = new PdfDictionary
        {
            [N("HalftoneType")] = new PdfInteger(5),
            [N("Black")] = Halftone1(("TransferFunction", N("Identity"))), // primary + TF -> refusal
            [N("Gray")] = Halftone1(),                                     // non-primary, no TF -> refusal
        };

        PdfDocumentEditor editor = EditorOver(
            (7, keys), (32, badType), (31, type3),
            (36, named), (33, labelled),
            (37, standalone), (34, standaloneWithTf),
            (38, composite), (35, type5));

        string[] messages = RuleMessages(editor);
        Assert.Equal(8, messages.Length); // all eight distinct messages the rule can produce

        GraphicsStateRepairPreview preview = editor.PreviewGraphicsStateRepairs();

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal) { "TR", "HTP", "TR2", "HalftoneName" },
            preview.Candidates.SelectMany(c => c.Keys).ToHashSet(StringComparer.Ordinal));
        Assert.Equal(4, preview.Refused.Count); // HalftoneType 3, standalone TF, Black TF, Gray missing TF

        // The four repairable messages close; the four refused ones stay open -- and every one of the
        // eight is accounted for by one list or the other, which is what closure means here.
        editor.RepairGraphicsState();

        string[] remaining = RuleMessages(editor);
        Assert.Equal(4, remaining.Length);
        Assert.DoesNotContain(remaining, m => m.Contains("HalftoneName", StringComparison.Ordinal));
        Assert.DoesNotContain(remaining, m => m.Contains("TR", StringComparison.Ordinal));
        Assert.DoesNotContain(remaining, m => m.Contains("HTP", StringComparison.Ordinal));
    }
}

/// <summary>Validates the synthetic fixtures in <see cref="GraphicsStateRepairTests"/> against the REAL
/// corpus documents they stand in for. Synthetic fixtures have been wrong on this project before, and
/// the two repairable shapes that DO have corpus witnesses are cheap to pin: this asserts that the
/// halftone <c>Faces.pdf</c> actually carries has the key set <c>Halftone1</c> builds, and that the
/// <c>/TR</c> value <c>allmand</c> actually carries is the name the fixtures use.
///
/// <para>LocalOnly: the corpus exists only on the development box and (mounted) on the self-hosted
/// runners. The trait is what keeps it out of CI.</para></summary>
[Trait("Category", "LocalOnly")]
public class GraphicsStateCorpusShapeTests
{
    private const string CorpusVariable = "PDFLIBRARY_LOCAL708_CORPUS";
    private const string DefaultCorpus = @"D:\PdfCorpora\real-world\local-708";

    private static string? Corpus()
    {
        string root = Environment.GetEnvironmentVariable(CorpusVariable) ?? DefaultCorpus;
        return Directory.Exists(root) ? root : null;
    }

    [Fact]
    public void The_real_Faces_halftone_has_the_shape_the_synthetic_fixture_builds()
    {
        string? corpus = Corpus();
        Assert.SkipWhen(corpus is null, $"corpus not present at {DefaultCorpus} (LocalOnly)");

        using PdfDocument doc = PdfDocument.Load(Path.Combine(corpus!, "Faces.pdf"), "");
        using var editor = new PdfDocumentEditor(doc);

        // Both offending ExtGStates are candidates, and each deletes exactly the label.
        GraphicsStateRepairPreview preview = editor.PreviewGraphicsStateRepairs();
        Assert.Equal([32, 45], preview.Candidates.Select(c => c.ObjectNumber).Order().ToArray());
        Assert.All(preview.Candidates, c => Assert.Equal(["HalftoneName"], c.Keys));
        Assert.Empty(preview.Refused);

        // ...and the halftone really is Type 1 with the three screen keys the fixture builds, plus the
        // byte-string label -- so the synthetic Halftone1 is the real shape, not a guess at it.
        var ht = (PdfDictionary)doc.Objects[31];
        Assert.Equal(1, (ht.Get("HalftoneType") as PdfInteger)?.Value);
        Assert.Equal("Default", (ht.Get("HalftoneName") as PdfString)?.GetText());
        Assert.NotNull(ht.Get("Frequency"));
        Assert.NotNull(ht.Get("Angle"));
        Assert.NotNull(ht.Get("SpotFunction"));
        Assert.Null(ht.Get("TransferFunction"));
    }

    [Fact]
    public void The_real_allmand_TR_values_are_the_name_Identity_on_both_ExtGStates()
    {
        string? corpus = Corpus();
        Assert.SkipWhen(corpus is null, $"corpus not present at {DefaultCorpus} (LocalOnly)");

        using PdfDocument doc = PdfDocument.Load(
            Path.Combine(corpus!, "allmand-backhoe-loaders-spec-e15132.pdf"), "");
        using var editor = new PdfDocumentEditor(doc);

        GraphicsStateRepairPreview preview = editor.PreviewGraphicsStateRepairs();
        Assert.Equal([19, 20], preview.Candidates.Select(c => c.ObjectNumber).Order().ToArray());
        Assert.All(preview.Candidates, c => Assert.Equal(["TR"], c.Keys));

        foreach (int n in new[] { 19, 20 })
            Assert.Equal("Identity", (((PdfDictionary)doc.Objects[n]).Get("TR") as PdfName)?.Value);
    }
}
