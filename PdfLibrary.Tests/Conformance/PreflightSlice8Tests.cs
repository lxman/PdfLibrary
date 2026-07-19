using System;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 8 of the preflight: embedded files (6.8), optional content (6.9), alternate presentations
/// (6.10) and document requirements (6.11). Rule-level tests over hand-built documents; the veraPDF
/// corpus oracle (<see cref="CorpusOracleTests"/>) exercises the 3b embedded-file rules end-to-end.
/// </summary>
public class PreflightSlice8Tests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfString Str(string s) => new(System.Text.Encoding.ASCII.GetBytes(s));

    private static PdfDocument Doc(Action<PdfDocument, PdfDictionary> configureCatalog)
    {
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfDictionary { [N("Type")] = N("Page"), [N("Parent")] = Ref(2) });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(3)),
            [N("Count")] = new PdfInteger(1),
        });
        var catalog = new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) };
        configureCatalog(doc, catalog);
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    private static ConformanceContext Ctx(PdfDocument doc, ConformanceProfile profile) => new(doc, profile);

    // ── 6.8 EmbeddedFileSpecRule ─────────────────────────────────────────────

    /// <summary>An embedded file spec (obj 10) + its embedded stream (obj 11), registered in /Names
    /// /EmbeddedFiles and optionally referenced from a catalog /AF array.</summary>
    private static void AddEmbeddedFile(
        PdfDocument doc, PdfDictionary catalog, bool hasF, bool hasUF, string? afRelationship,
        bool referencedByAF, string? subtype = "text/plain", string fValue = "file.txt", string ufValue = "file.txt")
    {
        var streamDict = new PdfDictionary();
        if (subtype is not null) streamDict[N("Subtype")] = N(subtype);
        doc.AddObject(11, 0, new PdfStream(streamDict, new byte[] { 1 }));

        var spec = new PdfDictionary
        {
            [N("Type")] = N("Filespec"),
            [N("EF")] = new PdfDictionary { [N("F")] = Ref(11) },
        };
        if (hasF) spec[N("F")] = Str(fValue);
        if (hasUF) spec[N("UF")] = Str(ufValue);
        if (afRelationship is not null) spec[N("AFRelationship")] = N(afRelationship);
        doc.AddObject(10, 0, spec);

        catalog[N("Names")] = new PdfDictionary
        {
            [N("EmbeddedFiles")] = new PdfDictionary { [N("Names")] = new PdfArray(Str("f"), Ref(10)) },
        };
        if (referencedByAF) catalog[N("AF")] = new PdfArray(Ref(10));
    }

    [Fact]
    public void Embedded_file_missing_UF_is_flagged() // 6.8-t2
    {
        var doc = Doc((d, c) => AddEmbeddedFile(d, c, hasF: true, hasUF: false, afRelationship: null, referencedByAF: false));
        Finding f = Assert.Single(new EmbeddedFileSpecRule().Check(Ctx(doc, ConformanceProfile.PdfA2b)));
        Assert.Equal("embedded-file", f.RuleId);
    }

    [Fact]
    public void Embedded_file_with_F_and_UF_passes_in_part2() // 6.8-t2 (t1/t3/t4 are 3b-only)
    {
        var doc = Doc((d, c) => AddEmbeddedFile(d, c, hasF: true, hasUF: true, afRelationship: null, referencedByAF: false));
        Assert.Empty(new EmbeddedFileSpecRule().Check(Ctx(doc, ConformanceProfile.PdfA2b)));
    }

    [Fact]
    public void Embedded_file_missing_AFRelationship_is_flagged_in_part3() // 6.8-t3
    {
        var doc = Doc((d, c) => AddEmbeddedFile(d, c, hasF: true, hasUF: true, afRelationship: null, referencedByAF: true));
        Assert.Single(new EmbeddedFileSpecRule().Check(Ctx(doc, ConformanceProfile.PdfA3b)));
    }

    [Fact]
    public void Embedded_file_not_referenced_by_AF_is_flagged_in_part3() // 6.8-t4
    {
        var doc = Doc((d, c) => AddEmbeddedFile(d, c, hasF: true, hasUF: true, afRelationship: "Data", referencedByAF: false));
        Assert.Single(new EmbeddedFileSpecRule().Check(Ctx(doc, ConformanceProfile.PdfA3b)));
    }

    [Fact]
    public void Embedded_file_with_invalid_mime_subtype_is_flagged_in_part3() // 6.8-t1
    {
        var doc = Doc((d, c) => AddEmbeddedFile(d, c, hasF: true, hasUF: true, afRelationship: "Data", referencedByAF: true, subtype: "notmime"));
        Assert.Single(new EmbeddedFileSpecRule().Check(Ctx(doc, ConformanceProfile.PdfA3b)));
    }

    [Fact]
    public void Fully_conformant_embedded_file_passes_in_part3()
    {
        var doc = Doc((d, c) => AddEmbeddedFile(d, c, hasF: true, hasUF: true, afRelationship: "Data", referencedByAF: true));
        Assert.Empty(new EmbeddedFileSpecRule().Check(Ctx(doc, ConformanceProfile.PdfA3b)));
    }

    // ── 7.11 EmbeddedFileSpecRule under PDF/UA-1 (non-empty /F and /UF) ───────

    [Fact]
    public void Ua1_embedded_file_missing_UF_is_flagged_at_7_11() // 7.11-t1
    {
        var doc = Doc((d, c) => AddEmbeddedFile(d, c, hasF: true, hasUF: false, afRelationship: null, referencedByAF: false));
        Finding f = Assert.Single(new EmbeddedFileSpecRule().Check(Ctx(doc, ConformanceProfile.PdfUA1)));
        Assert.Equal("embedded-file", f.RuleId);
        Assert.Equal(ConformanceClauses.For(ConformanceProfile.PdfUA1, "7.11"), f.Clause);
    }

    [Fact]
    public void Ua1_embedded_file_with_empty_UF_is_flagged() // 7.11-t1 non-empty requirement
    {
        // A present but empty /UF passes the PDF/A presence check yet fails UA-1's non-empty requirement.
        var doc = Doc((d, c) => AddEmbeddedFile(d, c, hasF: true, hasUF: true, afRelationship: null, referencedByAF: false, ufValue: ""));
        Assert.Single(new EmbeddedFileSpecRule().Check(Ctx(doc, ConformanceProfile.PdfUA1)));
    }

    [Fact]
    public void Ua1_embedded_file_with_nonempty_F_and_UF_passes()
    {
        var doc = Doc((d, c) => AddEmbeddedFile(d, c, hasF: true, hasUF: true, afRelationship: null, referencedByAF: false));
        Assert.Empty(new EmbeddedFileSpecRule().Check(Ctx(doc, ConformanceProfile.PdfUA1)));
    }

    /// <summary>A one-page doc whose only embedded file hangs off a FileAttachment annotation's /FS (not the
    /// catalog name tree) — the veraPDF 7.18.7 test-file shape that also fails 7.11.</summary>
    private static PdfDocument DocWithFileAttachment(bool hasUF, string ufValue = "file.txt")
    {
        var doc = new PdfDocument();
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), new byte[] { 1 }));
        var spec = new PdfDictionary
        {
            [N("Type")] = N("Filespec"),
            [N("F")] = Str("file.txt"),
            [N("EF")] = new PdfDictionary { [N("F")] = Ref(11) },
        };
        if (hasUF) spec[N("UF")] = Str(ufValue);
        doc.AddObject(10, 0, spec);
        doc.AddObject(12, 0, new PdfDictionary
        {
            [N("Type")] = N("Annot"), [N("Subtype")] = N("FileAttachment"), [N("FS")] = Ref(10),
        });
        doc.AddObject(3, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"), [N("Parent")] = Ref(2), [N("Annots")] = new PdfArray(Ref(12)),
        });
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    [Fact]
    public void Ua1_file_attachment_annotation_with_missing_UF_is_flagged() // 7.11 via annotation /FS
    {
        Finding f = Assert.Single(new EmbeddedFileSpecRule().Check(Ctx(DocWithFileAttachment(hasUF: false), ConformanceProfile.PdfUA1)));
        Assert.Equal(ConformanceClauses.For(ConformanceProfile.PdfUA1, "7.11"), f.Clause);
    }

    [Fact]
    public void Ua1_file_attachment_annotation_with_nonempty_UF_passes()
    {
        Assert.Empty(new EmbeddedFileSpecRule().Check(Ctx(DocWithFileAttachment(hasUF: true), ConformanceProfile.PdfUA1)));
    }

    [Fact]
    public void Pdfa_does_not_collect_annotation_filespecs() // PDF/A behaviour unchanged (name-tree only)
    {
        Assert.Empty(new EmbeddedFileSpecRule().Check(Ctx(DocWithFileAttachment(hasUF: false), ConformanceProfile.PdfA2b)));
    }

    // ── 6.9 OptionalContentRule ──────────────────────────────────────────────

    private static void AddOcConfigs(PdfDictionary catalog, PdfDictionary defaultConfig, params PdfDictionary[] alternates)
    {
        var ocp = new PdfDictionary { [N("D")] = defaultConfig };
        if (alternates.Length > 0) ocp[N("Configs")] = new PdfArray(alternates);
        catalog[N("OCProperties")] = ocp;
    }

    [Fact]
    public void Oc_config_without_name_is_flagged() // 6.9-t1
    {
        var doc = Doc((_, c) => AddOcConfigs(c, new PdfDictionary()));
        Assert.Single(new OptionalContentRule().Check(Ctx(doc, ConformanceProfile.PdfA2b)));
    }

    [Fact]
    public void Oc_duplicate_config_names_are_flagged() // 6.9-t2
    {
        var doc = Doc((_, c) => AddOcConfigs(c,
            new PdfDictionary { [N("Name")] = Str("View") },
            new PdfDictionary { [N("Name")] = Str("View") }));
        Assert.Single(new OptionalContentRule().Check(Ctx(doc, ConformanceProfile.PdfA2b)));
    }

    [Fact]
    public void Oc_config_with_AS_is_flagged() // 6.9-t4
    {
        var doc = Doc((_, c) => AddOcConfigs(c,
            new PdfDictionary { [N("Name")] = Str("View"), [N("AS")] = new PdfArray() }));
        Assert.Single(new OptionalContentRule().Check(Ctx(doc, ConformanceProfile.PdfA2b)));
    }

    [Fact]
    public void Oc_clean_config_passes()
    {
        var doc = Doc((_, c) => AddOcConfigs(c, new PdfDictionary { [N("Name")] = Str("Default") }));
        Assert.Empty(new OptionalContentRule().Check(Ctx(doc, ConformanceProfile.PdfA2b)));
    }

    // ── 6.10 AlternatePresentationsRule ──────────────────────────────────────

    [Fact]
    public void Alternate_presentations_in_names_is_flagged() // 6.10-t1
    {
        var doc = Doc((_, c) => c[N("Names")] = new PdfDictionary { [N("AlternatePresentations")] = new PdfDictionary() });
        Assert.Single(new AlternatePresentationsRule().Check(Ctx(doc, ConformanceProfile.PdfA2b)));
    }

    [Fact]
    public void Page_pres_steps_is_flagged() // 6.10-t2
    {
        var doc = Doc((_, _) => { });
        ((PdfDictionary)doc.GetObject(3)!)[N("PresSteps")] = new PdfDictionary();
        Assert.Single(new AlternatePresentationsRule().Check(Ctx(doc, ConformanceProfile.PdfA2b)));
    }

    // ── 6.11 DocumentRequirementsRule ────────────────────────────────────────

    [Fact]
    public void Catalog_requirements_is_flagged() // 6.11-t1
    {
        var doc = Doc((_, c) => c[N("Requirements")] = new PdfArray());
        Finding f = Assert.Single(new DocumentRequirementsRule().Check(Ctx(doc, ConformanceProfile.PdfA2b)));
        Assert.Equal("document-requirements", f.RuleId);
    }

    [Fact]
    public void Clean_document_passes_all_slice8_rules()
    {
        var doc = Doc((_, _) => { });
        var ctx = Ctx(doc, ConformanceProfile.PdfA3b);
        Assert.Empty(new EmbeddedFileSpecRule().Check(ctx));
        Assert.Empty(new OptionalContentRule().Check(ctx));
        Assert.Empty(new AlternatePresentationsRule().Check(ctx));
        Assert.Empty(new DocumentRequirementsRule().Check(ctx));
    }
}
