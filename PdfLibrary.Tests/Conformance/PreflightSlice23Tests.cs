using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 23 (PDF/UA-1) — the structural bucket, calibrated against veraPDF's PDF_UA-1 profile:
/// <list type="bullet">
///   <item><b>Suspects (7.1, <see cref="UaSuspectsRule"/>)</b> — catalog /MarkInfo /Suspects must not be true
///     (veraPDF CosDocument test 4: <c>Suspects != true</c>).</item>
///   <item><b>Note IDs (7.9, <see cref="UaNoteIdRule"/>)</b> — every &lt;Note&gt; needs a non-empty /ID
///     (SENote t1) and each must be unique (SENote t2).</item>
///   <item><b>Optional-content config (7.10, <see cref="OptionalContentRule"/> widened to UA)</b> — /Name
///     non-empty (PDOCConfig t1) and no /AS (PDOCConfig t2). veraPDF UA 7.10 has <em>no</em> /Name uniqueness
///     check, so the PDF/A uniqueness test is profile-gated off for the UA target — the regression guard here.</item>
///   <item><b>Reference XObjects (7.20, <see cref="UaReferenceXObjectRule"/>)</b> — no Form XObject may carry a
///     /Ref key (PDXForm t1: <c>containsRef == false</c>).</item>
/// </list>
/// The reference-file pass-oracle (<see cref="PdfUaReferenceOracleTests"/>) and veraPDF corpus oracle
/// (<see cref="CorpusOracleTests"/>) back the 0-FP / detection surface; these are the hand-built edge cases.
/// </summary>
public class PreflightSlice23Tests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfString Str(string s) => new(System.Text.Encoding.ASCII.GetBytes(s));
    private static ConformanceContext Ctx(PdfDocument doc, ConformanceProfile profile = ConformanceProfile.PdfUA1) =>
        new(doc, profile);

    /// <summary>A structure element with type <paramref name="s"/> and the given /K children.</summary>
    private static PdfDictionary Elem(string s, params PdfObject[] kids) => new()
    {
        [N("Type")] = N("StructElem"),
        [N("S")] = N(s),
        [N("K")] = new PdfArray(kids),
    };

    /// <summary>A document with a structure tree whose root /K and element objects <paramref name="build"/>
    /// populates; the rules read the tree through the catalog /StructTreeRoot.</summary>
    private static PdfDocument StructDoc(System.Action<PdfDocument, PdfDictionary> build)
    {
        var doc = new PdfDocument();
        var root = new PdfDictionary { [N("Type")] = N("StructTreeRoot") };
        build(doc, root);
        doc.AddObject(31, 0, root);
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("StructTreeRoot")] = Ref(31) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    // ── Suspects (7.1) ─────────────────────────────────────────────────────────

    /// <summary>A catalog carrying a /MarkInfo whose /Suspects is <paramref name="suspects"/> (null = the
    /// /Suspects key is absent; the whole /MarkInfo is omitted when <paramref name="markInfo"/> is false).</summary>
    private static PdfDocument SuspectsDoc(bool? suspects, bool markInfo = true)
    {
        var doc = new PdfDocument();
        var catalog = new PdfDictionary { [N("Type")] = N("Catalog") };
        if (markInfo)
        {
            var mark = new PdfDictionary { [N("Marked")] = PdfBoolean.True };
            if (suspects is { } s) mark[N("Suspects")] = PdfBoolean.FromValue(s);
            catalog[N("MarkInfo")] = mark;
        }
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void Suspects_true_is_flagged()
    {
        Finding f = Assert.Single(new UaSuspectsRule().Check(Ctx(SuspectsDoc(suspects: true))));
        Assert.Equal("ua-suspects", f.RuleId);
        Assert.Contains("Suspects", f.Message);
        Assert.Null(f.ObjectNumber);
        Assert.Null(f.PageIndex);
    }

    [Fact]
    public void Suspects_false_passes()
    {
        Assert.Empty(new UaSuspectsRule().Check(Ctx(SuspectsDoc(suspects: false))));
    }

    [Fact]
    public void Suspects_absent_passes()
    {
        Assert.Empty(new UaSuspectsRule().Check(Ctx(SuspectsDoc(suspects: null)))); // /MarkInfo present, no /Suspects
    }

    [Fact]
    public void MarkInfo_absent_passes()
    {
        Assert.Empty(new UaSuspectsRule().Check(Ctx(SuspectsDoc(suspects: null, markInfo: false))));
    }

    // ── Note IDs (7.9) ─────────────────────────────────────────────────────────

    private static PdfDictionary Note(string? id)
    {
        var note = Elem("Note", new PdfInteger(0));
        if (id is not null) note[N("ID")] = Str(id);
        return note;
    }

    private static Finding[] NoteFindings(PdfDocument doc) => new UaNoteIdRule().Check(Ctx(doc)).ToArray();

    [Fact]
    public void Note_with_nonempty_id_passes()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(50, 0, Note("note1"));
            d.AddObject(40, 0, Elem("Document", Ref(50)));
            root[N("K")] = Ref(40);
        });
        Assert.Empty(NoteFindings(doc));
    }

    [Fact]
    public void Note_without_id_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(50, 0, Note(null));
            d.AddObject(40, 0, Elem("Document", Ref(50)));
            root[N("K")] = Ref(40);
        });
        Finding f = Assert.Single(NoteFindings(doc));
        Assert.Equal("ua-note-id", f.RuleId);
        Assert.Contains("no /ID", f.Message);
        Assert.Equal(50, f.ObjectNumber);
    }

    [Fact]
    public void Note_with_empty_id_is_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(50, 0, Note("")); // present but zero bytes
            d.AddObject(40, 0, Elem("Document", Ref(50)));
            root[N("K")] = Ref(40);
        });
        Finding f = Assert.Single(NoteFindings(doc));
        Assert.Contains("no /ID", f.Message);
    }

    [Fact]
    public void Two_notes_with_the_same_id_are_flagged_for_uniqueness()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(50, 0, Note("dup"));
            d.AddObject(51, 0, Note("dup"));
            d.AddObject(40, 0, Elem("Document", Ref(50), Ref(51)));
            root[N("K")] = Ref(40);
        });
        Finding[] findings = NoteFindings(doc);
        Assert.Equal(2, findings.Length);
        Assert.All(findings, f =>
        {
            Assert.Equal("ua-note-id", f.RuleId);
            Assert.Contains("non-unique /ID", f.Message);
        });
    }

    [Fact]
    public void Two_notes_with_distinct_ids_pass()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(50, 0, Note("a"));
            d.AddObject(51, 0, Note("b"));
            d.AddObject(40, 0, Elem("Document", Ref(50), Ref(51)));
            root[N("K")] = Ref(40);
        });
        Assert.Empty(NoteFindings(doc));
    }

    [Fact] // a missing-ID Note gets only the presence finding, never the uniqueness one
    public void Note_without_id_is_not_also_flagged_for_uniqueness()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(50, 0, Note(null));
            d.AddObject(51, 0, Note(null));
            d.AddObject(40, 0, Elem("Document", Ref(50), Ref(51)));
            root[N("K")] = Ref(40);
        });
        Finding[] findings = NoteFindings(doc);
        Assert.Equal(2, findings.Length);
        Assert.All(findings, f => Assert.Contains("no /ID", f.Message));
    }

    [Fact] // a non-Note element without an /ID must not be flagged
    public void Non_note_element_without_id_is_not_flagged()
    {
        var doc = StructDoc((d, root) =>
        {
            d.AddObject(50, 0, Elem("P", new PdfInteger(0))); // Paragraph, no /ID
            d.AddObject(40, 0, Elem("Document", Ref(50)));
            root[N("K")] = Ref(40);
        });
        Assert.Empty(NoteFindings(doc));
    }

    // ── Optional-content configuration (7.10 UA / 6.9 PDF/A) ─────────────────────

    /// <summary>A catalog whose /OCProperties has the given /D config and /Configs array.</summary>
    private static PdfDocument OcDoc(PdfDictionary? defaultConfig, params PdfDictionary[] configs)
    {
        var ocProps = new PdfDictionary { [N("OCGs")] = new PdfArray() };
        if (defaultConfig is not null) ocProps[N("D")] = defaultConfig;
        if (configs.Length > 0) ocProps[N("Configs")] = new PdfArray(configs.Cast<PdfObject>().ToArray());

        var doc = new PdfDocument();
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("OCProperties")] = ocProps });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static PdfDictionary Config(string? name = null, bool withAs = false)
    {
        var config = new PdfDictionary();
        if (name is not null) config[N("Name")] = Str(name);
        if (withAs) config[N("AS")] = new PdfArray();
        return config;
    }

    private static Finding[] OcFindings(PdfDocument doc, ConformanceProfile profile) =>
        new OptionalContentRule().Check(Ctx(doc, profile)).ToArray();

    [Fact]
    public void Oc_config_with_valid_name_passes_under_ua()
    {
        Assert.Empty(OcFindings(OcDoc(Config(name: "View")), ConformanceProfile.PdfUA1));
    }

    [Fact]
    public void Oc_config_missing_name_is_flagged_under_ua()
    {
        Finding f = Assert.Single(OcFindings(OcDoc(Config(name: null)), ConformanceProfile.PdfUA1));
        Assert.Equal("optional-content", f.RuleId);
        Assert.Contains("non-empty /Name", f.Message);
        Assert.Contains("ISO 14289-1:2014, 7.10", f.Clause);
    }

    [Fact]
    public void Oc_config_with_as_is_flagged_under_ua()
    {
        Finding f = Assert.Single(OcFindings(OcDoc(Config(name: "View", withAs: true)), ConformanceProfile.PdfUA1));
        Assert.Equal("optional-content", f.RuleId);
        Assert.Contains("/AS", f.Message);
        Assert.Contains("ISO 14289-1:2014, 7.10", f.Clause);
    }

    [Fact] // THE UA GATE regression guard: two configs sharing a /Name must NOT flag under PDF/UA-1
    public void Duplicate_config_names_do_not_flag_under_ua()
    {
        PdfDocument doc = OcDoc(Config(name: "Same"), Config(name: "Same"));
        Assert.Empty(OcFindings(doc, ConformanceProfile.PdfUA1));
    }

    [Fact] // the gate the other way: PDF/A-2b DOES enforce /Name uniqueness (6.9-t2)
    public void Duplicate_config_names_flag_under_pdfa()
    {
        PdfDocument doc = OcDoc(Config(name: "Same"), Config(name: "Same"));
        Finding f = Assert.Single(OcFindings(doc, ConformanceProfile.PdfA2b));
        Assert.Equal("optional-content", f.RuleId);
        Assert.Contains("unique", f.Message);
        Assert.Contains("ISO 19005-2:2011, 6.9", f.Clause);
    }

    // ── Reference XObjects (7.20) ────────────────────────────────────────────────

    private static PdfStream FormXObject(bool withRef)
    {
        var dict = new PdfDictionary { [N("Type")] = N("XObject"), [N("Subtype")] = N("Form") };
        if (withRef) dict[N("Ref")] = Ref(99); // an indirect ref to a file-spec-bearing dict
        return new PdfStream(dict, new byte[] { (byte)'q' });
    }

    private static PdfDocument XObjectDoc(System.Action<PdfDocument> build)
    {
        var doc = new PdfDocument();
        build(doc);
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog") });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static Finding[] RefFindings(PdfDocument doc) => new UaReferenceXObjectRule().Check(Ctx(doc)).ToArray();

    [Fact]
    public void Form_xobject_with_ref_is_flagged()
    {
        var doc = XObjectDoc(d => d.AddObject(10, 0, FormXObject(withRef: true)));
        Finding f = Assert.Single(RefFindings(doc));
        Assert.Equal("ua-reference-xobject", f.RuleId);
        Assert.Contains("reference XObject", f.Message);
        Assert.Equal(10, f.ObjectNumber);
    }

    [Fact]
    public void Form_xobject_without_ref_passes()
    {
        var doc = XObjectDoc(d => d.AddObject(10, 0, FormXObject(withRef: false)));
        Assert.Empty(RefFindings(doc));
    }

    [Fact] // a /Ref on a non-Form stream (here an Image XObject) is not a reference XObject
    public void Non_form_stream_with_ref_is_not_flagged()
    {
        var doc = XObjectDoc(d =>
        {
            var dict = new PdfDictionary
            {
                [N("Type")] = N("XObject"), [N("Subtype")] = N("Image"), [N("Ref")] = Ref(99),
            };
            d.AddObject(10, 0, new PdfStream(dict, new byte[] { 0x00 }));
        });
        Assert.Empty(RefFindings(doc));
    }
}
