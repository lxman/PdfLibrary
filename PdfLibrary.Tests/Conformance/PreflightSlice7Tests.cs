using System;
using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 7 of the preflight: annotations (6.3), interactive forms (6.4.1/6.4.2) and actions (6.5).
/// Rule-level tests drive each rule over a hand-built document; the veraPDF corpus oracle
/// (<see cref="CorpusOracleTests"/>) exercises them against the real fixture set end-to-end.
/// The digital-signature rules (6.4.3) and the widget-button appearance sub-structure (6.3.3-t3/t4)
/// are intentionally out of scope for this slice.
/// </summary>
public class PreflightSlice7Tests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    /// <summary>Builds a one-page document; the page carries <paramref name="annots"/> as /Annots and the
    /// catalog is post-configured by <paramref name="configureCatalog"/>.</summary>
    private static PdfDocument DocWith(PdfArray? annots = null, Action<PdfDocument, PdfDictionary>? configureCatalog = null)
    {
        var doc = new PdfDocument();

        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
        };
        if (annots is not null)
            page[N("Annots")] = annots;
        doc.AddObject(3, 0, page);

        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(3)),
            [N("Count")] = new PdfInteger(1),
        });

        var catalog = new PdfDictionary
        {
            [N("Type")] = N("Catalog"),
            [N("Pages")] = Ref(2),
        };
        configureCatalog?.Invoke(doc, catalog);
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    /// <summary>A well-formed annotation of <paramref name="subtype"/>: 100×100 /Rect, Print flag set,
    /// a normal (/N-only) appearance. Individual tests mutate it to introduce a single violation.</summary>
    private static PdfDictionary Annot(string subtype, Action<PdfDictionary>? mutate = null)
    {
        var annot = new PdfDictionary
        {
            [N("Type")] = N("Annot"),
            [N("Subtype")] = N(subtype),
            [N("Rect")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(100)),
            [N("F")] = new PdfInteger(4), // Print set, all prohibited flags clear
            [N("AP")] = new PdfDictionary { [N("N")] = Ref(50) },
        };
        mutate?.Invoke(annot);
        return annot;
    }

    private static ConformanceContext Ctx(PdfDocument doc) => new(doc, ConformanceProfile.PdfA2b);

    // ── 6.3.1 AnnotationTypeRule ─────────────────────────────────────────────

    [Fact]
    public void Annotation_type_allows_permitted_subtype()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Text"))));
        Assert.Empty(new AnnotationTypeRule().Check(ctx));
    }

    [Theory]
    [InlineData("Movie")]
    [InlineData("Sound")]
    [InlineData("Screen")]
    [InlineData("3D")]
    public void Annotation_type_flags_prohibited_subtype(string subtype)
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot(subtype))));
        Finding f = Assert.Single(new AnnotationTypeRule().Check(ctx));
        Assert.Equal("annotation-type", f.RuleId);
        Assert.Equal(FindingSeverity.Error, f.Severity);
    }

    [Fact]
    public void Annotation_type_flags_missing_subtype()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Text", a => a.Remove(N("Subtype"))))));
        Assert.Single(new AnnotationTypeRule().Check(ctx));
    }

    // ── 6.3.2 AnnotationFlagsRule ────────────────────────────────────────────

    [Fact]
    public void Annotation_flags_pass_when_print_set_and_others_clear()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Text"))));
        Assert.Empty(new AnnotationFlagsRule().Check(ctx));
    }

    [Fact]
    public void Annotation_flags_flag_missing_F_on_non_popup()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Text", a => a.Remove(N("F"))))));
        Assert.Single(new AnnotationFlagsRule().Check(ctx));
    }

    [Fact]
    public void Annotation_flags_allow_missing_F_on_popup()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Popup", a => a.Remove(N("F"))))));
        Assert.Empty(new AnnotationFlagsRule().Check(ctx));
    }

    [Theory]
    [InlineData(0)]   // Print not set
    [InlineData(2)]   // Hidden set
    [InlineData(1)]   // Invisible set
    [InlineData(4 | 32)]  // NoView set alongside Print
    public void Annotation_flags_flag_bad_flag_bits(int flags)
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Text", a => a[N("F")] = new PdfInteger(flags)))));
        Assert.Single(new AnnotationFlagsRule().Check(ctx));
    }

    // ── 6.3.3 AnnotationAppearanceRule ───────────────────────────────────────

    [Fact]
    public void Annotation_appearance_passes_with_normal_appearance()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Text"))));
        Assert.Empty(new AnnotationAppearanceRule().Check(ctx));
    }

    [Fact]
    public void Annotation_appearance_flags_missing_AP()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Text", a => a.Remove(N("AP"))))));
        Assert.Single(new AnnotationAppearanceRule().Check(ctx));
    }

    [Fact]
    public void Annotation_appearance_exempts_popup_and_zero_sized()
    {
        var popup = Annot("Popup", a => a.Remove(N("AP")));
        var zero = Annot("Text", a =>
        {
            a.Remove(N("AP"));
            a[N("Rect")] = new PdfArray(new PdfInteger(10), new PdfInteger(10), new PdfInteger(10), new PdfInteger(10));
        });
        var ctx = Ctx(DocWith(new PdfArray(popup, zero)));
        Assert.Empty(new AnnotationAppearanceRule().Check(ctx));
    }

    [Fact]
    public void Annotation_appearance_flags_extra_appearance_states()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Text", a =>
            a[N("AP")] = new PdfDictionary { [N("N")] = Ref(50), [N("D")] = Ref(51) }))));
        Assert.Single(new AnnotationAppearanceRule().Check(ctx));
    }

    // ── 6.4.1 FormFieldActionsRule ───────────────────────────────────────────

    [Fact]
    public void Form_widget_with_action_is_flagged()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Widget", a => a[N("A")] = new PdfDictionary { [N("S")] = N("URI") }))));
        Finding f = Assert.Single(new FormFieldActionsRule().Check(ctx));
        Assert.Equal("form-field-actions", f.RuleId);
    }

    [Fact]
    public void Form_clean_widget_passes()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Widget"))));
        Assert.Empty(new FormFieldActionsRule().Check(ctx));
    }

    [Fact]
    public void Form_field_with_additional_actions_is_flagged()
    {
        var ctx = Ctx(DocWith(configureCatalog: (doc, cat) =>
        {
            doc.AddObject(30, 0, new PdfDictionary { [N("AA")] = new PdfDictionary() });
            cat[N("AcroForm")] = new PdfDictionary { [N("Fields")] = new PdfArray(Ref(30)) };
        }));
        Finding f = Assert.Single(new FormFieldActionsRule().Check(ctx));
        Assert.Equal("form-field-actions", f.RuleId);
    }

    // ── 6.4.1/6.4.2 FormConfigRule ───────────────────────────────────────────

    [Fact]
    public void Form_need_appearances_true_is_flagged()
    {
        var ctx = Ctx(DocWith(configureCatalog: (_, cat) =>
            cat[N("AcroForm")] = new PdfDictionary { [N("NeedAppearances")] = PdfBoolean.FromValue(true) }));
        Assert.Contains(new FormConfigRule().Check(ctx), f => f.Clause.Contains("6.4.1"));
    }

    [Fact]
    public void Form_xfa_is_flagged()
    {
        var ctx = Ctx(DocWith(configureCatalog: (_, cat) =>
            cat[N("AcroForm")] = new PdfDictionary { [N("XFA")] = new PdfArray() }));
        Assert.Contains(new FormConfigRule().Check(ctx), f => f.Clause.Contains("6.4.2"));
    }

    [Fact]
    public void Catalog_needs_rendering_true_is_flagged()
    {
        var ctx = Ctx(DocWith(configureCatalog: (_, cat) => cat[N("NeedsRendering")] = PdfBoolean.FromValue(true)));
        Assert.Single(new FormConfigRule().Check(ctx));
    }

    [Fact]
    public void Form_config_passes_with_no_acroform()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Text"))));
        Assert.Empty(new FormConfigRule().Check(ctx));
    }

    // ── 6.5.1 ActionTypeRule ─────────────────────────────────────────────────

    [Fact]
    public void Action_permitted_type_passes()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Link", a => a[N("A")] = new PdfDictionary { [N("S")] = N("GoTo") }))));
        Assert.Empty(new ActionTypeRule().Check(ctx));
    }

    [Fact]
    public void Action_prohibited_type_on_annotation_is_flagged()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Link", a => a[N("A")] = new PdfDictionary { [N("S")] = N("Launch") }))));
        Finding f = Assert.Single(new ActionTypeRule().Check(ctx));
        Assert.Equal("action-type", f.RuleId);
    }

    [Fact]
    public void Action_named_with_disallowed_name_is_flagged()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Link", a =>
            a[N("A")] = new PdfDictionary { [N("S")] = N("Named"), [N("N")] = N("GoForward") }))));
        Assert.Single(new ActionTypeRule().Check(ctx));
    }

    [Fact]
    public void Action_on_outline_item_is_flagged()
    {
        var ctx = Ctx(DocWith(configureCatalog: (doc, cat) =>
        {
            doc.AddObject(20, 0, new PdfDictionary { [N("Type")] = N("Outlines"), [N("First")] = Ref(21) });
            doc.AddObject(21, 0, new PdfDictionary { [N("A")] = new PdfDictionary { [N("S")] = N("Movie") } });
            cat[N("Outlines")] = Ref(20);
        }));
        Assert.Single(new ActionTypeRule().Check(ctx));
    }

    [Fact]
    public void Action_javascript_in_names_tree_is_flagged()
    {
        var ctx = Ctx(DocWith(configureCatalog: (_, cat) =>
            cat[N("Names")] = new PdfDictionary
            {
                [N("JavaScript")] = new PdfDictionary
                {
                    [N("Names")] = new PdfArray(
                        new PdfString(System.Text.Encoding.ASCII.GetBytes("s")),
                        new PdfDictionary { [N("S")] = N("JavaScript") }),
                },
            }));
        Assert.Single(new ActionTypeRule().Check(ctx));
    }

    // ── 6.5.2 AdditionalActionsRule ──────────────────────────────────────────

    [Fact]
    public void Catalog_additional_actions_is_flagged()
    {
        var ctx = Ctx(DocWith(configureCatalog: (_, cat) => cat[N("AA")] = new PdfDictionary()));
        Assert.Single(new AdditionalActionsRule().Check(ctx));
    }

    [Fact]
    public void Page_additional_actions_is_flagged()
    {
        // Put /AA on the page dictionary (object 3).
        var doc = DocWith(new PdfArray(Annot("Text")));
        ((PdfDictionary)doc.GetObject(3)!)[N("AA")] = new PdfDictionary();
        Finding f = Assert.Single(new AdditionalActionsRule().Check(Ctx(doc)));
        Assert.Equal("additional-actions", f.RuleId);
    }

    // ── review-hardening edge cases (not exercised by the corpus) ────────────

    [Fact] // fix: an action dictionary with no /S must be flagged, not silently skipped
    public void Action_dictionary_without_S_type_is_flagged()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Link", a => a[N("A")] = new PdfDictionary()))));
        Assert.Single(new ActionTypeRule().Check(ctx));
    }

    [Fact] // fix: an empty /AP violates "only /N"
    public void Annotation_appearance_flags_empty_AP()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Text", a => a[N("AP")] = new PdfDictionary()))));
        Assert.Single(new AnnotationAppearanceRule().Check(ctx));
    }

    [Fact] // fix: an /AP with a non-/N key (not just /D or /R) violates "only /N"
    public void Annotation_appearance_flags_non_N_key()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Text", a =>
            a[N("AP")] = new PdfDictionary { [N("X")] = Ref(50) }))));
        Assert.Single(new AnnotationAppearanceRule().Check(ctx));
    }

    [Fact] // fix: an absent /Rect must NOT be treated as zero-sized (appearance still required)
    public void Annotation_appearance_required_when_Rect_absent()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Text", a => { a.Remove(N("AP")); a.Remove(N("Rect")); }))));
        Assert.Single(new AnnotationAppearanceRule().Check(ctx));
    }

    [Fact] // fix: a present-but-non-integer /F counts as present (no false "missing /F")
    public void Annotation_flags_non_integer_F_is_present()
    {
        var ctx = Ctx(DocWith(new PdfArray(Annot("Text", a => a[N("F")] = new PdfReal(4.0)))));
        Assert.Empty(new AnnotationFlagsRule().Check(ctx));
    }

    // ── end-to-end ───────────────────────────────────────────────────────────

    [Fact]
    public void Clean_document_still_conforms_through_slice7()
    {
        using PdfDocument doc = ConformanceFixtures.CleanConformantDoc();
        PreflightResult result = Preflighter.Check(doc, ConformanceProfile.PdfA2b);
        Assert.True(result.Conforms);
        Assert.DoesNotContain(result.Findings, f => f.RuleId is
            "annotation-type" or "annotation-flags" or "annotation-appearance"
            or "form-field-actions" or "form-config" or "action-type" or "additional-actions");
    }
}
