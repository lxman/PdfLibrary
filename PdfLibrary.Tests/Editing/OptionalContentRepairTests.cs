using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>Tests for <see cref="PdfDocumentEditor.PreviewOptionalContentRepairs"/> and
/// <see cref="PdfDocumentEditor.RepairOptionalContent"/> -- ISO 19005-2/3 6.9 / ISO 14289-1 7.10
/// optional-content configuration remediation (<see cref="OptionalContentRule"/>).
///
/// <para><b>Every repair here is asserted on the SAVED BYTES, re-read from disk</b>, not on editor
/// state. On seven of the eight corpus documents that need this repair, <c>/OCProperties</c> and its
/// <c>/D</c> are DIRECT dictionaries inside the catalog; a repair that edited a copy, or that addressed
/// the configuration by object number, would leave every in-memory assertion passing and write a file
/// the catalog still pointed past. Asserting after a save-and-reload is the only thing that can tell
/// the two apart, so <see cref="Reopen"/> is used even where an in-memory check would read as
/// sufficient.</para></summary>
public class OptionalContentRepairTests
{
    private static PdfName N(string s) => new(s);

    /// <summary>An optional-content configuration dictionary carrying the entries given. Deliberately
    /// bare of <c>/Name</c> -- the corpus shape: all eight documents are missing the key outright,
    /// none has an empty or wrongly-typed one.</summary>
    private static PdfDictionary Config(params (string Key, PdfObject Value)[] entries)
    {
        var config = new PdfDictionary { [N("Order")] = new PdfArray() };
        foreach ((string key, PdfObject value) in entries)
            config[N(key)] = value;
        return config;
    }

    /// <summary>A one-page document whose catalog carries <c>/OCProperties</c>. The
    /// <paramref name="indirect"/> switch is the whole point of this fixture: <see langword="false"/>
    /// builds the shape seven of the eight corpus documents have (both <c>/OCProperties</c> and its
    /// <c>/D</c> DIRECT inside the catalog), <see langword="true"/> builds
    /// <c>Transcript_MICHAELJORDAN.pdf</c>'s (both indirect objects). Everything else is identical, so
    /// a test parameterised over it is comparing exactly that one difference.</summary>
    private static PdfDocument WithOptionalContent(
        bool indirect, PdfDictionary defaultConfig, PdfArray? configs = null, PdfArray? ocgs = null)
    {
        PdfDocument doc = PdfDocument.CreateEmpty();
        PdfDictionary catalog = doc.CatalogDictionary
                                ?? throw new InvalidOperationException("CreateEmpty produced no catalog.");

        var ocProperties = new PdfDictionary
        {
            [N("OCGs")] = ocgs ?? new PdfArray(),
            [N("D")] = indirect ? doc.RegisterObject(defaultConfig) : defaultConfig,
        };
        if (configs is not null) ocProperties[N("Configs")] = configs;

        catalog[N("OCProperties")] = indirect ? doc.RegisterObject(ocProperties) : ocProperties;
        return doc;
    }

    /// <summary>Saves through the real <see cref="PdfDocumentEditor.Save(Stream, PdfSaveOptions?)"/>
    /// path and re-loads the bytes as a fresh document. Everything a repair claims is asserted against
    /// THIS, never against the editor that produced it.</summary>
    private static PdfDocument Reopen(PdfDocumentEditor editor)
    {
        using var buffer = new MemoryStream();
        editor.Save(buffer);
        return PdfDocument.Load(new MemoryStream(buffer.ToArray()), "");
    }

    /// <summary>The configurations of a RELOADED document, in the rule's own order.</summary>
    private static List<PdfDictionary> ConfigsOf(PdfDocument doc)
    {
        PdfObject? Res(PdfObject? o) => o is PdfIndirectReference r ? doc.ResolveReference(r) : o;

        var result = new List<PdfDictionary>();
        if (Res(doc.CatalogDictionary?.Get("OCProperties")) is not PdfDictionary ocp) return result;
        if (Res(ocp.Get("D")) is PdfDictionary d) result.Add(d);
        if (Res(ocp.Get("Configs")) is PdfArray configs)
            foreach (PdfObject entry in configs)
                if (Res(entry) is PdfDictionary c)
                    result.Add(c);
        return result;
    }

    /// <summary>What <see cref="OptionalContentRule"/> -- the REAL detector -- reports for this
    /// document. Every fixture below is run through it, so a fixture that does not actually violate 6.9
    /// fails its own test rather than quietly proving nothing. Four of the shapes covered here
    /// (empty-string <c>/Name</c>, a wrongly-typed one, duplicate names, and a document with
    /// <c>/Configs</c> at all) have NO witness in the 708-document corpus, which is why they get that
    /// second check.</summary>
    private static string[] RuleMessages(PdfDocument doc, ConformanceProfile profile = ConformanceProfile.PdfA2b) =>
    [
        .. new OptionalContentRule().Check(new ConformanceContext(doc, profile)).Select(f => f.Message),
    ];

    /// <summary>A configuration's <c>/Name</c> as text. Non-nullable with a readable stand-in for the
    /// absent case, so a name that failed to be written shows up in the assertion message as
    /// <c>&lt;no /Name&gt;</c> rather than as a null that xunit cannot compare against a string.</summary>
    private static string NameOf(PdfDictionary config) =>
        (config.Get("Name") as PdfString)?.GetText() ?? "<no /Name>";

    // ---- The direct-vs-indirect trap (spec §3.1) ------------------------------------------------

    [Theory]
    [InlineData(false)] // /OCProperties and /D DIRECT in the catalog -- seven of the eight corpus docs
    [InlineData(true)]  // both indirect objects -- Transcript_MICHAELJORDAN.pdf
    public void A_missing_Name_is_written_and_survives_the_save_in_both_shapes(bool indirect)
    {
        PdfDocument doc = WithOptionalContent(indirect, Config());
        using var editor = new PdfDocumentEditor(doc);

        Assert.Single(RuleMessages(doc));

        OptionalContentRepairCandidate candidate =
            Assert.Single(editor.PreviewOptionalContentRepairs().Candidates);
        Assert.Equal("/D", candidate.Configuration);
        Assert.Equal("Default", candidate.NameToWrite);
        Assert.False(candidate.WouldDeleteAutoState);

        OptionalContentRepair applied = Assert.Single(editor.RepairOptionalContent().Applied);
        Assert.Equal("Default", applied.NameWritten);

        // The assertion that matters: on the bytes, after a reload -- not on the editor's own graph.
        using PdfDocument saved = Reopen(editor);
        Assert.Equal("Default", NameOf(Assert.Single(ConfigsOf(saved))));
        Assert.Empty(RuleMessages(saved));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AS_is_deleted_and_stays_deleted_through_the_save_in_both_shapes(bool indirect)
    {
        var autoStates = new PdfArray(new PdfDictionary { [N("Event")] = N("View") });
        PdfDictionary config = Config(("Name", PdfString.FromText("Layers")), ("AS", autoStates));
        PdfDocument doc = WithOptionalContent(indirect, config);
        using var editor = new PdfDocumentEditor(doc);

        Assert.Contains(RuleMessages(doc), m => m.Contains("/AS", StringComparison.Ordinal));

        OptionalContentRepairCandidate candidate =
            Assert.Single(editor.PreviewOptionalContentRepairs().Candidates);
        Assert.Null(candidate.NameToWrite); // the existing /Name is fine and is left alone
        Assert.True(candidate.WouldDeleteAutoState);

        editor.RepairOptionalContent();

        using PdfDocument saved = Reopen(editor);
        PdfDictionary reloaded = Assert.Single(ConfigsOf(saved));
        Assert.Null(reloaded.Get("AS"));
        Assert.Equal("Layers", NameOf(reloaded));
        Assert.Empty(RuleMessages(saved));
    }

    // ---- 6.9-t1: /Name absent, empty, or not a string --------------------------------------------

    [Fact]
    public void An_empty_string_Name_is_replaced()
    {
        // No corpus witness -- all eight documents are missing the key outright. The rule check above
        // is what proves the fixture is a real violation and not a shape nothing cares about.
        PdfDocument doc = WithOptionalContent(indirect: false, Config(("Name", PdfString.FromText(""))));
        using var editor = new PdfDocumentEditor(doc);

        Assert.Single(RuleMessages(doc));

        editor.RepairOptionalContent();

        using PdfDocument saved = Reopen(editor);
        Assert.Equal("Default", NameOf(Assert.Single(ConfigsOf(saved))));
        Assert.Empty(RuleMessages(saved));
    }

    [Fact]
    public void A_Name_that_is_not_a_string_is_replaced()
    {
        // OptionalContentRule reads /Name as `Resolve(...) as PdfString`, so a NAME object fails t1
        // exactly as an absent key does. Also synthetic-only.
        PdfDocument doc = WithOptionalContent(indirect: false, Config(("Name", N("Layers"))));
        using var editor = new PdfDocumentEditor(doc);

        Assert.Single(RuleMessages(doc));

        editor.RepairOptionalContent();

        using PdfDocument saved = Reopen(editor);
        Assert.Equal("Default", NameOf(Assert.Single(ConfigsOf(saved))));
        Assert.Empty(RuleMessages(saved));
    }

    [Fact]
    public void A_configuration_that_already_conforms_is_left_completely_alone()
    {
        PdfDocument doc = WithOptionalContent(indirect: false, Config(("Name", PdfString.FromText("Layers"))));
        using var editor = new PdfDocumentEditor(doc);

        Assert.Empty(RuleMessages(doc));

        OptionalContentRepairPreview preview = editor.PreviewOptionalContentRepairs();
        Assert.Empty(preview.Candidates);
        Assert.Empty(preview.Refused);
        Assert.Empty(editor.RepairOptionalContent().Applied);

        using PdfDocument saved = Reopen(editor);
        Assert.Equal("Layers", NameOf(Assert.Single(ConfigsOf(saved))));
    }

    [Fact]
    public void A_document_with_no_OCProperties_is_untouched()
    {
        PdfDocument doc = PdfDocument.CreateEmpty();
        using var editor = new PdfDocumentEditor(doc);

        OptionalContentRepairPreview preview = editor.PreviewOptionalContentRepairs();

        Assert.Empty(preview.Candidates);
        Assert.Empty(preview.Refused);
        Assert.Empty(editor.RepairOptionalContent().Applied);
        Assert.Null(doc.CatalogDictionary?.Get("OCProperties"));
    }

    // ---- 6.9-t2: names must be unique across configurations --------------------------------------

    [Fact]
    public void Several_unnamed_configurations_get_DISTINCT_names()
    {
        // The trap this test exists for (spec §3.6): 6.9-t2 forbids duplicate configuration names and
        // NO corpus document can catch a breach of it -- all eight have exactly one configuration and
        // none has /Configs at all. A repair that stamped one literal name into every configuration
        // would MANUFACTURE a t2 violation with every corpus gate still green.
        var configs = new PdfArray(Config(), Config(), Config());
        PdfDocument doc = WithOptionalContent(indirect: false, Config(), configs);
        using var editor = new PdfDocumentEditor(doc);

        Assert.Equal(4, RuleMessages(doc).Length); // t1 on all four

        Assert.Equal(
            ["/D", "/Configs[0]", "/Configs[1]", "/Configs[2]"],
            editor.PreviewOptionalContentRepairs().Candidates.Select(c => c.Configuration).ToArray());

        editor.RepairOptionalContent();

        using PdfDocument saved = Reopen(editor);
        string[] names = [.. ConfigsOf(saved).Select(NameOf)];
        Assert.Equal(["Default", "Configuration 1", "Configuration 2", "Configuration 3"], names);
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(RuleMessages(saved));
    }

    [Fact]
    public void A_duplicated_Name_is_renamed_and_the_FIRST_occurrence_keeps_it()
    {
        // 6.9-t2 is first-occurrence-wins: the rule flags the LATER configuration, so that is the one
        // renamed. Renaming the other member of the pair would leave the finding open AND relabel a
        // configuration that was never at fault.
        var configs = new PdfArray(Config(("Name", PdfString.FromText("Shared"))));
        PdfDocument doc = WithOptionalContent(
            indirect: false, Config(("Name", PdfString.FromText("Shared"))), configs);
        using var editor = new PdfDocumentEditor(doc);

        Assert.Contains(RuleMessages(doc), m => m.Contains("unique", StringComparison.Ordinal));

        OptionalContentRepairCandidate candidate =
            Assert.Single(editor.PreviewOptionalContentRepairs().Candidates);
        Assert.Equal("/Configs[0]", candidate.Configuration);
        Assert.Equal("Configuration 1", candidate.NameToWrite);

        editor.RepairOptionalContent();

        using PdfDocument saved = Reopen(editor);
        Assert.Equal(["Shared", "Configuration 1"], ConfigsOf(saved).Select(NameOf).ToArray());
        Assert.Empty(RuleMessages(saved));
    }

    [Fact]
    public void A_synthesized_name_never_collides_with_one_that_is_staying_put()
    {
        // /Configs[0] already legitimately holds "Configuration 2" -- the exact name the unnamed
        // /Configs[1] would otherwise be given, since a synthesized name is derived from the 1-based
        // /Configs position. The suffix loop is what stops the repair inventing the very duplicate
        // 6.9-t2 forbids.
        var configs = new PdfArray(Config(("Name", PdfString.FromText("Configuration 2"))), Config());
        PdfDocument doc = WithOptionalContent(
            indirect: false, Config(("Name", PdfString.FromText("Default"))), configs);
        using var editor = new PdfDocumentEditor(doc);

        Assert.Single(RuleMessages(doc)); // t1 on /Configs[1] only

        editor.RepairOptionalContent();

        using PdfDocument saved = Reopen(editor);
        Assert.Equal(
            ["Default", "Configuration 2", "Configuration 2 (2)"],
            ConfigsOf(saved).Select(NameOf).ToArray());
        Assert.Empty(RuleMessages(saved));
    }

    [Fact]
    public void A_configuration_listed_twice_is_a_refusal()
    {
        // Closure: /D and /Configs[0] naming the SAME indirect object. The rule's walk yields it twice
        // and t2 therefore fires against itself, but the violation cannot be repaired -- one dictionary
        // cannot carry two different names. Synthetic-only (no corpus document has /Configs), and here
        // so the violation lands in a refusal rather than in neither list.
        PdfDocument doc = PdfDocument.CreateEmpty();
        PdfDictionary catalog = doc.CatalogDictionary!;
        PdfIndirectReference shared = doc.RegisterObject(Config(("Name", PdfString.FromText("Only"))));
        catalog[N("OCProperties")] = new PdfDictionary
        {
            [N("OCGs")] = new PdfArray(),
            [N("D")] = shared,
            [N("Configs")] = new PdfArray(shared),
        };
        using var editor = new PdfDocumentEditor(doc);

        Assert.Contains(RuleMessages(doc), m => m.Contains("unique", StringComparison.Ordinal));

        OptionalContentRepairPreview preview = editor.PreviewOptionalContentRepairs();
        Assert.Empty(preview.Candidates);
        OptionalContentRefusal refusal = Assert.Single(preview.Refused);
        Assert.Equal("/Configs[0]", refusal.Configuration);
        Assert.Contains("same dictionary", refusal.Reason, StringComparison.Ordinal);

        OptionalContentRepairReport report = editor.RepairOptionalContent();

        Assert.Empty(report.Applied);
        Assert.Single(report.Refused);
        using PdfDocument saved = Reopen(editor);
        Assert.Equal("Only", NameOf(ConfigsOf(saved)[0]));
    }

    // ---- Sharing, idempotency, and closure -------------------------------------------------------

    [Fact]
    public void The_preview_writes_nothing()
    {
        // Propose calls this; Propose must never write.
        PdfDictionary config = Config();
        PdfDocument doc = WithOptionalContent(indirect: false, config);
        using var editor = new PdfDocumentEditor(doc);

        editor.PreviewOptionalContentRepairs();
        editor.PreviewOptionalContentRepairs();

        Assert.Null(config.Get("Name"));
        Assert.Single(RuleMessages(doc));
    }

    [Fact]
    public void The_preview_and_the_repair_report_the_same_edits()
    {
        var configs = new PdfArray(Config(("AS", new PdfArray())), Config());
        PdfDocument doc = WithOptionalContent(indirect: true, Config(), configs);
        using var editor = new PdfDocumentEditor(doc);

        OptionalContentRepairPreview preview = editor.PreviewOptionalContentRepairs();
        OptionalContentRepairReport report = editor.RepairOptionalContent();

        Assert.Equal(
            preview.Candidates.Select(c => (c.Configuration, c.NameToWrite, c.WouldDeleteAutoState)),
            report.Applied.Select(a => (a.Configuration, a.NameWritten, a.DeletedAutoState)));
        using PdfDocument saved = Reopen(editor);
        Assert.Empty(RuleMessages(saved));
    }

    [Fact]
    public void Repairing_twice_is_a_no_op_the_second_time()
    {
        // Deterministic names are what make this true: a second run must not churn out fresh labels.
        PdfDocument doc = WithOptionalContent(indirect: false, Config(("AS", new PdfArray())));
        using var editor = new PdfDocumentEditor(doc);

        editor.RepairOptionalContent();
        Assert.Empty(editor.RepairOptionalContent().Applied);

        using PdfDocument saved = Reopen(editor);
        Assert.Equal("Default", NameOf(Assert.Single(ConfigsOf(saved))));
        Assert.Empty(RuleMessages(saved));
    }

    [Fact]
    public void Every_message_OptionalContentRule_can_raise_lands_in_a_candidate_or_a_refusal()
    {
        // The closure contract (spec §4). One document carrying every shape the rule reports -- t1, t2
        // and t4 -- so a message added later without a classifier branch fails here rather than
        // escaping into "nothing wrong".
        var configs = new PdfArray(
            Config(("Name", PdfString.FromText("Shared"))),  // t2 against /D
            Config(("AS", new PdfArray())));                 // t1 + t4
        PdfDocument doc = WithOptionalContent(
            indirect: false, Config(("Name", PdfString.FromText("Shared"))), configs);
        using var editor = new PdfDocumentEditor(doc);

        Assert.Equal(3, RuleMessages(doc).Length);

        OptionalContentRepairPreview preview = editor.PreviewOptionalContentRepairs();
        Assert.Equal(2, preview.Candidates.Count);
        Assert.Empty(preview.Refused);

        editor.RepairOptionalContent();

        using PdfDocument saved = Reopen(editor);
        Assert.Empty(RuleMessages(saved));
        Assert.Equal(
            ["Shared", "Configuration 1", "Configuration 2"],
            ConfigsOf(saved).Select(NameOf).ToArray());
    }

    [Fact]
    public void The_repair_closes_the_PDF_UA_form_of_the_clause_too()
    {
        // OptionalContentRule serves PDF/UA-1 7.10 as well, with t2 gated off there. The repair is
        // profile-blind by construction -- it satisfies the stricter PDF/A form -- and this is the
        // check that the weaker one is not left open by some asymmetry.
        PdfDocument doc = WithOptionalContent(indirect: false, Config(("AS", new PdfArray())));
        using var editor = new PdfDocumentEditor(doc);

        Assert.Equal(2, RuleMessages(doc, ConformanceProfile.PdfUA1).Length);

        editor.RepairOptionalContent();

        using PdfDocument saved = Reopen(editor);
        Assert.Empty(RuleMessages(saved, ConformanceProfile.PdfUA1));
    }
}

/// <summary>Proves the repair on the REAL corpus documents, one of each <c>/OCProperties</c> shape, and
/// on the saved bytes -- spec §6 DoD item 5. <c>5137.pdf</c> holds <c>/OCProperties</c> and <c>/D</c> as
/// DIRECT dictionaries inside its catalog (the shape seven of the eight share);
/// <c>Transcript_MICHAELJORDAN.pdf</c> holds both as indirect objects and is the only document in the
/// corpus carrying <c>/AS</c>.
///
/// <para>LocalOnly: the corpus exists only on the development box and (mounted) on the self-hosted
/// runners. The trait is what keeps it out of CI.</para></summary>
[Trait("Category", "LocalOnly")]
public class OptionalContentCorpusShapeTests
{
    private const string CorpusVariable = "PDFLIBRARY_LOCAL708_CORPUS";
    private const string DefaultCorpus = @"D:\PdfCorpora\real-world\local-708";

    private static string? Corpus()
    {
        string root = Environment.GetEnvironmentVariable(CorpusVariable) ?? DefaultCorpus;
        return Directory.Exists(root) ? root : null;
    }

    private static PdfObject? Res(PdfDocument doc, PdfObject? o) =>
        o is PdfIndirectReference r ? doc.ResolveReference(r) : o;

    private static (bool OcPropsDirect, bool DefaultConfigDirect) Shape(PdfDocument doc)
    {
        PdfObject? rawOcp = doc.CatalogDictionary?.Get("OCProperties");
        var ocp = (PdfDictionary)Res(doc, rawOcp)!;
        return (rawOcp is not PdfIndirectReference, ocp.Get("D") is not PdfIndirectReference);
    }

    private static string[] Messages(PdfDocument doc) =>
    [
        .. new OptionalContentRule()
            .Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b))
            .Select(f => f.Message),
    ];

    [Theory]
    // The direct shape (seven of eight) and the indirect one (the eighth). Both are asserted, so a
    // repair that only worked on one of them cannot pass.
    [InlineData("5137.pdf", true, true, false)]
    [InlineData("Transcript_MICHAELJORDAN.pdf", false, false, true)]
    public void The_repair_closes_6_9_on_the_saved_bytes_of_a_real_document(
        string file, bool ocPropsDirect, bool defaultConfigDirect, bool hasAutoState)
    {
        string? corpus = Corpus();
        Assert.SkipWhen(corpus is null, $"corpus not present at {DefaultCorpus} (LocalOnly)");

        byte[] original = File.ReadAllBytes(Path.Combine(corpus!, file));
        byte[] repaired;

        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(original), ""))
        {
            doc.MaterializeAllObjects();
            // Spec §3.1, re-measured here rather than trusted: this is the trap the whole task exists
            // for, and a fixture that silently stopped being the shape it claims proves nothing.
            Assert.Equal((ocPropsDirect, defaultConfigDirect), Shape(doc));
            Assert.NotEmpty(Messages(doc));

            using var editor = new PdfDocumentEditor(doc);
            OptionalContentRepair applied = Assert.Single(editor.RepairOptionalContent().Applied);
            Assert.Equal("/D", applied.Configuration);
            Assert.Equal("Default", applied.NameWritten);
            Assert.Equal(hasAutoState, applied.DeletedAutoState);

            using var buffer = new MemoryStream();
            editor.Save(buffer);
            repaired = buffer.ToArray();
        }

        // The whole point: the finding is gone from the BYTES, re-read from scratch. An in-memory
        // assertion would pass for a repair that edited a copy the catalog still pointed past.
        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(repaired), "");
        Assert.Empty(Messages(reloaded));

        PdfObject? ocp = Res(reloaded, reloaded.CatalogDictionary?.Get("OCProperties"));
        var config = (PdfDictionary)Res(reloaded, ((PdfDictionary)ocp!).Get("D"))!;
        Assert.Equal("Default", (config.Get("Name") as PdfString)?.GetText());
        Assert.Null(config.Get("AS"));
    }
}
