using PdfLibrary.Builder;
using PdfLibrary.Conformance;
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
/// name tree, the five hosts it deliberately refuses, and the /Next chain a permitted head can
/// hide.</summary>
public sealed class ProhibitedActionRepairTests
{
    // ---- Fixture builders (mirrors AnnotationAppearanceRepairTests' convention) ------------------

    private static readonly PdfName AKey = new("A");
    private static readonly PdfName SKey = new("S");
    private static readonly PdfName NKey = new("N");
    private static readonly PdfName NextKey = new("Next");
    private static readonly PdfName AnnotsKey = new("Annots");
    private static readonly PdfName AAKey = new("AA");
    private static readonly PdfName NamesKey = new("Names");
    private static readonly PdfName JavaScriptKey = new("JavaScript");

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

    /// <summary>A bare annotation of the given subtype with a /Rect and nothing else -- the shape every
    /// fixture below starts from, so a test asserting the annotation SURVIVED a repair has a key
    /// (/Rect) to assert on that the repair never touches.</summary>
    private static PdfDictionary MakeAnnotation(string subtype) => new()
    {
        [new PdfName("Subtype")] = new PdfName(subtype),
        [new PdfName("Rect")] = new PdfArray(
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(100), new PdfInteger(20)),
    };

    private static PdfDictionary MakeLink(PdfObject? actionValue)
    {
        PdfDictionary link = MakeAnnotation("Link");
        link[AKey] = actionValue!;
        return link;
    }

    /// <summary>An action dictionary with just an /S -- enough for the classifier, which reads /S (and
    /// /N for a Named action) and nothing else.</summary>
    private static PdfDictionary MakeAction(string actionType) =>
        new() { [SKey] = new PdfName(actionType) };

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
    public void Repair_removes_a_disallowed_Named_action_and_drops_it()
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
        // The fixture must carry all three outcomes at once. Against a one-candidate/zero-refusal
        // document the assertions below reduce to 1 == 1 and 0 == 0, which agrees just as well with a
        // preview and a repair that share nothing at all.
        PdfDocumentEditor editor = NewEditorWithARepairARefusalAndAnUnmeasuredHost();

        ProhibitedActionRepairPreview preview = editor.PreviewProhibitedActionRepairs();
        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Single(preview.Candidates);       // the repairable Link /A
        Assert.Equal(2, preview.Refused.Count);  // the /Next chain + the catalog /OpenAction

        Assert.Equal(preview.Candidates.Select(s => s.HostDescription),
                     report.Repaired.Select(r => r.Site.HostDescription));
        Assert.Equal(preview.Candidates.Select(s => s.HostObjectNumber),
                     report.Repaired.Select(r => r.Site.HostObjectNumber));
        Assert.Equal(preview.Refused.Select(r => r.Site.HostDescription),
                     report.Refused.Select(r => r.Site.HostDescription));
        Assert.Equal(preview.Refused.Select(r => r.Reason), report.Refused.Select(r => r.Reason));
    }

    // ---- Task 2 fixtures: /AA triggers, the /Names /JavaScript tree, the unmeasured hosts --------

    private static PdfObject? ResolveOrNull(PdfDocument doc, PdfObject? entry) =>
        entry is null ? null : Resolve(doc, entry);

    /// <summary>Saves the edited document and re-opens it, handing <paramref name="read"/> the reloaded
    /// document. Everything the caller needs must be read INSIDE the callback: the reloaded document is
    /// disposed on the way out, and a reference cannot be resolved after that.
    ///
    /// <para>Why save-and-reload at all, when <c>InternalsVisibleTo("PdfLibrary.Tests")</c> makes
    /// <c>editor.Document.CatalogDictionary</c> directly readable? Because the claim under test is about
    /// the DOCUMENT, not about one in-memory dictionary: a repair that removed a name-tree entry from a
    /// node the writer then re-serialised from somewhere else would pass an in-memory assertion and still
    /// ship the script. This is also how the sibling domain tests prove persistence.</para></summary>
    private static T SaveAndReload<T>(PdfDocumentEditor editor, Func<PdfDocument, T> read)
    {
        var saved = new MemoryStream();
        editor.Save(saved);
        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(saved.ToArray()));
        return read(reloaded);
    }

    /// <summary>The reloaded document's catalog. Safe to hold past the reloaded document's lifetime for
    /// <c>ContainsKey</c>, which reads an already-materialised dictionary and resolves nothing.</summary>
    private static PdfDictionary SaveAndReloadCatalog(PdfDocumentEditor editor) =>
        SaveAndReload(editor, d => d.CatalogDictionary!);

    /// <summary>The reloaded document's catalog <c>/Names</c> dictionary -- resolved inside the reloaded
    /// document's lifetime, so the caller may only <c>ContainsKey</c> it afterwards.</summary>
    private static PdfDictionary SaveAndReloadCatalogNames(PdfDocumentEditor editor) =>
        SaveAndReload(editor, d =>
            (PdfDictionary)ResolveOrNull(d, d.CatalogDictionary?.Get(NamesKey))!);

    /// <summary>A single indirect Widget whose <c>/AA</c> carries a prohibited JavaScript trigger and,
    /// when <paramref name="permittedTrigger"/> is given, a permitted GoTo one alongside it -- the shape
    /// that separates "drop the offending trigger" from "drop the whole /AA".</summary>
    private static PdfDocumentEditor NewEditorWithWidgetAA(string prohibitedTrigger, string? permittedTrigger)
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var aa = new PdfDictionary { [new PdfName(prohibitedTrigger)] = MakeAction("JavaScript") };
        if (permittedTrigger is not null)
            aa[new PdfName(permittedTrigger)] = MakeAction("GoTo");

        PdfDictionary widget = MakeAnnotation("Widget");
        widget[AAKey] = aa;
        AddAnnotEntry(doc, 0, doc.RegisterObject(widget));

        return editor;
    }

    /// <summary>Two indirect Widgets whose <c>/AA /E</c> both point at the SAME indirect JavaScript
    /// action -- the measured shape in <c>2025_PIV-Card</c>. The repair must drop BOTH references and
    /// must never delete the action object, which would reach a host the caller never selected.</summary>
    private static PdfDocumentEditor NewEditorWithSharedAction()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference shared = doc.RegisterObject(MakeAction("JavaScript"));
        for (var i = 0; i < 2; i++)
        {
            PdfDictionary widget = MakeAnnotation("Widget");
            widget[AAKey] = new PdfDictionary { [new PdfName("E")] = shared };
            AddAnnotEntry(doc, 0, doc.RegisterObject(widget));
        }

        return editor;
    }

    /// <summary>Replaces the catalog's <c>/Names</c> with a single-leaf <c>/JavaScript</c> name tree, one
    /// indirect JavaScript action per given entry name, in the order given.</summary>
    private static void SetJavaScriptNameTree(PdfDocument doc, params string[] entryNames)
    {
        var pairs = new PdfArray();
        foreach (string entryName in entryNames)
        {
            pairs.Add(PdfString.FromText(entryName));
            PdfDictionary action = MakeAction("JavaScript");
            action[new PdfName("JS")] = PdfString.FromText("app.alert(0);");
            pairs.Add(doc.RegisterObject(action));
        }

        var leaf = new PdfDictionary { [NamesKey] = pairs };
        doc.CatalogDictionary![NamesKey] = new PdfDictionary { [JavaScriptKey] = leaf };
    }

    private static PdfDocumentEditor NewEditorWithDocumentJavaScript(string entryName)
    {
        PdfDocumentEditor editor = NewEditor();
        SetJavaScriptNameTree(editor.Document, entryName);
        return editor;
    }

    private static PdfDocumentEditor NewEditorWithTwoJavaScriptEntries()
    {
        PdfDocumentEditor editor = NewEditor();
        SetJavaScriptNameTree(editor.Document, "EntryOne", "EntryTwo");
        return editor;
    }

    /// <summary>A catalog <c>/OpenAction</c> holding a direct action dictionary of the given type -- an
    /// unmeasured host (zero occurrences across all 708 corpus documents), refused rather than
    /// repaired.</summary>
    private static PdfDocumentEditor NewEditorWithCatalogOpenAction(string actionType)
    {
        PdfDocumentEditor editor = NewEditor();
        editor.Document.CatalogDictionary![new PdfName("OpenAction")] = MakeAction(actionType);
        return editor;
    }

    private static PdfDocumentEditor NewEditorWithCatalogAA(string trigger, string actionType)
    {
        PdfDocumentEditor editor = NewEditor();
        editor.Document.CatalogDictionary![AAKey] =
            new PdfDictionary { [new PdfName(trigger)] = MakeAction(actionType) };
        return editor;
    }

    private static PdfDocumentEditor NewEditorWithPageAA(string trigger, string actionType)
    {
        PdfDocumentEditor editor = NewEditor();
        PageTreeOps.PageDicts(editor.Document)[0][AAKey] =
            new PdfDictionary { [new PdfName(trigger)] = MakeAction(actionType) };
        return editor;
    }

    /// <summary>A one-item outline tree whose item carries a prohibited <c>/A</c>. Returns the item's
    /// object number so the refusal's host description can be asserted exactly.</summary>
    private static (PdfDocumentEditor Editor, int ItemObjectNumber) NewEditorWithOutlineAction(string actionType)
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary item = new()
        {
            [new PdfName("Title")] = PdfString.FromText("An outline item"),
            [AKey] = MakeAction(actionType),
        };
        PdfIndirectReference itemRef = doc.RegisterObject(item);
        var outlines = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Outlines"),
            [new PdfName("First")] = itemRef,
            [new PdfName("Last")] = itemRef,
            [new PdfName("Count")] = new PdfInteger(1),
        };
        doc.CatalogDictionary![new PdfName("Outlines")] = doc.RegisterObject(outlines);

        return (editor, itemRef.ObjectNumber);
    }

    /// <summary>An AcroForm whose single field is PURE -- a field dictionary that is not also an
    /// annotation, so it is absent from every page's <c>/Annots</c> and invisible to the annotation walk.
    /// Zero of these carry a prohibited action anywhere in the measured population, which is exactly why
    /// silence here would be indistinguishable from success.</summary>
    private static (PdfDocumentEditor Editor, int FieldObjectNumber) NewEditorWithPureField(string actionType)
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary field = new()
        {
            [new PdfName("FT")] = new PdfName("Btn"),
            [new PdfName("T")] = PdfString.FromText("PureField"),
            [AKey] = MakeAction(actionType),
        };
        PdfIndirectReference fieldRef = doc.RegisterObject(field);
        doc.CatalogDictionary![new PdfName("AcroForm")] =
            new PdfDictionary { [new PdfName("Fields")] = new PdfArray(fieldRef) };

        return (editor, fieldRef.ObjectNumber);
    }

    private static PdfDocumentEditor NewEditorWithLinkActionAndCatalogOpenAction()
    {
        PdfDocumentEditor editor = NewEditorWithLinkAction("Launch");
        editor.Document.CatalogDictionary![new PdfName("OpenAction")] = MakeAction("Launch");
        return editor;
    }

    /// <summary>A Link whose <c>/A</c> is a PERMITTED GoTo carrying a <c>/Next</c> pointing at a
    /// prohibited Launch. The rule collects the whole chain, so it raises a finding here -- a classifier
    /// reading the head only would put this site in neither list.</summary>
    private static PdfDocumentEditor NewEditorWithPermittedHeadAndProhibitedNext()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary goToWithNext = MakeAction("GoTo");
        goToWithNext[NextKey] = MakeAction("Launch");
        AddAnnotEntry(doc, 0, doc.RegisterObject(MakeLink(goToWithNext)));

        return editor;
    }

    // ---- Task 2 tests: /AA triggers ------------------------------------------------------------

    [Fact]
    public void Repair_drops_only_the_offending_AA_triggers()
    {
        // /AA = { /E -> JavaScript (prohibited), /X -> GoTo (permitted) }
        PdfDocumentEditor editor = NewEditorWithWidgetAA(prohibitedTrigger: "E", permittedTrigger: "X");
        PdfDictionary widget = SingleAnnotation(editor);

        editor.RepairProhibitedActions();

        var aa = (PdfDictionary)widget.Get(AAKey)!;
        Assert.False(aa.ContainsKey(new PdfName("E")));   // prohibited trigger gone
        Assert.True(aa.ContainsKey(new PdfName("X")));    // permitted trigger kept
    }

    [Fact]
    public void Repair_removes_AA_itself_only_when_it_empties()
    {
        PdfDocumentEditor editor = NewEditorWithWidgetAA(prohibitedTrigger: "E", permittedTrigger: null);
        PdfDictionary widget = SingleAnnotation(editor);

        editor.RepairProhibitedActions();

        Assert.False(widget.ContainsKey(AAKey));
        Assert.True(widget.ContainsKey(new PdfName("Rect")));   // the widget itself survives
    }

    [Fact]
    public void Repair_names_the_offending_trigger_in_the_site_description()
    {
        PdfDocumentEditor editor = NewEditorWithWidgetAA(prohibitedTrigger: "E", permittedTrigger: "X");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        ProhibitedActionRepair repaired = Assert.Single(report.Repaired);
        Assert.Equal("Widget /AA /E", repaired.Site.HostDescription);
        Assert.Equal(ProhibitedActionKind.JavaScript, repaired.Site.Kind);
    }

    [Fact]
    public void Repair_drops_both_references_to_a_SHARED_action_object()
    {
        // Two widgets whose /AA /E point at the SAME indirect JavaScript action.
        PdfDocumentEditor editor = NewEditorWithSharedAction();
        (PdfDictionary a, PdfDictionary b) = TwoAnnotations(editor);

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Equal(2, report.Repaired.Count);            // one site per HOST, not per action object
        Assert.False(a.ContainsKey(AAKey));
        Assert.False(b.ContainsKey(AAKey));
    }

    // ---- Task 2 tests: the /Names /JavaScript name tree ----------------------------------------

    [Fact]
    public void Repair_removes_a_JavaScript_name_tree_entry_and_prunes_an_empty_tree()
    {
        PdfDocumentEditor editor = NewEditorWithDocumentJavaScript("EntryOne");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        ProhibitedActionRepair repaired = Assert.Single(report.Repaired);
        Assert.Equal("EntryOne", repaired.Site.JavaScriptEntryName);
        Assert.Null(repaired.Site.HostObjectNumber);

        // The site is document-level, so there is no host dictionary a test could inspect: observe by
        // saving and re-opening, which also proves the removal survived serialisation.
        PdfDictionary reloadedNames = SaveAndReloadCatalogNames(editor);
        Assert.False(reloadedNames.ContainsKey(JavaScriptKey));   // tree emptied -> pruned
    }

    [Fact]
    public void Repair_keeps_the_JavaScript_tree_when_a_permitted_sibling_entry_remains()
    {
        // "Permitted" here means "not staged by this caller": every JavaScript entry is prohibited by
        // 6.5.1-t1, so the only way a sibling survives a repair is the selector. That is the shape that
        // must NOT prune the tree.
        PdfDocumentEditor editor = NewEditorWithTwoJavaScriptEntries();
        editor.RepairProhibitedActions(hostObjectNumbers: new HashSet<int>(),
                                       javaScriptEntryNames: new HashSet<string> { "EntryOne" });

        PdfDictionary reloadedNames = SaveAndReloadCatalogNames(editor);
        Assert.True(reloadedNames.ContainsKey(JavaScriptKey));   // sibling still there
    }

    [Fact]
    public void Repair_removes_only_the_selected_JavaScript_entry()
    {
        PdfDocumentEditor editor = NewEditorWithTwoJavaScriptEntries();

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions(
            hostObjectNumbers: new HashSet<int>(),
            javaScriptEntryNames: new HashSet<string> { "EntryOne" });

        Assert.Equal("EntryOne", Assert.Single(report.Repaired).Site.JavaScriptEntryName);

        List<string?> surviving = SaveAndReload(editor, d =>
        {
            var names = (PdfDictionary)ResolveOrNull(d, d.CatalogDictionary?.Get(NamesKey))!;
            var leaf = (PdfDictionary)ResolveOrNull(d, names.Get(JavaScriptKey))!;
            var pairs = (PdfArray)ResolveOrNull(d, leaf.Get(NamesKey))!;
            var result = new List<string?>();
            for (var i = 0; i + 1 < pairs.Count; i += 2)
                result.Add((ResolveOrNull(d, pairs[i]) as PdfString)?.GetText());
            return result;
        });

        Assert.Equal(["EntryTwo"], surviving);
    }

    // ---- Task 2 tests: the five unmeasured hosts, actively walked and refused -------------------

    [Fact]
    public void Repair_refuses_a_prohibited_action_on_the_catalog_OpenAction()
    {
        PdfDocumentEditor editor = NewEditorWithCatalogOpenAction("Launch");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Contains("catalog /OpenAction", refusal.Reason, StringComparison.Ordinal);
        Assert.Equal("catalog /OpenAction", refusal.Site.HostDescription);
        Assert.True(SaveAndReloadCatalog(editor).ContainsKey(new PdfName("OpenAction")));  // untouched
    }

    [Fact]
    public void Repair_refuses_a_prohibited_action_on_the_catalog_AA()
    {
        PdfDocumentEditor editor = NewEditorWithCatalogAA("WC", "JavaScript");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Contains("catalog /AA /WC", refusal.Reason, StringComparison.Ordinal);
        Assert.Equal("catalog /AA /WC", refusal.Site.HostDescription);

        bool triggerSurvived = SaveAndReload(editor, d =>
            ResolveOrNull(d, d.CatalogDictionary?.Get(AAKey)) is PdfDictionary aa
            && aa.ContainsKey(new PdfName("WC")));
        Assert.True(triggerSurvived, "the catalog /AA /WC trigger must survive a refusal");
    }

    [Fact]
    public void Repair_refuses_a_prohibited_action_on_a_page_AA()
    {
        PdfDocumentEditor editor = NewEditorWithPageAA("O", "Launch");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Contains("page 1 /AA /O", refusal.Reason, StringComparison.Ordinal);   // 1-based
        Assert.Equal("page 1 /AA /O", refusal.Site.HostDescription);

        bool triggerSurvived = SaveAndReload(editor, d =>
            ResolveOrNull(d, PageTreeOps.PageDicts(d)[0].Get(AAKey)) is PdfDictionary aa
            && aa.ContainsKey(new PdfName("O")));
        Assert.True(triggerSurvived, "the page /AA /O trigger must survive a refusal");
    }

    [Fact]
    public void Repair_refuses_a_prohibited_action_on_an_outline_item()
    {
        (PdfDocumentEditor editor, int itemObj) = NewEditorWithOutlineAction("Launch");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Contains($"outline item {itemObj} /A", refusal.Reason, StringComparison.Ordinal);
        Assert.Equal($"outline item {itemObj} /A", refusal.Site.HostDescription);

        bool outlineActionSurvived = SaveAndReload(editor, d =>
            ResolveOrNull(d, d.CatalogDictionary?.Get(new PdfName("Outlines"))) is PdfDictionary outlines
            && ResolveOrNull(d, outlines.Get(new PdfName("First"))) is PdfDictionary item
            && item.ContainsKey(AKey));
        Assert.True(outlineActionSurvived, "the outline item's /A must survive a refusal");
    }

    [Fact]
    public void Repair_refuses_a_prohibited_action_on_a_pure_field_dictionary()
    {
        (PdfDocumentEditor editor, int fieldObj) = NewEditorWithPureField("JavaScript");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Contains($"field {fieldObj} /A", refusal.Reason, StringComparison.Ordinal);
        Assert.Equal($"field {fieldObj} /A", refusal.Site.HostDescription);

        bool fieldActionSurvived = SaveAndReload(editor, d =>
            ResolveOrNull(d, d.CatalogDictionary?.Get(new PdfName("AcroForm"))) is PdfDictionary acro
            && ResolveOrNull(d, acro.Get(new PdfName("Fields"))) is PdfArray fields
            && fields.Count == 1
            && ResolveOrNull(d, fields[0]) is PdfDictionary field
            && field.ContainsKey(AKey));
        Assert.True(fieldActionSurvived, "the pure field's /A must survive a refusal");
    }

    [Fact]
    public void A_merged_field_and_widget_is_not_reported_twice()
    {
        // The AcroForm /Fields tree reaches the SAME dictionary the annotation walk already returned --
        // every one of the 78 real form-hosted actions is this shape. It must be repaired once as an
        // annotation, never ALSO refused as a "pure" field.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary widget = MakeAnnotation("Widget");
        widget[new PdfName("FT")] = new PdfName("Btn");
        widget[AKey] = MakeAction("Launch");
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);
        doc.CatalogDictionary![new PdfName("AcroForm")] =
            new PdfDictionary { [new PdfName("Fields")] = new PdfArray(widgetRef) };

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Single(report.Repaired);
        Assert.Empty(report.Refused);
        Assert.False(widget.ContainsKey(AKey));
    }

    [Fact]
    public void Repair_leaves_a_permitted_action_on_an_unmeasured_host_entirely_unreported()
    {
        // A permitted GoTo on the catalog /OpenAction raises no 6.5.1 finding, so a refusal for it would
        // be a false alarm the user cannot act on.
        PdfDocumentEditor editor = NewEditorWithCatalogOpenAction("GoTo");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        Assert.Empty(report.Refused);
    }

    [Fact]
    public void Repair_ignores_a_catalog_OpenAction_that_is_a_destination_array()
    {
        // /OpenAction may legally be a destination array rather than an action dictionary; the rule
        // filters those out (ActionTypeRule.CollectActions :86-89), so this walk must too or every
        // document with one gets a phantom refusal.
        PdfDocumentEditor editor = NewEditor();
        editor.Document.CatalogDictionary![new PdfName("OpenAction")] =
            new PdfArray(new PdfInteger(0), new PdfName("Fit"));

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        Assert.Empty(report.Refused);
    }

    // ---- Task 2 tests: not going quiet ---------------------------------------------------------

    [Fact]
    public void An_unmeasured_host_refusal_survives_a_caller_that_staged_only_other_sites()
    {
        // A document with BOTH a repairable Link /A and a prohibited catalog /OpenAction. The caller
        // stages the link only. The refusal must still come back: it is the caller's one signal that a
        // shape we never measured is present, and a filter it never asked to apply must not hide it.
        PdfDocumentEditor editor = NewEditorWithLinkActionAndCatalogOpenAction();
        int linkObj = SingleAnnotation(editor).ObjectNumber;

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions(
            hostObjectNumbers: new HashSet<int> { linkObj });

        Assert.Single(report.Repaired);
        Assert.Contains(report.Refused, r => r.Reason.Contains("catalog /OpenAction", StringComparison.Ordinal));
    }

    [Fact]
    public void Repair_refuses_a_permitted_action_whose_Next_chain_hides_a_prohibited_one()
    {
        // /A -> GoTo (permitted) with /Next -> Launch (prohibited). Task 1 classified the HEAD only, so
        // this site was invisible to both lists while the rule still raises a finding for it.
        PdfDocumentEditor editor = NewEditorWithPermittedHeadAndProhibitedNext();

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Contains("/Next", refusal.Reason, StringComparison.Ordinal);
        Assert.True(SingleAnnotation(editor).ContainsKey(AKey));   // untouched
    }

    [Fact]
    public void Repair_leaves_a_permitted_action_whose_Next_chain_is_also_permitted_alone()
    {
        // The discrimination check on the test above: an all-permitted chain raises no finding, so it
        // must reach neither list -- otherwise "refuses a hidden prohibited action" would pass just as
        // well against code that refuses every /Next chain it sees.
        PdfDocumentEditor editor = NewEditor();
        PdfDictionary goToWithNext = MakeAction("GoTo");
        goToWithNext[NextKey] = MakeAction("URI");
        AddAnnotEntry(editor.Document, 0, editor.Document.RegisterObject(MakeLink(goToWithNext)));

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        Assert.Empty(report.Refused);
    }

    [Fact]
    public void Preview_reports_the_unmeasured_host_refusals_too()
    {
        PdfDocumentEditor editor = NewEditorWithLinkActionAndCatalogOpenAction();

        ProhibitedActionRepairPreview preview = editor.PreviewProhibitedActionRepairs();

        Assert.Single(preview.Candidates);
        Assert.Contains(preview.Refused, r => r.Site.HostDescription == "catalog /OpenAction");
    }

    // ---- Fix wave (adversarial review): the shapes that reached NEITHER list, and the write hazards
    //
    // Every test below exists because the governing invariant was breakable: a site in Repaired must
    // raise no action-type finding afterwards, and a shape the rule flags must reach Repaired or
    // Refused -- never neither. Each defect gets a case that fails without its fix AND, where the fix
    // could over-fire, a control that fails if it does.

    /// <summary>A <c>/Names /JavaScript</c> tree whose single entry's KEY slot is not a
    /// <c>PdfString</c>. <c>EnumerateNameTree</c> yields a null key for it, while the rule reads the
    /// tree's VALUES only (<c>ConformanceContext.EnumerateNameTree</c>) and flags the action regardless
    /// of what the key slot holds.</summary>
    private static PdfDocumentEditor NewEditorWithUnreadableJavaScriptEntryName(string actionType)
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var pairs = new PdfArray(
            new PdfName("NotAString"),                     // key slot: a NAME, not a string
            doc.RegisterObject(MakeAction(actionType)));
        doc.CatalogDictionary![NamesKey] =
            new PdfDictionary { [JavaScriptKey] = new PdfDictionary { [NamesKey] = pairs } };

        return editor;
    }

    /// <summary>A <c>/Names /JavaScript</c> tree holding <c>[ (X) &lt;permitted GoTo&gt; (X)
    /// &lt;prohibited JavaScript&gt; ]</c> -- one entry name addressing two entries, malformed under
    /// ISO 32000-1 7.9.6. A removal that matches by name alone deletes the FIRST pair, which is the
    /// action PDF/A permits, and leaves the prohibited one standing.</summary>
    private static PdfDocumentEditor NewEditorWithDuplicateJavaScriptEntryName()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var pairs = new PdfArray(
            PdfString.FromText("X"), doc.RegisterObject(MakeAction("GoTo")),
            PdfString.FromText("X"), doc.RegisterObject(MakeAction("JavaScript")));
        doc.CatalogDictionary![NamesKey] =
            new PdfDictionary { [JavaScriptKey] = new PdfDictionary { [NamesKey] = pairs } };

        return editor;
    }

    /// <summary>Every <c>(entry name, /S type)</c> pair the saved document's <c>/Names /JavaScript</c>
    /// leaf still holds, in array order -- so a test can prove WHICH entry survived, not merely how
    /// many did.</summary>
    private static List<(string? Name, string? ActionType)> SaveAndReloadJavaScriptEntries(
        PdfDocumentEditor editor) =>
        SaveAndReload(editor, d =>
        {
            var names = (PdfDictionary)ResolveOrNull(d, d.CatalogDictionary?.Get(NamesKey))!;
            var leaf = (PdfDictionary)ResolveOrNull(d, names.Get(JavaScriptKey))!;
            var pairs = (PdfArray)ResolveOrNull(d, leaf.Get(NamesKey))!;
            var result = new List<(string? Name, string? ActionType)>();
            for (var i = 0; i + 1 < pairs.Count; i += 2)
            {
                string? name = (ResolveOrNull(d, pairs[i]) as PdfString)?.GetText();
                string? type = ResolveOrNull(d, pairs[i + 1]) is PdfDictionary action
                    ? (ResolveOrNull(d, action.Get(SKey)) as PdfName)?.Value
                    : null;
                result.Add((name, type));
            }

            return result;
        });

    /// <summary>A page whose <c>/Annots</c> holds a DIRECT annotation dictionary -- one with no object
    /// number, which <c>EnumerateIndirectAnnotations</c> skips by design and
    /// <c>ConformanceContext.CollectAnnotations</c> does not.</summary>
    private static PdfDocumentEditor NewEditorWithDirectAnnotationAction(string subtype, string actionType)
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDictionary annot = MakeAnnotation(subtype);
        annot[AKey] = MakeAction(actionType);
        AddAnnotEntry(editor.Document, 0, annot);   // NOT RegisterObject'd -- direct
        return editor;
    }

    private static PdfDocumentEditor NewEditorWithDirectAnnotationTrigger(
        string subtype, string trigger, string actionType)
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDictionary annot = MakeAnnotation(subtype);
        annot[AAKey] = new PdfDictionary { [new PdfName(trigger)] = MakeAction(actionType) };
        AddAnnotEntry(editor.Document, 0, annot);
        return editor;
    }

    /// <summary>Two indirect Widgets whose <c>/AA</c> is the SAME indirect dictionary -- legal, and
    /// distinct from the shared ACTION object the program already handles. Removing a trigger from it
    /// on behalf of one widget removes it from the other too.</summary>
    private static PdfDocumentEditor NewEditorWithSharedTriggerDictionary()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference sharedAA = doc.RegisterObject(
            new PdfDictionary { [new PdfName("E")] = MakeAction("JavaScript") });
        for (var i = 0; i < 2; i++)
        {
            PdfDictionary widget = MakeAnnotation("Widget");
            widget[AAKey] = sharedAA;
            AddAnnotEntry(doc, 0, doc.RegisterObject(widget));
        }

        return editor;
    }

    /// <summary>A Link whose <c>/A</c> is an action of <paramref name="headType"/> carrying the given
    /// <c>/Next</c> value verbatim -- including <c>PdfNull.Instance</c>, which is a PRESENT /Next key
    /// naming no chain at all.</summary>
    private static PdfDocumentEditor NewEditorWithNextValue(string headType, PdfObject nextValue)
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDictionary head = MakeAction(headType);
        head[NextKey] = nextValue;
        AddAnnotEntry(editor.Document, 0, editor.Document.RegisterObject(MakeLink(head)));
        return editor;
    }

    /// <summary>One document carrying all three outcomes at once: a repairable Link <c>/A</c>, a Link
    /// whose <c>/A</c> chain must be refused, and a prohibited catalog <c>/OpenAction</c> (unmeasured
    /// host). The preview/repair agreement test needs this so its refusal assertion is not
    /// <c>0 == 0</c>.</summary>
    private static PdfDocumentEditor NewEditorWithARepairARefusalAndAnUnmeasuredHost()
    {
        PdfDocumentEditor editor = NewEditorWithLinkAction("Launch");
        PdfDocument doc = editor.Document;

        PdfDictionary launchWithPermittedNext = MakeAction("Launch");
        launchWithPermittedNext[NextKey] = MakeAction("GoTo");
        AddAnnotEntry(doc, 0, doc.RegisterObject(MakeLink(launchWithPermittedNext)));

        doc.CatalogDictionary![new PdfName("OpenAction")] = MakeAction("Launch");
        return editor;
    }

    // ---- MAJOR 1: an unreadable name-tree key ---------------------------------------------------

    [Fact]
    public void Repair_refuses_a_prohibited_script_whose_name_tree_key_is_unreadable()
    {
        PdfDocumentEditor editor = NewEditorWithUnreadableJavaScriptEntryName("JavaScript");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Null(refusal.Site.HostObjectNumber);      // unfilterable, like the five unmeasured hosts:
        Assert.Null(refusal.Site.JavaScriptEntryName);   // it genuinely has no address to stage
        Assert.Contains("unreadable entry name", refusal.Site.HostDescription, StringComparison.Ordinal);
        Assert.Equal([(null, "JavaScript")], SaveAndReloadJavaScriptEntries(editor));  // left in place
    }

    [Fact]
    public void Repair_reports_nothing_for_a_PERMITTED_action_whose_name_tree_key_is_unreadable()
    {
        // Discrimination control for the test above: an unreadable key is not itself a finding, so a
        // refusal here would be a false alarm -- and would pass the test above just as well.
        PdfDocumentEditor editor = NewEditorWithUnreadableJavaScriptEntryName("GoTo");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        Assert.Empty(report.Refused);
    }

    // ---- MAJOR 2: a DIRECT annotation ------------------------------------------------------------

    [Fact]
    public void Repair_refuses_a_prohibited_action_on_a_DIRECT_annotation()
    {
        // EnumerateIndirectAnnotations skips a direct annotation (deliberately -- it is shared with two
        // other programs); CollectAnnotations does not, so the rule flags this and we said nothing.
        PdfDocumentEditor editor = NewEditorWithDirectAnnotationAction("Link", "Launch");
        PdfDictionary annot = SingleAnnotation(editor);

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal("direct Link /A", refusal.Site.HostDescription);
        Assert.Null(refusal.Site.HostObjectNumber);
        Assert.Null(refusal.Site.JavaScriptEntryName);
        Assert.True(annot.ContainsKey(AKey), "a refused direct annotation must keep its /A");
    }

    [Fact]
    public void Repair_refuses_a_prohibited_trigger_on_a_DIRECT_annotation()
    {
        PdfDocumentEditor editor = NewEditorWithDirectAnnotationTrigger("Widget", "E", "JavaScript");
        PdfDictionary annot = SingleAnnotation(editor);

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal("direct Widget /AA /E", refusal.Site.HostDescription);
        Assert.True(((PdfDictionary)annot.Get(AAKey)!).ContainsKey(new PdfName("E")));
    }

    [Fact]
    public void Repair_reports_nothing_for_a_permitted_action_on_a_direct_annotation()
    {
        // Discrimination control: the refusal must key off the ACTION being prohibited, not off the
        // host being direct -- otherwise every document with a direct annotation gets a false alarm.
        PdfDocumentEditor editor = NewEditorWithDirectAnnotationAction("Link", "GoTo");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        Assert.Empty(report.Refused);
    }

    // ---- MAJOR 3: a shared /AA dictionary --------------------------------------------------------

    [Fact]
    public void Repair_refuses_a_trigger_whose_AA_dictionary_is_shared_between_annotations()
    {
        PdfDocumentEditor editor = NewEditorWithSharedTriggerDictionary();
        (PdfDictionary a, PdfDictionary b) = TwoAnnotations(editor);

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        Assert.Equal(2, report.Refused.Count);          // one per host, each told why
        Assert.All(report.Refused, r => Assert.Contains("shared", r.Reason, StringComparison.Ordinal));
        Assert.True(SharedTriggers(editor, a).ContainsKey(new PdfName("E")));
        Assert.True(SharedTriggers(editor, b).ContainsKey(new PdfName("E")));
    }

    /// <summary>An annotation's <c>/AA</c>, resolved -- the shared-container fixture makes it an
    /// INDIRECT reference, which is the whole point of the shape and not castable directly.</summary>
    private static PdfDictionary SharedTriggers(PdfDocumentEditor editor, PdfDictionary annot) =>
        (PdfDictionary)Resolve(editor.Document, annot.Get(AAKey)!);

    [Fact]
    public void Repair_refuses_a_shared_AA_trigger_when_only_one_of_its_hosts_is_staged()
    {
        // The hazard in its original form: stage widget A only, and the write would strip the trigger
        // from widget B as well -- while the report said nothing at all about B.
        PdfDocumentEditor editor = NewEditorWithSharedTriggerDictionary();
        (PdfDictionary a, PdfDictionary b) = TwoAnnotations(editor);

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions(
            hostObjectNumbers: new HashSet<int> { a.ObjectNumber },
            javaScriptEntryNames: new HashSet<string>());

        Assert.Empty(report.Repaired);
        Assert.Single(report.Refused);                                      // only the staged host's
        Assert.Equal(a.ObjectNumber, report.Refused[0].Site.HostObjectNumber);
        Assert.True(SharedTriggers(editor, b).ContainsKey(new PdfName("E")),
                    "an unstaged host's trigger must survive");
    }

    [Fact]
    public void Repair_refuses_an_AA_shared_between_an_indirect_and_a_DIRECT_annotation()
    {
        // The seam between the two fixes: the direct annotation is only ever REFUSED, so if the shared
        // scan looks at indirect annotations alone it calls this /AA unshared, repairs it for the
        // indirect widget, and strips the trigger out from under the direct one -- whose refusal has
        // just told the caller it was "left in place". A false report is worse than a silent one.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfIndirectReference sharedAA = doc.RegisterObject(
            new PdfDictionary { [new PdfName("E")] = MakeAction("JavaScript") });

        PdfDictionary indirectWidget = MakeAnnotation("Widget");
        indirectWidget[AAKey] = sharedAA;
        AddAnnotEntry(doc, 0, doc.RegisterObject(indirectWidget));

        PdfDictionary directWidget = MakeAnnotation("Widget");
        directWidget[AAKey] = sharedAA;
        AddAnnotEntry(doc, 0, directWidget);

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        Assert.Equal(2, report.Refused.Count);   // the indirect host's, and the direct host's
        Assert.True(SharedTriggers(editor, indirectWidget).ContainsKey(new PdfName("E")),
                    "a refusal must leave the shared trigger in place");
    }

    // ---- MAJOR 4: a duplicated name-tree entry name ----------------------------------------------

    [Fact]
    public void Repair_refuses_a_duplicated_JavaScript_entry_name_and_keeps_the_permitted_twin()
    {
        // /Names [ (X) <GoTo> (X) <JavaScript> ]. Matching by name alone deletes the GoTo -- an action
        // PDF/A PERMITS -- and reports the JavaScript entry, still standing, as Repaired.
        PdfDocumentEditor editor = NewEditorWithDuplicateJavaScriptEntryName();

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Contains("'X'", refusal.Reason, StringComparison.Ordinal);
        Assert.Null(refusal.Site.JavaScriptEntryName);   // a name addressing two entries is not an address
        Assert.Equal([("X", "GoTo"), ("X", "JavaScript")], SaveAndReloadJavaScriptEntries(editor));
    }

    // ---- MINOR 5: the /Next refusal fired on key presence ----------------------------------------

    [Fact]
    public void Repair_removes_an_action_whose_Next_names_no_chain()
    {
        // A PRESENT /Next key naming nothing to protect -- here an empty chain array. Refusing it was a
        // missed repair whose stated reason (protecting "chained actions that PDF/A permits") named
        // nothing that exists.
        //
        // NOT written as /Next null, which is the shape that reads most naturally: PdfDictionary.Set
        // drops a PdfNull value outright (ISO 32000-1 7.3.9 -- a null-valued entry is equivalent to an
        // absent one), and the parser assigns through the same indexer, so /Next null can never reach a
        // PdfDictionary in this engine at all. The rule cannot see it either, so the two still agree.
        PdfDocumentEditor editor = NewEditorWithNextValue("Launch", new PdfArray());

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Single(report.Repaired);
        Assert.Empty(report.Refused);
        Assert.False(SingleAnnotation(editor).ContainsKey(AKey));
    }

    [Fact]
    public void Repair_removes_an_action_whose_whole_Next_chain_is_prohibited()
    {
        // Launch -> Next -> JavaScript: nothing in the chain is worth protecting, and dropping /A makes
        // the whole chain unreachable, which is exactly the outcome 6.5.1 wants.
        PdfDocumentEditor editor = NewEditorWithNextValue("Launch", MakeAction("JavaScript"));

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Single(report.Repaired);
        Assert.Empty(report.Refused);
        Assert.False(SingleAnnotation(editor).ContainsKey(AKey));
    }

    [Fact]
    public void The_Next_refusal_reason_names_the_permitted_action_it_protects()
    {
        // The don't-over-repair control paired with the two tests above (and green both before and
        // after the fix, as a control should be): this shape stays refused BECAUSE the chain reaches a
        // permitted GoTo. Without it, "refuse only a chain worth protecting" would be satisfied just as
        // well by code that stopped refusing chains altogether.
        PdfDocumentEditor editor = NewEditorWithChainedAction();

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Contains("/Next", refusal.Reason, StringComparison.Ordinal);
        Assert.Contains("permits", refusal.Reason, StringComparison.Ordinal);
    }

    // ---- Final whole-branch review: traversal-budget exhaustion must be reachable and loud -------

    /// <summary>The old bounded walker treated exhaustion as a complete chain. With a prohibited head
    /// and another prohibited node inside the budget, it removed /A even though a permitted GoTo sat
    /// just beyond the cutoff. The internal budget seam makes that latent 100,000-node shape testable.</summary>
    [Fact]
    public void Next_chain_budget_exhaustion_refuses_the_host_instead_of_removing_an_unseen_permitted_action()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDictionary head = MakeAction("Launch");
        PdfDictionary first = MakeAction("JavaScript");
        first[NextKey] = MakeAction("GoTo");
        head[NextKey] = first;
        AddAnnotEntry(editor.Document, 0, editor.Document.RegisterObject(MakeLink(head)));
        editor.ActionWalkBudget = 1;
        Assert.Contains(Preflighter.Check(editor.Document, ConformanceProfile.PdfA2b).Findings,
            finding => finding.RuleId == "action-type");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Contains("safety limit", refusal.Reason, StringComparison.Ordinal);
        Assert.Contains("permitted actions", refusal.Reason, StringComparison.Ordinal);
        Assert.True(SingleAnnotation(editor).ContainsKey(AKey));
    }

    /// <summary>An outline action beyond the test-sized budget is still a live ActionTypeRule finding.
    /// The repair cannot enumerate that site's address, so it returns one unfilterable traversal refusal
    /// rather than an empty report that is indistinguishable from a clean document.</summary>
    [Fact]
    public void Outline_budget_exhaustion_returns_an_explicit_unfilterable_refusal()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;
        PdfDictionary second = new()
        {
            [new PdfName("Title")] = PdfString.FromText("Second"),
            [AKey] = MakeAction("Launch"),
        };
        PdfIndirectReference secondRef = doc.RegisterObject(second);
        PdfDictionary first = new()
        {
            [new PdfName("Title")] = PdfString.FromText("First"),
            [NextKey] = secondRef,
        };
        PdfIndirectReference firstRef = doc.RegisterObject(first);
        doc.CatalogDictionary![new PdfName("Outlines")] = doc.RegisterObject(new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Outlines"),
            [new PdfName("First")] = firstRef,
            [new PdfName("Last")] = secondRef,
            [new PdfName("Count")] = new PdfInteger(2),
        });
        // First item plus its null /First child consume both iterations; the second item stays queued.
        editor.ActionWalkBudget = 2;
        Assert.Contains(Preflighter.Check(doc, ConformanceProfile.PdfA2b).Findings,
            finding => finding.RuleId == "action-type");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal("outline tree traversal", refusal.Site.HostDescription);
        Assert.Null(refusal.Site.HostObjectNumber);
        Assert.Null(refusal.Site.JavaScriptEntryName);
        Assert.Contains("safety limit", refusal.Reason, StringComparison.Ordinal);
        Assert.True(second.ContainsKey(AKey));
    }

    /// <summary>The field-tree counterpart to the outline test. The child action is beyond a one-node
    /// budget and therefore unseen by the repair walk, while the unbounded conformance walk proves the
    /// violation exists; the explicit refusal closes the former absent-from-both-lists hole.</summary>
    [Fact]
    public void Field_budget_exhaustion_returns_an_explicit_unfilterable_refusal()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;
        PdfDictionary child = new()
        {
            [new PdfName("FT")] = new PdfName("Btn"),
            [new PdfName("T")] = PdfString.FromText("Child"),
            [AKey] = MakeAction("JavaScript"),
        };
        PdfIndirectReference childRef = doc.RegisterObject(child);
        PdfDictionary parent = new()
        {
            [new PdfName("FT")] = new PdfName("Btn"),
            [new PdfName("T")] = PdfString.FromText("Parent"),
            [new PdfName("Kids")] = new PdfArray(childRef),
        };
        PdfIndirectReference parentRef = doc.RegisterObject(parent);
        doc.CatalogDictionary![new PdfName("AcroForm")] =
            new PdfDictionary { [new PdfName("Fields")] = new PdfArray(parentRef) };
        editor.ActionWalkBudget = 1;
        Assert.Contains(Preflighter.Check(doc, ConformanceProfile.PdfA2b).Findings,
            finding => finding.RuleId == "action-type");

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal("AcroForm /Fields tree traversal", refusal.Site.HostDescription);
        Assert.Null(refusal.Site.HostObjectNumber);
        Assert.Null(refusal.Site.JavaScriptEntryName);
        Assert.Contains("safety limit", refusal.Reason, StringComparison.Ordinal);
        Assert.True(child.ContainsKey(AKey));
    }

    // ---- MINOR 8: the main repair path had no save-round-trip proof ------------------------------

    [Fact]
    public void The_Link_A_removal_survives_a_save_round_trip()
    {
        // Every /A and /AA assertion above reads the in-memory dictionary. This path carries all 1365
        // measured findings, and this codebase has already shipped an unsavable fix once.
        PdfDocumentEditor editor = NewEditorWithLinkAction("Launch");
        editor.RepairProhibitedActions();

        bool actionSurvived = SaveAndReload(editor, d =>
            ResolveOrNull(d, ((PdfArray)PageTreeOps.PageDicts(d)[0].Get(AnnotsKey)!)[0])
                is PdfDictionary link
            && link.ContainsKey(AKey));

        Assert.False(actionSurvived, "the removed /A must not come back through the writer");
    }

    [Fact]
    public void The_AA_trigger_removal_survives_a_save_round_trip()
    {
        PdfDocumentEditor editor = NewEditorWithWidgetAA(prohibitedTrigger: "E", permittedTrigger: "X");
        editor.RepairProhibitedActions();

        (bool HasE, bool HasX) survived = SaveAndReload(editor, d =>
        {
            var widget = (PdfDictionary)ResolveOrNull(
                d, ((PdfArray)PageTreeOps.PageDicts(d)[0].Get(AnnotsKey)!)[0])!;
            var aa = (PdfDictionary)ResolveOrNull(d, widget.Get(AAKey))!;
            return (aa.ContainsKey(new PdfName("E")), aa.ContainsKey(new PdfName("X")));
        });

        Assert.False(survived.HasE, "the removed /AA /E trigger must not come back through the writer");
        Assert.True(survived.HasX, "the permitted /AA /X trigger must survive the writer");
    }
    // ---- Fix wave 2 (verification pass): the shared-/AA scan's blind spots, and the page-tree walk
    //
    // Both defects below let a shape the rule flags reach the WRONG list -- a widget's repair silently
    // gutting a co-host's /AA while the report said it was left in place (major 1), and a page /AA in a
    // nested page tree reaching neither list at all (major 2).

    /// <summary>A two-page document with ONE indirect Widget listed in BOTH pages' <c>/Annots</c>, its
    /// <c>/AA /E</c> prohibited. One annotation is one host however many pages list it: counting it
    /// twice would call its own <c>/AA</c> "shared with itself" and turn every such document's repair
    /// into a refusal.</summary>
    private static PdfDocumentEditor NewEditorWithOneAnnotationOnTwoPages()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("one", 72, 700, "Helvetica", 12))
            .AddPage(p => p.AddText("two", 72, 700, "Helvetica", 12));
        PdfDocumentEditor editor = PdfDocumentEditor.Open(new MemoryStream(builder.ToByteArray()));
        PdfDocument doc = editor.Document;

        PdfDictionary widget = MakeAnnotation("Widget");
        widget[AAKey] = new PdfDictionary { [new PdfName("E")] = MakeAction("JavaScript") };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);
        AddAnnotEntry(doc, 1, widgetRef);   // the SAME annotation, listed on both pages

        return editor;
    }

    /// <summary>An indirect Widget in page 0's <c>/Annots</c> plus a PURE AcroForm field (in no
    /// <c>/Annots</c> at all) whose <c>/AA</c> is the SAME indirect dictionary as the widget's.</summary>
    private static (PdfDocumentEditor Editor, PdfDictionary Widget, int FieldObjectNumber, PdfDictionary SharedAa)
        NewEditorWithAaSharedBetweenWidgetAndPureField()
    {
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        var sharedAa = new PdfDictionary { [new PdfName("E")] = MakeAction("JavaScript") };
        PdfIndirectReference sharedAaRef = doc.RegisterObject(sharedAa);

        PdfDictionary widget = MakeAnnotation("Widget");
        widget[AAKey] = sharedAaRef;
        AddAnnotEntry(doc, 0, doc.RegisterObject(widget));

        PdfDictionary field = new()
        {
            [new PdfName("FT")] = new PdfName("Btn"),
            [new PdfName("T")] = PdfString.FromText("parent"),
            [AAKey] = sharedAaRef,
        };
        PdfIndirectReference fieldRef = doc.RegisterObject(field);
        doc.CatalogDictionary![new PdfName("AcroForm")] =
            new PdfDictionary { [new PdfName("Fields")] = new PdfArray(fieldRef) };

        return (editor, widget, fieldRef.ObjectNumber, sharedAa);
    }

    /// <summary>Splices an intermediate <c>/Type /Pages</c> node between the page-tree root and the
    /// document's single page, so the page sits TWO levels down: root <c>/Kids [ inter ]</c>, inter
    /// <c>/Kids [ page ]</c>. <c>PdfPageTree.CollectPages</c> recurses into it, and so does the rule
    /// through <c>ConformanceContext.Pages</c>; a one-level read of the root's <c>/Kids</c> hands back
    /// the intermediate node instead and never reaches the page at all.</summary>
    private static void NestThePageTree(PdfDocument doc)
    {
        PdfDictionary root = doc.PageTreeRootDictionary!;
        var rootKids = (PdfArray)root.Get(new PdfName("Kids"))!;
        PdfObject pageEntry = rootKids[0];

        var intermediate = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Pages"),
            [new PdfName("Kids")] = new PdfArray(pageEntry),
            [new PdfName("Count")] = new PdfInteger(1),
        };
        root[new PdfName("Kids")] = new PdfArray(doc.RegisterObject(intermediate));
    }

    // ---- MAJOR 1: an /AA shared with a host the annotation-only scan could not see ---------------

    [Fact]
    public void Repair_refuses_an_AA_shared_between_a_widget_and_a_PURE_FIELD()
    {
        // The widget is repairable and the pure field is refused, so a scan that walks /Annots alone
        // calls this /AA unshared, strips /E through the widget, and empties the very dictionary the
        // field points at -- moments after telling the caller the field's trigger was "left in place".
        // A false report is worse than a silent one.
        (PdfDocumentEditor editor, PdfDictionary widget, int fieldObj, PdfDictionary sharedAa) =
            NewEditorWithAaSharedBetweenWidgetAndPureField();

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        Assert.True(sharedAa.ContainsKey(new PdfName("E")),
                    "the shared /AA /E trigger must survive -- the field still points at this dictionary");
        Assert.True(widget.ContainsKey(AAKey), "the widget's /AA reference must survive a refusal");
        Assert.Contains(report.Refused,
                        r => r.Site.HostObjectNumber == widget.ObjectNumber
                             && r.Reason.Contains("shared", StringComparison.Ordinal));
        Assert.Contains(report.Refused, r => r.Site.HostDescription == $"field {fieldObj} /AA /E");
    }

    [Fact]
    public void The_shared_AA_refusal_names_the_co_host()
    {
        // Without the co-host named, acting on "give each host its own /AA first" means hunting the
        // document by hand for whatever else points at it.
        (PdfDocumentEditor editor, PdfDictionary widget, int fieldObj, PdfDictionary _) =
            NewEditorWithAaSharedBetweenWidgetAndPureField();

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        ProhibitedActionRefusal shared =
            report.Refused.Single(r => r.Site.HostObjectNumber == widget.ObjectNumber);
        Assert.Contains($"field {fieldObj}", shared.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain($"Widget annotation {widget.ObjectNumber}", shared.Reason,
                              StringComparison.Ordinal);   // a host is never its own co-host
    }

    [Fact]
    public void Repair_repairs_a_MERGED_field_and_widget_trigger_reached_by_both_walks()
    {
        // The over-fire control for the two tests above. Widening the sharing scan to the AcroForm
        // /Fields tree makes a merged field+widget -- the shape of every one of the 78 measured
        // form-hosted actions -- reachable TWICE, once as an annotation and once as a field. It is
        // still ONE host and its /AA is not shared; counting it twice would refuse the whole measured
        // form population.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary widget = MakeAnnotation("Widget");
        widget[new PdfName("FT")] = new PdfName("Btn");
        widget[AAKey] = new PdfDictionary { [new PdfName("E")] = MakeAction("JavaScript") };
        PdfIndirectReference widgetRef = doc.RegisterObject(widget);
        AddAnnotEntry(doc, 0, widgetRef);
        doc.CatalogDictionary![new PdfName("AcroForm")] =
            new PdfDictionary { [new PdfName("Fields")] = new PdfArray(widgetRef) };

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Equal("Widget /AA /E", Assert.Single(report.Repaired).Site.HostDescription);
        Assert.Empty(report.Refused);
        Assert.False(widget.ContainsKey(AAKey));
    }

    // ---- MINOR 1: the sharing scan's host dedup, which nothing tested ----------------------------

    [Fact]
    public void Repair_repairs_an_AA_on_an_annotation_listed_on_TWO_pages()
    {
        // Delete the sharing scan's host dedup and this goes red: the one widget is visited once per
        // page, its /AA reads as shared with itself, and the repair becomes a refusal -- a board
        // movement regression with nothing else to catch it.
        PdfDocumentEditor editor = NewEditorWithOneAnnotationOnTwoPages();
        PdfDocument doc = editor.Document;
        var widget = (PdfDictionary)Resolve(
            doc, ((PdfArray)doc.GetPages()[0].Dictionary.Get(AnnotsKey)!)[0]);

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Equal("Widget /AA /E", Assert.Single(report.Repaired).Site.HostDescription);
        Assert.Empty(report.Refused);
        Assert.False(widget.ContainsKey(AAKey));
    }

    // ---- MAJOR 2: a page /AA two levels down the page tree ---------------------------------------

    [Fact]
    public void Repair_refuses_a_page_AA_in_a_NESTED_page_tree()
    {
        // A one-level read of the page-tree root's /Kids hands back the intermediate /Type /Pages node
        // and never reaches the page, so this site landed in NEITHER list -- while the rule, which
        // recurses through ConformanceContext.Pages, raises a finding for it.
        PdfDocumentEditor editor = NewEditor();
        PdfDocument doc = editor.Document;

        PdfDictionary page = doc.GetPages()[0].Dictionary;
        page[AAKey] = new PdfDictionary { [new PdfName("O")] = MakeAction("Launch") };
        NestThePageTree(doc);

        // The fixture is only worth anything if the page really is two levels down AND still reachable
        // the way the rule reaches it.
        var rootKids = (PdfArray)doc.PageTreeRootDictionary!.Get(new PdfName("Kids"))!;
        var intermediate = (PdfDictionary)Resolve(doc, Assert.Single(rootKids));
        Assert.Equal("Pages", ((PdfName)intermediate.Get(new PdfName("Type"))!).Value);
        Assert.Same(page, Assert.Single(doc.GetPages()).Dictionary);

        ProhibitedActionRepairReport report = editor.RepairProhibitedActions();

        Assert.Empty(report.Repaired);
        ProhibitedActionRefusal refusal = Assert.Single(report.Refused);
        Assert.Equal("page 1 /AA /O", refusal.Site.HostDescription);   // numbered by page, not by kid
        Assert.True(((PdfDictionary)page.Get(AAKey)!).ContainsKey(new PdfName("O")));
    }

    [Fact]
    public void Preview_writes_nothing_to_a_page_tree_root_that_has_no_Kids()
    {
        // PreviewProhibitedActionRepairs documents itself as writing nothing. Reading the pages through
        // PageTreeOps.Kids broke that: it INSERTS an empty /Kids array when the key is absent.
        PdfDocumentEditor editor = NewEditorWithCatalogOpenAction("Launch");
        PdfDictionary root = editor.Document.PageTreeRootDictionary!;
        root.Remove(new PdfName("Kids"));

        ProhibitedActionRepairPreview preview = editor.PreviewProhibitedActionRepairs();

        Assert.Single(preview.Refused);   // the walk still ran -- this is not a vacuous pass
        Assert.False(root.ContainsKey(new PdfName("Kids")), "the preview must not write to the document");
    }
}
