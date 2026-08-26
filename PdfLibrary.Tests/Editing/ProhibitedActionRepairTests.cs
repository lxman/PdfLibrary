using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>Tests for <see cref="PdfDocumentEditor.RepairProhibitedActions"/> and
/// <see cref="PdfDocumentEditor.PreviewProhibitedActionRepairs"/> -- the PDF/A clause 6.5.1
/// prohibited-action remediation program (<c>PdfLibrary.Conformance.Rules.ActionTypeRule</c>). Task 1
/// covers Link and Widget <c>/A</c> only -- the reference is removed, never the annotation and never
/// the action object itself (measured fact: one corpus document shares an action object between two
/// widgets). Task 2 extends the same classifier with Widget <c>/AA</c> and the <c>/Names /JavaScript</c>
/// name tree.</summary>
public sealed class ProhibitedActionRepairTests
{
    // ---- Fixture builders (mirrors AnnotationAppearanceRepairTests' convention) ------------------

    private static readonly PdfName AKey = new("A");
    private static readonly PdfName SKey = new("S");
    private static readonly PdfName NKey = new("N");
    private static readonly PdfName NextKey = new("Next");
    private static readonly PdfName AnnotsKey = new("Annots");

    private static PdfDocumentEditor NewEditor()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("x", 72, 700, "Helvetica", 12));
        byte[] bytes = builder.ToByteArray();
        return PdfDocumentEditor.Open(new MemoryStream(bytes));
    }

    private static void AddAnnotEntry(PdfDocument doc, int pageIndex, PdfObject entry)
    {
        PdfDictionary page = PageTreeOps.PageDicts(doc)[pageIndex];
        if (page.Get(AnnotsKey) is PdfArray existing)
            existing.Add(entry);
        else
            page[AnnotsKey] = new PdfArray(entry);
    }

    private static PdfDictionary MakeLink(PdfObject? actionValue) => new()
    {
        [new PdfName("Subtype")] = new PdfName("Link"),
        [new PdfName("Rect")] = new PdfArray(
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(20)),
        [AKey] = actionValue!,
    };

    /// <summary>A Link annotation whose direct (not indirect) <c>/A</c> has the given <c>/S</c> and no
    /// other keys -- the shape of 444 of the measured 1365 findings (task brief measured fact 1).</summary>
    private static PdfDocumentEditor NewEditorWithLinkAction(string actionType)
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary link = MakeLink(new PdfDictionary { [SKey] = new PdfName(actionType) });
        AddAnnotEntry(doc, 0, doc.RegisterObject(link));

        return editor;
    }

    /// <summary>A Link whose <c>/A</c> is a prohibited Launch action carrying a <c>/Next</c> pointing at
    /// a PERMITTED GoTo action -- removing the host's <c>/A</c> key would silently drop the chained GoTo
    /// PDF/A permits (measured fact: no real corpus document has a <c>/Next</c> chain, so this shape must
    /// be proven by a constructed fixture).</summary>
    private static PdfDocumentEditor NewEditorWithChainedAction()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var chainedGoTo = new PdfDictionary { [SKey] = new PdfName("GoTo") };
        var launchWithNext = new PdfDictionary
        {
            [SKey] = new PdfName("Launch"),
            [NextKey] = chainedGoTo,
        };
        PdfDictionary link = MakeLink(launchWithNext);
        AddAnnotEntry(doc, 0, doc.RegisterObject(link));

        return editor;
    }

    /// <summary>A Link whose <c>/A</c> is <c>/S /Named /N &lt;name&gt;</c> -- permitted only when
    /// <paramref name="name"/> is one of NextPage/PrevPage/FirstPage/LastPage.</summary>
    private static PdfDocumentEditor NewEditorWithNamedAction(string name)
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary link = MakeLink(
            new PdfDictionary { [SKey] = new PdfName("Named"), [NKey] = new PdfName(name) });
        AddAnnotEntry(doc, 0, doc.RegisterObject(link));

        return editor;
    }

    /// <summary>Two Link annotations on the same page, each with its own direct <c>/A</c> -- for the
    /// host-selector test. Order in the page's <c>/Annots</c> array matches the order the two action
    /// types are given.</summary>
    private static PdfDocumentEditor NewEditorWithTwoLinkActions(string actionTypeA, string actionTypeB)
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary linkA = MakeLink(new PdfDictionary { [SKey] = new PdfName(actionTypeA) });
        AddAnnotEntry(doc, 0, doc.RegisterObject(linkA));

        PdfDictionary linkB = MakeLink(new PdfDictionary { [SKey] = new PdfName(actionTypeB) });
        AddAnnotEntry(doc, 0, doc.RegisterObject(linkB));

        return editor;
    }

    private static PdfObject Resolve(PdfDocument doc, PdfObject entry) =>
        entry is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber)! : entry;

    /// <summary>The one (and only) annotation on page 0 -- for fixtures built with exactly one.</summary>
    private static PdfDictionary SingleAnnotation(PdfDocumentEditor editor)
    {
        PdfDocument doc = editor.Document;
        var annots = (PdfArray)PageTreeOps.PageDicts(doc)[0].Get(AnnotsKey)!;
        return (PdfDictionary)Resolve(doc, Assert.Single(annots));
    }

    /// <summary>Both annotations on page 0, in <c>/Annots</c> order -- for fixtures built with two.</summary>
    private static (PdfDictionary A, PdfDictionary B) TwoAnnotations(PdfDocumentEditor editor)
    {
        PdfDocument doc = editor.Document;
        var annots = (PdfArray)PageTreeOps.PageDicts(doc)[0].Get(AnnotsKey)!;
        Assert.Equal(2, annots.Count);
        return ((PdfDictionary)Resolve(doc, annots[0]), (PdfDictionary)Resolve(doc, annots[1]));
    }

    // ---- Tests --------------------------------------------------------------------------------

    [Fact]
    public void Repair_drops_A_from_a_Link_and_leaves_the_annotation_intact()
    {
        PdfDocumentEditor editor = NewEditorWithLinkAction("Launch");
        PdfDictionary link = SingleAnnotation(editor);
        int annotObj = link.ObjectNumber;

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Single(report.Repaired);
        Assert.Empty(report.Refused);
        Assert.Equal(annotObj, report.Repaired[0].Site.HostObjectNumber);
        Assert.False(link.ContainsKey(new PdfName("A")));      // action reference gone
        Assert.True(link.ContainsKey(new PdfName("Rect")));    // annotation itself untouched
        Assert.Equal("Link", ((PdfName)link.Get("Subtype")!).Value);
    }

    [Fact]
    public void Repair_refuses_an_action_carrying_a_Next_chain()
    {
        // /A -> Launch with /Next -> a PERMITTED GoTo. Removing /A would take the GoTo with it.
        PdfDocumentEditor editor = NewEditorWithChainedAction();

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Contains("/Next", refusal.Reason, StringComparison.Ordinal);
        Assert.True(SingleAnnotation(editor).ContainsKey(new PdfName("A")));  // untouched
    }

    [Fact]
    public void Repair_leaves_a_permitted_action_untouched_with_no_rewrite()
    {
        PdfDocumentEditor editor = NewEditorWithLinkAction("GoTo");
        PdfDictionary link = SingleAnnotation(editor);
        PdfObject before = link.Get(new PdfName("A"))!;

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        Assert.Empty(report.Refused);
        Assert.Same(before, link.Get(new PdfName("A")));   // same instance: no gratuitous rewrite
    }

    [Fact]
    public void Repair_refuses_a_disallowed_Named_action_nothing_and_drops_it()
    {
        // /S /Named with /N /GoBack -- prohibited by test 2, and the single largest real population.
        PdfDocumentEditor editor = NewEditorWithNamedAction("GoBack");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Single(report.Repaired);
        Assert.Equal(ProhibitedActionKind.DisallowedNamed, report.Repaired[0].Site.Kind);
        Assert.False(SingleAnnotation(editor).ContainsKey(new PdfName("A")));
    }

    [Fact]
    public void Repair_leaves_a_permitted_Named_action_alone()
    {
        PdfDocumentEditor editor = NewEditorWithNamedAction("FirstPage");
        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();
        Assert.Empty(report.Repaired);
        Assert.True(SingleAnnotation(editor).ContainsKey(new PdfName("A")));
    }

    [Fact]
    public void Repair_honours_the_host_selector_and_leaves_siblings_alone()
    {
        PdfDocumentEditor editor = NewEditorWithTwoLinkActions("Launch", "Launch");
        (PdfDictionary a, PdfDictionary b) = TwoAnnotations(editor);

        editor.RepairProhibitedActions(hostObjectNumbers: new HashSet<int> { a.ObjectNumber },
                                      javaScriptEntryNames: new HashSet<string>());

        Assert.False(a.ContainsKey(new PdfName("A")));
        Assert.True(b.ContainsKey(new PdfName("A")));   // NOT selected -> untouched
    }

    [Fact]
    public void Preview_and_repair_agree_about_every_site()
    {
        PdfDocumentEditor editor = NewEditorWithLinkAction("Launch");
        ProhibitedActionRepairPreview preview = editor.PreviewProhibitedActionRepairs();
        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Equal(preview.Candidates.Count, report.Repaired.Count);
        Assert.Equal(preview.Candidates[0].HostObjectNumber, report.Repaired[0].Site.HostObjectNumber);
        Assert.Equal(preview.Refused.Count, report.Refused.Count);
    }
}
