# Forms Authoring API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Field authoring on `PdfDocumentEditor.Forms` — create, delete, rename, move/resize, and restyle AcroForm fields on an existing document, per `Docs/specs/2026-07-03-forms-authoring-api-design.md`.

**Architecture:** A new internal `Editing/Forms/FieldAuthor.cs` owns dictionary-level construction (AcroForm bootstrap, widget wiring into page `/Annots` and `/AcroForm /Fields`, validation). Public entry points are thin methods on `PdfFormFields` (creation/removal, via a new partial) and on `PdfFormField` (rename/geometry/property setters). Appearance generation reuses the existing `FieldAppearanceGenerator` / `ButtonStateWriter` machinery unchanged. The builder's field builders are NOT reused (they target the from-scratch writer pipeline); their dict recipes are transcribed here.

**Tech Stack:** C#/.NET (PdfLibrary engine repo), xUnit (PdfLibrary.Tests, which has `InternalsVisibleTo` — tests may touch `field.Dict`, `FormFieldTree`, `PdfDocument.Load`, etc.).

## Global Constraints

- Repo: `C:\Users\jorda\RiderProjects\PDF` (NOT the Focal repo). Work on branch `feature/forms-authoring` off `master`.
- All new public API lives in namespace `PdfLibrary.Editing.Forms`; geometry uses the existing `PdfRect` (PDF user space, Y-up) — no new rect type.
- Never set `/NeedAppearances`; every creation/mutation ends with a generated appearance stream (via `FieldAppearanceGenerator.Regenerate` or `EnsureButtonAppearance`).
- Every authoring entry point is guarded against dynamic XFA (same posture as `Flatten`).
- Validation throws BEFORE any document mutation — a failed call leaves the document byte-identical.
- Run task-filtered tests during a task; run the FULL suite (`dotnet test PdfLibrary.Tests`) before each commit. Suite baseline: all green (~1350 tests).
- Commit messages end with: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`
- Do NOT push or publish anything — local commits only (user gates pushes/publishes).

### Shared test helpers (defined in Task 1, used by every test file)

Task 1 creates `PdfLibrary.Tests/Editing/Forms/AuthoringTestHelper.cs`; later tasks call these exact signatures:

```csharp
public static class AuthoringTestHelper
{
    /// <summary>A one-page document with NO /AcroForm.</summary>
    public static PdfDocumentEditor OpenPlainSinglePage();

    /// <summary>A two-page document with NO /AcroForm.</summary>
    public static PdfDocumentEditor OpenPlainTwoPages();

    /// <summary>Saves and reopens through real bytes; disposes the input editor.</summary>
    public static PdfDocumentEditor SaveAndReopen(PdfDocumentEditor editor);

    /// <summary>A dynamic-XFA shell document (AcroForm with /XFA, no widgets), opened for editing.</summary>
    public static PdfDocumentEditor OpenDynamicXfaShell();
}
```

---

### Task 1: FieldAuthor foundation + AddTextField

**Files:**
- Create: `PdfLibrary/Editing/Forms/FieldAuthor.cs`
- Create: `PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs`
- Modify: `PdfLibrary/Editing/Forms/PdfFormFields.cs:10` (add `partial` to the class declaration)
- Create: `PdfLibrary.Tests/Editing/Forms/AuthoringTestHelper.cs`
- Test: `PdfLibrary.Tests/Editing/Forms/AuthoringTextFieldTests.cs`

**Interfaces:**
- Consumes: `FormFieldTree.Read(PdfDocument)`, `FieldAppearanceGenerator.Regenerate(PdfDocument, PdfFormField)`, `FormFlattener.IsDynamicXfa(PdfDocument)`, `PdfDocument.RegisterObject(PdfObject) → PdfIndirectReference` (all existing).
- Produces (later tasks rely on these exact signatures):
  - `internal static PdfDictionary FieldAuthor.EnsureAcroForm(PdfDocument doc)`
  - `internal static PdfArray FieldAuthor.EnsureFieldsArray(PdfDocument doc)`
  - `internal static void FieldAuthor.ValidateNewName(PdfDocument doc, string name)`
  - `internal static PdfDictionary FieldAuthor.GetPageDict(PdfDocument doc, int pageIndex)`
  - `internal static PdfArray FieldAuthor.RectArray(PdfRect rect)`
  - `internal static void FieldAuthor.AddToAnnots(PdfDocument doc, PdfDictionary page, PdfIndirectReference widgetRef)`
  - `private void PdfFormFields.GuardAuthoring()` (partial-class private, callable from `PdfFormFields.Authoring.cs`)
  - `public PdfTextField PdfFormFields.AddTextField(int pageIndex, string name, PdfRect rect)`
  - Test helper class per Global Constraints.

- [ ] **Step 1: Create branch**

```bash
cd /c/Users/jorda/RiderProjects/PDF
git checkout -b feature/forms-authoring master
```

- [ ] **Step 2: Write the test helper**

Create `PdfLibrary.Tests/Editing/Forms/AuthoringTestHelper.cs`:

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing.Forms;

/// <summary>Shared fixtures for the forms-authoring test files.</summary>
public static class AuthoringTestHelper
{
    /// <summary>A one-page document with NO /AcroForm.</summary>
    public static PdfDocumentEditor OpenPlainSinglePage() =>
        PdfDocumentEditor.Open(new MemoryStream(
            PdfDocumentBuilder.Create().AddPage(_ => { }).ToByteArray()));

    /// <summary>A two-page document with NO /AcroForm.</summary>
    public static PdfDocumentEditor OpenPlainTwoPages() =>
        PdfDocumentEditor.Open(new MemoryStream(
            PdfDocumentBuilder.Create().AddPage(_ => { }).AddPage(_ => { }).ToByteArray()));

    /// <summary>Saves and reopens through real bytes; disposes the input editor.</summary>
    public static PdfDocumentEditor SaveAndReopen(PdfDocumentEditor editor)
    {
        using var ms = new MemoryStream();
        editor.Save(ms);
        editor.Dispose();
        return PdfDocumentEditor.Open(new MemoryStream(ms.ToArray()));
    }

    /// <summary>A dynamic-XFA shell (AcroForm with /XFA, no widgets), opened for editing.
    /// Mirrors XfaFlattenGateTests.BuildDynamicXfaShell.</summary>
    public static PdfDocumentEditor OpenDynamicXfaShell()
    {
        byte[] simple = PdfDocumentBuilder.Create().AddPage(_ => { }).ToByteArray();
        var doc = PdfDocument.Load(new MemoryStream(simple));
        var acro = new PdfDictionary();
        acro[new PdfName("Fields")] = new PdfArray();
        acro[new PdfName("XFA")] = PdfString.FromText(
            "<xdp:xdp xmlns:xdp=\"http://ns.adobe.com/xdp/\"><template/></xdp:xdp>");
        doc.CatalogDictionary![new PdfName("AcroForm")] = acro;
        return doc.Edit();
    }
}
```

Note: if `doc.Edit()` in the last helper takes ownership such that disposing the editor disposes the doc, that is fine for these tests. If `PdfDocument.Edit()` does not exist with that exact name, check `XfaFlattenGateTests.cs:63` for the current idiom and match it.

- [ ] **Step 3: Write the failing tests**

Create `PdfLibrary.Tests/Editing/Forms/AuthoringTextFieldTests.cs`:

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringTextFieldTests
{
    private static readonly PdfRect Rect = new(72, 700, 372, 720);

    [Fact]
    public void AddTextField_OnPlainDoc_BootstrapsAcroForm()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddTextField(0, "name1", Rect);

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        PdfFormField? field = reopened.Forms["name1"];
        Assert.NotNull(field);
        Assert.IsType<PdfTextField>(field);
        Assert.Equal(1, reopened.Forms.Count);
    }

    [Fact]
    public void AddTextField_ReturnsLiveField_WithWidgetGeometry()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "name1", Rect);

        Assert.Equal("name1", field.FullName);
        Assert.Single(field.Widgets);
        Assert.Equal(0, field.Widgets[0].PageIndex);
        Assert.Equal(72, field.Widgets[0].Rect.Left, 3);
        Assert.Equal(700, field.Widgets[0].Rect.Bottom, 3);
        Assert.Equal(372, field.Widgets[0].Rect.Right, 3);
        Assert.Equal(720, field.Widgets[0].Rect.Top, 3);
    }

    [Fact]
    public void AddTextField_ThenFill_RoundTrips()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "name1", Rect);
        field.Value = "hello authoring";

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfTextField>(reopened.Forms["name1"]);
        Assert.Equal("hello authoring", back.Value);
    }

    [Fact]
    public void AddTextField_GeneratesAppearanceStream_AndNoNeedAppearances()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "name1", Rect);

        // /AP /N present on the widget (InternalsVisibleTo lets us read the dict).
        PdfDictionary widget = field.WidgetDicts[0];
        Assert.True(widget.ContainsKey(new PdfName("AP")));

        // Bootstrap never sets /NeedAppearances.
        PdfDictionary acro = FieldAuthor.EnsureAcroForm(field.Doc);
        Assert.False(acro.ContainsKey(new PdfName("NeedAppearances")));
        // Bootstrap wrote the Helvetica default /DA.
        Assert.True(acro.ContainsKey(new PdfName("DA")));
    }

    [Fact]
    public void AddTextField_OnAlreadyFormedDoc_LeavesExistingFieldsAlone()
    {
        byte[] formed = FormTestDocs.WithTextField("existing", "keep me");
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(new MemoryStream(formed));
        editor.Forms.AddTextField(0, "fresh", Rect);

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        Assert.Equal(2, reopened.Forms.Count);
        Assert.Equal("keep me", Assert.IsType<PdfTextField>(reopened.Forms["existing"]).Value);
        Assert.NotNull(reopened.Forms["fresh"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a.b")]
    public void AddTextField_InvalidName_Throws(string bad)
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        Assert.Throws<ArgumentException>(() => editor.Forms.AddTextField(0, bad, Rect));
        Assert.Equal(0, editor.Forms.Count); // document unmodified
    }

    [Fact]
    public void AddTextField_DuplicateName_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddTextField(0, "dup", Rect);
        Assert.Throws<ArgumentException>(() => editor.Forms.AddTextField(0, "dup", Rect));
        Assert.Equal(1, editor.Forms.Count);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void AddTextField_BadPageIndex_Throws(int pageIndex)
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.Forms.AddTextField(pageIndex, "n", Rect));
        Assert.Equal(0, editor.Forms.Count);
    }

    [Fact]
    public void AddTextField_OnDynamicXfa_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenDynamicXfaShell();
        Assert.Throws<InvalidOperationException>(() => editor.Forms.AddTextField(0, "n", Rect));
    }

    [Fact]
    public void AddTextField_ParityWithBuilderField()
    {
        // Spec §4: the authored dict recipe must match what the builder produces for an
        // identical field — same effective type and flags when read back through the tree.
        using PdfDocumentEditor authored = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField mine = authored.Forms.AddTextField(0, "f", new PdfRect(72, 700, 372, 720));

        byte[] built = FormTestDocs.WithTextField("f");
        using PdfDocumentEditor builder = PdfDocumentEditor.Open(new MemoryStream(built));
        var theirs = Assert.IsType<PdfTextField>(builder.Forms["f"]);

        Assert.Equal(theirs.Type, mine.Type);
        Assert.Equal(theirs.IsMultiline, mine.IsMultiline);
        Assert.Equal(theirs.IsReadOnly, mine.IsReadOnly);
        Assert.Equal(theirs.IsRequired, mine.IsRequired);
        Assert.Equal(theirs.Quadding, mine.Quadding);
    }

    [Fact]
    public void AddTextField_OnSecondPage_WidgetLandsThere()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainTwoPages();
        PdfTextField field = editor.Forms.AddTextField(1, "p2", Rect);
        Assert.Equal(1, field.Widgets[0].PageIndex);

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        Assert.Equal(1, reopened.Forms["p2"]!.Widgets[0].PageIndex);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `cd /c/Users/jorda/RiderProjects/PDF && dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringTextField"`
Expected: COMPILE ERROR — `AddTextField` / `FieldAuthor` do not exist. That counts as the failing state.

- [ ] **Step 5: Implement FieldAuthor**

Create `PdfLibrary/Editing/Forms/FieldAuthor.cs`:

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing.Forms;

/// <summary>
/// Dictionary-level construction shared by the PdfFormFields authoring methods: AcroForm
/// bootstrap, field/widget wiring into page /Annots and /AcroForm /Fields, and validation.
/// The builder's WriteFormField recipes are transcribed here for the parsed object model;
/// appearance generation stays in FieldAppearanceGenerator/ButtonStateWriter.
/// </summary>
internal static class FieldAuthor
{
    /// <summary>Returns the /AcroForm dictionary, creating and catalog-wiring it (with the
    /// Helvetica /DA default) when the document has none. Never sets /NeedAppearances —
    /// authoring always generates appearance streams itself. /DR is NOT written here: the
    /// first Regenerate call routes through AppearanceFontResolver, which synthesises the
    /// standard-14 font and registers it in /DR /Font (spec §2.2's /DR requirement is met
    /// by that existing self-healing path, not duplicated here).</summary>
    internal static PdfDictionary EnsureAcroForm(PdfDocument doc)
    {
        PdfDictionary catalog = doc.CatalogDictionary
            ?? throw new InvalidOperationException("Document has no catalog.");
        if (Resolve(doc, catalog.Get(new PdfName("AcroForm"))) is PdfDictionary existing)
            return existing;
        var acro = new PdfDictionary
        {
            [new PdfName("Fields")] = new PdfArray(),
            [new PdfName("DA")] = PdfString.FromText("/Helv 0 Tf 0 g")
        };
        catalog[new PdfName("AcroForm")] = doc.RegisterObject(acro);
        return acro;
    }

    internal static PdfArray EnsureFieldsArray(PdfDocument doc)
    {
        PdfDictionary acro = EnsureAcroForm(doc);
        if (Resolve(doc, acro.Get(new PdfName("Fields"))) is PdfArray fields) return fields;
        var created = new PdfArray();
        acro[new PdfName("Fields")] = created;
        return created;
    }

    /// <summary>Root-level partial-name validation: non-empty, no '.', unique against the live tree.</summary>
    internal static void ValidateNewName(PdfDocument doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Field name must be non-empty.", nameof(name));
        if (name.Contains('.'))
            throw new ArgumentException(
                "Field name must not contain '.' — the period separates hierarchy levels in full names.",
                nameof(name));
        if (FormFieldTree.Read(doc).Any(f => string.Equals(f.FullName, name, StringComparison.Ordinal)))
            throw new ArgumentException($"A field named '{name}' already exists.", nameof(name));
    }

    internal static PdfDictionary GetPageDict(PdfDocument doc, int pageIndex)
    {
        List<PdfPage> pages = doc.GetPages();
        if (pageIndex < 0 || pageIndex >= pages.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex,
                $"Page index must be in [0, {pages.Count}).");
        return pages[pageIndex].Dictionary;
    }

    /// <summary>Normalized [minX minY maxX maxY] /Rect array.</summary>
    internal static PdfArray RectArray(PdfRect rect) => new()
    {
        new PdfReal(Math.Min(rect.Left, rect.Right)),
        new PdfReal(Math.Min(rect.Bottom, rect.Top)),
        new PdfReal(Math.Max(rect.Left, rect.Right)),
        new PdfReal(Math.Max(rect.Bottom, rect.Top))
    };

    /// <summary>Appends the widget to the page's /Annots (creating the array when absent).</summary>
    internal static void AddToAnnots(PdfDocument doc, PdfDictionary page, PdfIndirectReference widgetRef)
    {
        if (Resolve(doc, page.Get(new PdfName("Annots"))) is PdfArray annots)
        {
            annots.Add(widgetRef);
            return;
        }
        page[new PdfName("Annots")] = new PdfArray { widgetRef };
    }

    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;
}
```

If `PdfArray` has no collection-initializer `Add`, use explicit `.Add(...)` calls — match whatever `AcroFormMerger.cs` compiles with.

- [ ] **Step 6: Add the partial + AddTextField**

In `PdfLibrary/Editing/Forms/PdfFormFields.cs:10` change:

```csharp
public sealed class PdfFormFields : IReadOnlyCollection<PdfFormField>
```
to
```csharp
public sealed partial class PdfFormFields : IReadOnlyCollection<PdfFormField>
```

Create `PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs`:

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing.Forms;

/// <summary>
/// Field-authoring surface: create/remove AcroForm fields on an existing document
/// (design: Docs/specs/2026-07-03-forms-authoring-api-design.md). Geometry is PDF user
/// space, Y-up — the same convention as <see cref="PdfFieldWidget.Rect"/>.
/// </summary>
public sealed partial class PdfFormFields
{
    /// <summary>
    /// Creates a single-line text field on the given page. Bootstraps /AcroForm when the
    /// document has none. The returned field is live — set <see cref="PdfTextField.Value"/>
    /// to fill it immediately.
    /// </summary>
    /// <exception cref="ArgumentException">Empty/dotted/duplicate name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Bad page index.</exception>
    /// <exception cref="InvalidOperationException">The document is a dynamic XFA form.</exception>
    public PdfTextField AddTextField(int pageIndex, string name, PdfRect rect)
    {
        GuardAuthoring();
        FieldAuthor.ValidateNewName(_document, name);
        PdfDictionary page = FieldAuthor.GetPageDict(_document, pageIndex);

        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Annot"),
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("FT")] = new PdfName("Tx"),
            [new PdfName("T")] = PdfString.FromText(name),
            [new PdfName("V")] = PdfString.FromText(string.Empty),
            [new PdfName("Rect")] = FieldAuthor.RectArray(rect)
        };
        PdfIndirectReference fieldRef = _document.RegisterObject(dict);
        FieldAuthor.AddToAnnots(_document, page, fieldRef);
        FieldAuthor.EnsureFieldsArray(_document).Add(fieldRef);

        var field = (PdfTextField)this[name]!;
        FieldAppearanceGenerator.Regenerate(_document, field);
        return field;
    }

    private void GuardAuthoring()
    {
        if (FormFlattener.IsDynamicXfa(_document))
            throw new InvalidOperationException(
                "Cannot author fields on a dynamic XFA form: its fields exist only in the XFA " +
                "template, so AcroForm widgets added here would never be shown by an XFA viewer. " +
                "Check Forms.IsDynamicXfa before offering form design.");
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringTextField"`
Expected: all AuthoringTextFieldTests PASS.

- [ ] **Step 8: Full suite + commit**

Run: `dotnet test PdfLibrary.Tests`
Expected: all green, no regressions.

```bash
git add PdfLibrary/Editing/Forms/FieldAuthor.cs PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs PdfLibrary/Editing/Forms/PdfFormFields.cs PdfLibrary.Tests/Editing/Forms/AuthoringTestHelper.cs PdfLibrary.Tests/Editing/Forms/AuthoringTextFieldTests.cs
git commit -m "feat(forms): AddTextField authoring + AcroForm bootstrap on existing documents

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: AddCheckbox + AddSignatureField

**Files:**
- Modify: `PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs` (add two methods)
- Test: `PdfLibrary.Tests/Editing/Forms/AuthoringButtonSigTests.cs`

**Interfaces:**
- Consumes: `FieldAuthor.*` and `GuardAuthoring()` from Task 1; `FieldAppearanceGenerator.EnsureButtonAppearance(PdfDocument, PdfDictionary widget, string onStateName, bool isRadio)` and `EnsurePrintable(PdfDictionary)` (existing).
- Produces:
  - `public PdfButtonField AddCheckbox(int pageIndex, string name, PdfRect rect)` — on-state named `"Yes"`, created unchecked.
  - `public PdfSignatureField AddSignatureField(int pageIndex, string name, PdfRect rect)` — unsigned placeholder.

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Editing/Forms/AuthoringButtonSigTests.cs`:

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringButtonSigTests
{
    private static readonly PdfRect CheckRect = new(72, 700, 86, 714);
    private static readonly PdfRect SigRect = new(72, 600, 272, 660);

    [Fact]
    public void AddCheckbox_CreatesUnchecked_WithYesOffAppearances()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfButtonField cb = editor.Forms.AddCheckbox(0, "cb1", CheckRect);

        Assert.Equal(ButtonKind.Checkbox, cb.Kind);
        Assert.False(cb.IsChecked);
        Assert.Contains("Yes", cb.Options);

        // The widget has /AP /N with both the on-state and /Off.
        PdfDictionary widget = cb.WidgetDicts[0];
        var ap = Assert.IsType<PdfDictionary>(widget.Get(new PdfName("AP")));
        var n = Assert.IsType<PdfDictionary>(ap.Get(new PdfName("N")));
        Assert.True(n.ContainsKey(new PdfName("Yes")));
        Assert.True(n.ContainsKey(new PdfName("Off")));
    }

    [Fact]
    public void AddCheckbox_CheckThenSave_RoundTrips()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfButtonField cb = editor.Forms.AddCheckbox(0, "cb1", CheckRect);
        cb.Check();

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfButtonField>(reopened.Forms["cb1"]);
        Assert.True(back.IsChecked);
    }

    [Fact]
    public void AddCheckbox_UncheckedSurvivesRoundTrip()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddCheckbox(0, "cb1", CheckRect);

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        Assert.False(Assert.IsType<PdfButtonField>(reopened.Forms["cb1"]).IsChecked);
    }

    [Fact]
    public void AddSignatureField_CreatesUnsignedPlaceholder()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfSignatureField sig = editor.Forms.AddSignatureField(0, "sig1", SigRect);
        Assert.False(sig.IsSigned);

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfSignatureField>(reopened.Forms["sig1"]);
        Assert.False(back.IsSigned);
        Assert.Equal(72, back.Widgets[0].Rect.Left, 3);
    }

    [Fact]
    public void AddCheckbox_DuplicateName_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddCheckbox(0, "dup", CheckRect);
        Assert.Throws<ArgumentException>(() => editor.Forms.AddCheckbox(0, "dup", CheckRect));
    }

    [Fact]
    public void AddCheckbox_OnDynamicXfa_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenDynamicXfaShell();
        Assert.Throws<InvalidOperationException>(() => editor.Forms.AddCheckbox(0, "cb", CheckRect));
        Assert.Throws<InvalidOperationException>(() => editor.Forms.AddSignatureField(0, "sig", SigRect));
    }
}
```

Note: `ap.Get(...)` may return an indirect reference depending on how `EnsureButtonAppearance` stores states (it registers streams indirect but the /N dict direct — the assertions above match its actual shape: `/AP` direct dict, `/N` direct dict, state values indirect). If `Assert.IsType<PdfDictionary>` fails on `/AP`, resolve through `field.Doc.GetObject(...)` first — but per `FieldAppearanceGenerator.cs:906-911` the direct-dict shape is what's written.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringButtonSig"`
Expected: COMPILE ERROR (methods missing).

- [ ] **Step 3: Implement both methods**

Add to `PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs`:

```csharp
    /// <summary>
    /// Creates an unchecked checkbox with on-state "Yes" and generated check-mark /AP states.
    /// </summary>
    /// <exception cref="ArgumentException">Empty/dotted/duplicate name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Bad page index.</exception>
    /// <exception cref="InvalidOperationException">The document is a dynamic XFA form.</exception>
    public PdfButtonField AddCheckbox(int pageIndex, string name, PdfRect rect)
    {
        GuardAuthoring();
        FieldAuthor.ValidateNewName(_document, name);
        PdfDictionary page = FieldAuthor.GetPageDict(_document, pageIndex);

        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Annot"),
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("FT")] = new PdfName("Btn"),
            [new PdfName("T")] = PdfString.FromText(name),
            [new PdfName("V")] = new PdfName("Off"),
            [new PdfName("AS")] = new PdfName("Off"),
            [new PdfName("Rect")] = FieldAuthor.RectArray(rect)
        };
        PdfIndirectReference fieldRef = _document.RegisterObject(dict);
        FieldAuthor.AddToAnnots(_document, page, fieldRef);
        FieldAuthor.EnsureFieldsArray(_document).Add(fieldRef);

        FieldAppearanceGenerator.EnsureButtonAppearance(_document, dict, "Yes", isRadio: false);
        FieldAppearanceGenerator.EnsurePrintable(dict);
        return (PdfButtonField)this[name]!;
    }

    /// <summary>
    /// Creates an unsigned signature-field placeholder (no /V; signing is out of scope).
    /// </summary>
    /// <exception cref="ArgumentException">Empty/dotted/duplicate name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Bad page index.</exception>
    /// <exception cref="InvalidOperationException">The document is a dynamic XFA form.</exception>
    public PdfSignatureField AddSignatureField(int pageIndex, string name, PdfRect rect)
    {
        GuardAuthoring();
        FieldAuthor.ValidateNewName(_document, name);
        PdfDictionary page = FieldAuthor.GetPageDict(_document, pageIndex);

        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Annot"),
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("FT")] = new PdfName("Sig"),
            [new PdfName("T")] = PdfString.FromText(name),
            [new PdfName("Rect")] = FieldAuthor.RectArray(rect)
        };
        PdfIndirectReference fieldRef = _document.RegisterObject(dict);
        FieldAuthor.AddToAnnots(_document, page, fieldRef);
        FieldAuthor.EnsureFieldsArray(_document).Add(fieldRef);
        FieldAppearanceGenerator.EnsurePrintable(dict);
        return (PdfSignatureField)this[name]!;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringButtonSig"`
Expected: PASS.

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test PdfLibrary.Tests` — all green.

```bash
git add PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs PdfLibrary.Tests/Editing/Forms/AuthoringButtonSigTests.cs
git commit -m "feat(forms): AddCheckbox + AddSignatureField authoring

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: AddRadioGroup

**Files:**
- Create: `PdfLibrary/Editing/Forms/PdfRadioOptionPlacement.cs`
- Modify: `PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs` (add method)
- Modify: `PdfLibrary/Editing/Forms/FieldFlags.cs` (add `NoToggleToOff = 15`)
- Test: `PdfLibrary.Tests/Editing/Forms/AuthoringRadioGroupTests.cs`

**Interfaces:**
- Consumes: Task 1 `FieldAuthor.*`/`GuardAuthoring`; `FieldFlags.Radio` (=16), existing.
- Produces:
  - `public readonly record struct PdfRadioOptionPlacement(int PageIndex, PdfRect Rect, string OnState)`
  - `public PdfButtonField AddRadioGroup(string name, IReadOnlyList<PdfRadioOptionPlacement> options)` — parent field + one widget per option; created with `/V /Off` (nothing selected).
  - `FieldFlags.NoToggleToOff = 15`

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Editing/Forms/AuthoringRadioGroupTests.cs`:

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringRadioGroupTests
{
    private static PdfRadioOptionPlacement Opt(string onState, double y, int page = 0) =>
        new(page, new PdfRect(72, y, 86, y + 14), onState);

    [Fact]
    public void AddRadioGroup_CreatesParentWithPerOptionWidgets()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfButtonField radio = editor.Forms.AddRadioGroup("choice",
            new[] { Opt("A", 700), Opt("B", 680), Opt("C", 660) });

        Assert.Equal(ButtonKind.Radio, radio.Kind);
        Assert.Equal(new[] { "A", "B", "C" }, radio.Options);
        Assert.Equal(3, radio.Widgets.Count);
        Assert.Null(radio.SelectedOption);
    }

    [Fact]
    public void AddRadioGroup_SelectThenSave_RoundTrips()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfButtonField radio = editor.Forms.AddRadioGroup("choice",
            new[] { Opt("A", 700), Opt("B", 680) });
        radio.SelectedOption = "B";

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfButtonField>(reopened.Forms["choice"]);
        Assert.Equal("B", back.SelectedOption);
    }

    [Fact]
    public void AddRadioGroup_OptionsAcrossPages_WidgetsLandOnTheirPages()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainTwoPages();
        PdfButtonField radio = editor.Forms.AddRadioGroup("span",
            new[] { Opt("P1", 700, page: 0), Opt("P2", 700, page: 1) });

        Assert.Equal(0, radio.Widgets[0].PageIndex);
        Assert.Equal(1, radio.Widgets[1].PageIndex);
    }

    [Fact]
    public void AddRadioGroup_EmptyOptions_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        Assert.Throws<ArgumentException>(() =>
            editor.Forms.AddRadioGroup("r", Array.Empty<PdfRadioOptionPlacement>()));
        Assert.Equal(0, editor.Forms.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Off")]
    public void AddRadioGroup_BadOnState_Throws(string onState)
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        Assert.Throws<ArgumentException>(() =>
            editor.Forms.AddRadioGroup("r", new[] { Opt(onState, 700) }));
        Assert.Equal(0, editor.Forms.Count);
    }

    [Fact]
    public void AddRadioGroup_DuplicateOnStates_Throws_DocumentUnmodified()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        Assert.Throws<ArgumentException>(() =>
            editor.Forms.AddRadioGroup("r", new[] { Opt("A", 700), Opt("A", 680) }));
        Assert.Equal(0, editor.Forms.Count);
    }

    [Fact]
    public void AddRadioGroup_BadPageIndexInAnyOption_Throws_DocumentUnmodified()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            editor.Forms.AddRadioGroup("r", new[] { Opt("A", 700), Opt("B", 680, page: 7) }));
        Assert.Equal(0, editor.Forms.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringRadioGroup"`
Expected: COMPILE ERROR.

- [ ] **Step 3: Implement**

Add to `PdfLibrary/Editing/Forms/FieldFlags.cs` after `Required = 2;` / `NoExport = 3;` block:

```csharp
    // Button flags (Table 226)
    public const int NoToggleToOff = 15;
```

(Place it with the existing button-flags group next to `Radio = 16`.)

Create `PdfLibrary/Editing/Forms/PdfRadioOptionPlacement.cs`:

```csharp
using PdfLibrary.Builder;

namespace PdfLibrary.Editing.Forms;

/// <summary>One radio-button option: which page, where, and its on-state name (the /AP /N key
/// that selecting this option sets, e.g. "A"). On-state names must be unique within a group
/// and must not be "Off".</summary>
public readonly record struct PdfRadioOptionPlacement(int PageIndex, PdfRect Rect, string OnState);
```

Add to `PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs`:

```csharp
    /// <summary>
    /// Creates a radio group: a parent /Btn field with one widget per option (options may sit on
    /// different pages). Created with nothing selected; set <see cref="PdfButtonField.SelectedOption"/>
    /// to choose one. Radio + NoToggleToOff flags are set (Acrobat's default posture).
    /// </summary>
    /// <exception cref="ArgumentException">Bad name; no options; empty/"Off"/duplicate on-state.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Bad page index in any placement.</exception>
    /// <exception cref="InvalidOperationException">The document is a dynamic XFA form.</exception>
    public PdfButtonField AddRadioGroup(string name, IReadOnlyList<PdfRadioOptionPlacement> options)
    {
        GuardAuthoring();
        FieldAuthor.ValidateNewName(_document, name);
        if (options is null || options.Count == 0)
            throw new ArgumentException("A radio group needs at least one option placement.", nameof(options));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PdfRadioOptionPlacement o in options)
        {
            if (string.IsNullOrWhiteSpace(o.OnState) || o.OnState == "Off")
                throw new ArgumentException(
                    "Radio on-state names must be non-empty and must not be 'Off'.", nameof(options));
            if (!seen.Add(o.OnState))
                throw new ArgumentException($"Duplicate radio on-state '{o.OnState}'.", nameof(options));
            FieldAuthor.GetPageDict(_document, o.PageIndex); // validate every page index BEFORE mutating
        }

        int radioFf = (1 << (FieldFlags.Radio - 1)) | (1 << (FieldFlags.NoToggleToOff - 1));
        var kids = new PdfArray();
        var parent = new PdfDictionary
        {
            [new PdfName("FT")] = new PdfName("Btn"),
            [new PdfName("Ff")] = new PdfInteger(radioFf),
            [new PdfName("T")] = PdfString.FromText(name),
            [new PdfName("V")] = new PdfName("Off"),
            [new PdfName("Kids")] = kids
        };
        PdfIndirectReference parentRef = _document.RegisterObject(parent);

        foreach (PdfRadioOptionPlacement o in options)
        {
            var widget = new PdfDictionary
            {
                [new PdfName("Type")] = new PdfName("Annot"),
                [new PdfName("Subtype")] = new PdfName("Widget"),
                [new PdfName("Parent")] = parentRef,
                [new PdfName("AS")] = new PdfName("Off"),
                [new PdfName("Rect")] = FieldAuthor.RectArray(o.Rect)
            };
            PdfIndirectReference widgetRef = _document.RegisterObject(widget);
            FieldAuthor.AddToAnnots(_document, FieldAuthor.GetPageDict(_document, o.PageIndex), widgetRef);
            kids.Add(widgetRef);
            FieldAppearanceGenerator.EnsureButtonAppearance(_document, widget, o.OnState, isRadio: true);
            FieldAppearanceGenerator.EnsurePrintable(widget);
        }

        FieldAuthor.EnsureFieldsArray(_document).Add(parentRef);
        return (PdfButtonField)this[name]!;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringRadioGroup"`
Expected: PASS. If `Widgets` ordering doesn't match insertion order in the cross-page test, check `FormFieldTree.PopulateWidgets` — widget order follows `/Kids` order, which this code appends in options order, so it should match.

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test PdfLibrary.Tests` — all green.

```bash
git add PdfLibrary/Editing/Forms/PdfRadioOptionPlacement.cs PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs PdfLibrary/Editing/Forms/FieldFlags.cs PdfLibrary.Tests/Editing/Forms/AuthoringRadioGroupTests.cs
git commit -m "feat(forms): AddRadioGroup authoring with per-option widget placement

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: AddDropdown

**Files:**
- Modify: `PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs` (add method)
- Test: `PdfLibrary.Tests/Editing/Forms/AuthoringDropdownTests.cs`

**Interfaces:**
- Consumes: Task 1 `FieldAuthor.*`/`GuardAuthoring`; `FieldFlags.Combo` (=18).
- Produces: `public PdfChoiceField AddDropdown(int pageIndex, string name, PdfRect rect, IReadOnlyList<(string Export, string Display)> options)` — combo box, nothing selected.

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Editing/Forms/AuthoringDropdownTests.cs`:

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringDropdownTests
{
    private static readonly PdfRect Rect = new(72, 700, 272, 720);
    private static readonly (string, string)[] Opts =
        { ("red", "Red"), ("grn", "Green"), ("blu", "Blue") };

    [Fact]
    public void AddDropdown_CreatesComboWithOptions()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfChoiceField dd = editor.Forms.AddDropdown(0, "color", Rect, Opts);

        Assert.True(dd.IsCombo);
        Assert.False(dd.IsMultiSelect);
        Assert.Equal(3, dd.Options.Count);
        Assert.Equal(("red", "Red"), dd.Options[0]);
        Assert.Empty(dd.SelectedValues);
    }

    [Fact]
    public void AddDropdown_SelectThenSave_RoundTrips()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfChoiceField dd = editor.Forms.AddDropdown(0, "color", Rect, Opts);
        dd.SelectedValues = new[] { "grn" };

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfChoiceField>(reopened.Forms["color"]);
        Assert.Equal(new[] { "grn" }, back.SelectedValues);
        Assert.Equal(new[] { 1 }, back.SelectedIndices);
    }

    [Fact]
    public void AddDropdown_SameExportAndDisplay_WritesSingleStringOpt()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfChoiceField dd = editor.Forms.AddDropdown(0, "plain", Rect,
            new[] { ("one", "one"), ("two", "two") });

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfChoiceField>(reopened.Forms["plain"]);
        Assert.Equal(("one", "one"), back.Options[0]);
        Assert.Equal(("two", "two"), back.Options[1]);
    }

    [Fact]
    public void AddDropdown_EmptyOptions_Throws_DocumentUnmodified()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        Assert.Throws<ArgumentException>(() =>
            editor.Forms.AddDropdown(0, "dd", Rect, Array.Empty<(string, string)>()));
        Assert.Equal(0, editor.Forms.Count);
    }

    [Fact]
    public void AddDropdown_OnDynamicXfa_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenDynamicXfaShell();
        Assert.Throws<InvalidOperationException>(() => editor.Forms.AddDropdown(0, "dd", Rect, Opts));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringDropdown"`
Expected: COMPILE ERROR.

- [ ] **Step 3: Implement**

Add to `PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs`:

```csharp
    /// <summary>
    /// Creates a combo-box (dropdown) with the given (export, display) options and nothing
    /// selected. Set <see cref="PdfChoiceField.SelectedValues"/> to choose.
    /// </summary>
    /// <exception cref="ArgumentException">Bad name; empty options.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Bad page index.</exception>
    /// <exception cref="InvalidOperationException">The document is a dynamic XFA form.</exception>
    public PdfChoiceField AddDropdown(int pageIndex, string name, PdfRect rect,
        IReadOnlyList<(string Export, string Display)> options)
    {
        GuardAuthoring();
        FieldAuthor.ValidateNewName(_document, name);
        if (options is null || options.Count == 0)
            throw new ArgumentException("A dropdown needs at least one option.", nameof(options));
        PdfDictionary page = FieldAuthor.GetPageDict(_document, pageIndex);

        var opt = new PdfArray();
        foreach ((string export, string display) in options)
        {
            if (export == display)
                opt.Add(PdfString.FromText(export));
            else
                opt.Add(new PdfArray { PdfString.FromText(export), PdfString.FromText(display) });
        }

        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Annot"),
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("FT")] = new PdfName("Ch"),
            [new PdfName("Ff")] = new PdfInteger(1 << (FieldFlags.Combo - 1)),
            [new PdfName("T")] = PdfString.FromText(name),
            [new PdfName("V")] = PdfString.FromText(string.Empty),
            [new PdfName("Opt")] = opt,
            [new PdfName("Rect")] = FieldAuthor.RectArray(rect)
        };
        PdfIndirectReference fieldRef = _document.RegisterObject(dict);
        FieldAuthor.AddToAnnots(_document, page, fieldRef);
        FieldAuthor.EnsureFieldsArray(_document).Add(fieldRef);

        var field = (PdfChoiceField)this[name]!;
        FieldAppearanceGenerator.Regenerate(_document, field);
        return field;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringDropdown"`
Expected: PASS.

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test PdfLibrary.Tests` — all green.

```bash
git add PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs PdfLibrary.Tests/Editing/Forms/AuthoringDropdownTests.cs
git commit -m "feat(forms): AddDropdown authoring

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Remove(fullName)

**Files:**
- Modify: `PdfLibrary/Editing/Forms/FormFlattener.cs:243` and `:284` (make `RemoveWidgetFromAnnots` and `RemoveFieldFromAcroForm` `internal static` instead of `private static`)
- Modify: `PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs` (add method)
- Test: `PdfLibrary.Tests/Editing/Forms/AuthoringRemoveTests.cs`

**Interfaces:**
- Consumes: `FormFlattener.RemoveWidgetFromAnnots(PdfDocument, PdfDictionary page, PdfDictionary widget)`, `FormFlattener.RemoveFieldFromAcroForm(PdfDocument, PdfDictionary fieldDict)`, `FormFlattener.PruneAcroFormIfEmpty(PdfDocument)` (existing, first two promoted to internal); `PdfDocument.GetPages()`.
- Produces: `public bool PdfFormFields.Remove(string fullName)` — true when found and removed; `InvalidOperationException` for signed signature fields.

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Editing/Forms/AuthoringRemoveTests.cs`:

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringRemoveTests
{
    private static readonly PdfRect Rect = new(72, 700, 372, 720);

    [Fact]
    public void Remove_ExistingField_GoneAfterRoundTrip()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddTextField(0, "a", Rect);
        editor.Forms.AddTextField(0, "b", new PdfRect(72, 650, 372, 670));

        Assert.True(editor.Forms.Remove("a"));

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        Assert.Equal(1, reopened.Forms.Count);
        Assert.Null(reopened.Forms["a"]);
        Assert.NotNull(reopened.Forms["b"]);
    }

    [Fact]
    public void Remove_MissingField_ReturnsFalse()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddTextField(0, "a", Rect);
        Assert.False(editor.Forms.Remove("nope"));
        Assert.Equal(1, editor.Forms.Count);
    }

    [Fact]
    public void Remove_LastField_PrunesAcroForm()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "only", Rect);
        PdfLibrary.Structure.PdfDocument doc = field.Doc; // capture before removal

        Assert.True(editor.Forms.Remove("only"));

        Assert.Equal(0, editor.Forms.Count);
        Assert.False(doc.CatalogDictionary!.ContainsKey(new PdfName("AcroForm")));
    }

    [Fact]
    public void Remove_RadioGroup_RemovesParentAndAllWidgets()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainTwoPages();
        editor.Forms.AddRadioGroup("span", new[]
        {
            new PdfRadioOptionPlacement(0, new PdfRect(72, 700, 86, 714), "A"),
            new PdfRadioOptionPlacement(1, new PdfRect(72, 700, 86, 714), "B")
        });

        Assert.True(editor.Forms.Remove("span"));

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        Assert.Equal(0, reopened.Forms.Count);
    }

    [Fact]
    public void Remove_WidgetLeavesPageAnnots()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "a", Rect);
        PdfLibrary.Structure.PdfDocument doc = field.Doc;

        editor.Forms.Remove("a");

        PdfDictionary page = doc.GetPages()[0].Dictionary;
        // /Annots either absent or empty of the removed widget.
        if (page.Get(new PdfName("Annots")) is PdfArray annots)
            Assert.Equal(0, annots.Count);
    }

    [Fact]
    public void Remove_SignedSignature_Refuses()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfSignatureField sig = editor.Forms.AddSignatureField(0, "sig1", Rect);
        // Simulate a signed field: /V present (InternalsVisibleTo gives Dict access).
        sig.Dict[new PdfName("V")] = new PdfDictionary();

        Assert.Throws<InvalidOperationException>(() => editor.Forms.Remove("sig1"));
        Assert.Equal(1, editor.Forms.Count); // still there
    }

    [Fact]
    public void Remove_UnsignedSignature_Succeeds()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddSignatureField(0, "sig1", Rect);
        Assert.True(editor.Forms.Remove("sig1"));
        Assert.Equal(0, editor.Forms.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringRemove"`
Expected: COMPILE ERROR (`Remove` missing).

- [ ] **Step 3: Implement**

In `PdfLibrary/Editing/Forms/FormFlattener.cs` change the two access modifiers (bodies untouched):

- line 243: `private static void RemoveWidgetFromAnnots(` → `internal static void RemoveWidgetFromAnnots(`
- line 284: `private static void RemoveFieldFromAcroForm(` → `internal static void RemoveFieldFromAcroForm(`

Add to `PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs` (add `using PdfLibrary.Document;` to the file's usings):

```csharp
    /// <summary>
    /// Removes the field and its widget annotations from the document. Prunes /AcroForm when
    /// no fields remain. Returns false when no field has that full name.
    /// </summary>
    /// <exception cref="InvalidOperationException">The field is a SIGNED signature — removing it
    /// would silently invalidate the signature; flatten it or leave it. Also thrown for dynamic
    /// XFA documents.</exception>
    public bool Remove(string fullName)
    {
        GuardAuthoring();
        PdfFormField? field = this[fullName];
        if (field is null) return false;
        if (field is PdfSignatureField { IsSigned: true })
            throw new InvalidOperationException(
                $"Field '{fullName}' is a signed signature; removing it would invalidate the " +
                "signature. Flatten it instead, or leave it in place.");

        foreach (PdfDictionary widget in field.WidgetDicts)
            foreach (PdfPage pg in _document.GetPages())
                FormFlattener.RemoveWidgetFromAnnots(_document, pg.Dictionary, widget);

        // For radio groups Dict is the parent field; for merged fields Dict IS the widget.
        FormFlattener.RemoveFieldFromAcroForm(_document, field.Dict);
        FormFlattener.PruneAcroFormIfEmpty(_document);
        return true;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringRemove"`
Expected: PASS.

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test PdfLibrary.Tests` — all green (flatten tests exercise the two promoted methods; behavior unchanged).

```bash
git add PdfLibrary/Editing/Forms/FormFlattener.cs PdfLibrary/Editing/Forms/PdfFormFields.Authoring.cs PdfLibrary.Tests/Editing/Forms/AuthoringRemoveTests.cs
git commit -m "feat(forms): Remove(fullName) — field + widget removal with AcroForm pruning

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Rename + SetWidgetRect

**Files:**
- Modify: `PdfLibrary/Editing/Forms/PdfFormField.cs` (add two methods to the abstract base)
- Test: `PdfLibrary.Tests/Editing/Forms/AuthoringRenameGeometryTests.cs`

**Interfaces:**
- Consumes: `FormFieldTree.Read` / `FormFieldTree.Resolve` (internal static), `FieldAuthor.RectArray`, `FieldAppearanceGenerator.Regenerate` / `EnsureButtonAppearance`; `PdfFormField.Dict/Doc/WidgetDicts` (internal).
- Produces:
  - `public void PdfFormField.Rename(string newPartialName)` — updates `/T`, `PartialName`, `FullName`; uniqueness-validated.
  - `public void PdfFormField.SetWidgetRect(int widgetIndex, PdfRect rect)` — rewrites `/Rect`, regenerates the appearance at the new size (buttons redraw their vector mark).
  - Documented caveat (existing contract): `Widgets` stays a read-time snapshot; callers re-read the field for fresh geometry.

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Editing/Forms/AuthoringRenameGeometryTests.cs`:

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringRenameGeometryTests
{
    private static readonly PdfRect Rect = new(72, 700, 372, 720);

    [Fact]
    public void Rename_UpdatesNameAndPersists()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "old", Rect);
        field.Value = "keep";
        field.Rename("shiny");

        Assert.Equal("shiny", field.FullName);
        Assert.Equal("shiny", field.PartialName);

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        Assert.Null(reopened.Forms["old"]);
        var back = Assert.IsType<PdfTextField>(reopened.Forms["shiny"]);
        Assert.Equal("keep", back.Value);
    }

    [Fact]
    public void Rename_Collision_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        editor.Forms.AddTextField(0, "a", Rect);
        PdfTextField b = editor.Forms.AddTextField(0, "b", new PdfRect(72, 650, 372, 670));
        Assert.Throws<ArgumentException>(() => b.Rename("a"));
        Assert.Equal("b", b.FullName); // unchanged
    }

    [Theory]
    [InlineData("")]
    [InlineData("x.y")]
    public void Rename_InvalidName_Throws(string bad)
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "a", Rect);
        Assert.Throws<ArgumentException>(() => field.Rename(bad));
    }

    [Fact]
    public void Rename_ToSameName_IsNoOpNotCollision()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "same", Rect);
        field.Rename("same"); // must not throw
        Assert.Equal("same", field.FullName);
    }

    [Fact]
    public void SetWidgetRect_MovesTextField_AndRegeneratesAppearance()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        field.Value = "resized";

        field.SetWidgetRect(0, new PdfRect(100, 500, 500, 540));

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        PdfFormField back = reopened.Forms["t"]!;
        Assert.Equal(100, back.Widgets[0].Rect.Left, 3);
        Assert.Equal(500, back.Widgets[0].Rect.Bottom, 3);
        Assert.Equal(500, back.Widgets[0].Rect.Right, 3);
        Assert.Equal(540, back.Widgets[0].Rect.Top, 3);

        // Regenerated /AP BBox matches the new size (400 x 40).
        PdfDictionary widget = back.WidgetDicts[0];
        var ap = Assert.IsType<PdfDictionary>(
            FormFieldTree.Resolve(back.Doc, widget.Get(new PdfName("AP"))));
        PdfObject? nRaw = FormFieldTree.Resolve(back.Doc, ap.Get(new PdfName("N")));
        var n = Assert.IsType<PdfStream>(nRaw);
        var bbox = Assert.IsType<PdfArray>(n.Dictionary.Get(new PdfName("BBox")));
        Assert.Equal(400, ((PdfReal)bbox[2]).Value, 3);
        Assert.Equal(40, ((PdfReal)bbox[3]).Value, 3);
    }

    [Fact]
    public void SetWidgetRect_Checkbox_RedrawsMarkAtNewSize()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfButtonField cb = editor.Forms.AddCheckbox(0, "cb", new PdfRect(72, 700, 86, 714));
        cb.Check();

        cb.SetWidgetRect(0, new PdfRect(72, 700, 100, 728)); // 28x28

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfButtonField>(reopened.Forms["cb"]);
        Assert.True(back.IsChecked); // state survived the redraw
        Assert.Contains("Yes", back.Options); // on-state name survived
        Assert.Equal(100, back.Widgets[0].Rect.Right, 3);
    }

    [Fact]
    public void SetWidgetRect_RadioWidget_MovesOnlyThatWidget()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfButtonField radio = editor.Forms.AddRadioGroup("r", new[]
        {
            new PdfRadioOptionPlacement(0, new PdfRect(72, 700, 86, 714), "A"),
            new PdfRadioOptionPlacement(0, new PdfRect(72, 680, 86, 694), "B")
        });

        radio.SetWidgetRect(1, new PdfRect(200, 680, 214, 694));

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        PdfFormField back = reopened.Forms["r"]!;
        Assert.Equal(72, back.Widgets[0].Rect.Left, 3);   // untouched
        Assert.Equal(200, back.Widgets[1].Rect.Left, 3);  // moved
        Assert.Equal(new[] { "A", "B" }, ((PdfButtonField)back).Options); // states intact
    }

    [Fact]
    public void SetWidgetRect_BadIndex_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        Assert.Throws<ArgumentOutOfRangeException>(() => field.SetWidgetRect(1, Rect));
        Assert.Throws<ArgumentOutOfRangeException>(() => field.SetWidgetRect(-1, Rect));
    }
}
```

Note the test uses `FormFieldTree.Resolve`, `PdfStream`, `field.Dict` — all reachable via `InternalsVisibleTo`. If the single-line text AP `/N` value is an indirect reference to the stream, `FormFieldTree.Resolve` handles it (that is why the assertion resolves before casting).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringRenameGeometry"`
Expected: COMPILE ERROR.

- [ ] **Step 3: Implement on the base class**

Add to `PdfLibrary/Editing/Forms/PdfFormField.cs`, inside `public abstract class PdfFormField` (after the `Widgets` property; add `using PdfLibrary.Builder;` for `PdfRect`):

```csharp
    /// <summary>
    /// Renames the field's partial name (/T). The full name keeps any parent prefix. Throws
    /// when the resulting full name collides with another field, or the name is empty/dotted.
    /// </summary>
    public void Rename(string newPartialName)
    {
        if (string.IsNullOrWhiteSpace(newPartialName))
            throw new ArgumentException("Field name must be non-empty.", nameof(newPartialName));
        if (newPartialName.Contains('.'))
            throw new ArgumentException(
                "Field name must not contain '.' — the period separates hierarchy levels in full names.",
                nameof(newPartialName));

        int cut = FullName.LastIndexOf('.');
        string newFullName = cut < 0 ? newPartialName : FullName[..(cut + 1)] + newPartialName;
        if (!string.Equals(newFullName, FullName, StringComparison.Ordinal) &&
            FormFieldTree.Read(Doc).Any(f => string.Equals(f.FullName, newFullName, StringComparison.Ordinal)))
            throw new ArgumentException($"A field named '{newFullName}' already exists.", nameof(newPartialName));

        Dict[new PdfName("T")] = PdfString.FromText(newPartialName);
        PartialName = newPartialName;
        FullName = newFullName;
    }

    /// <summary>
    /// Rewrites widget <paramref name="widgetIndex"/>'s /Rect and regenerates its appearance at
    /// the new size (buttons redraw their vector mark; text/choice re-run the layout).
    /// <see cref="Widgets"/> stays a read-time snapshot — re-read the field for fresh geometry.
    /// </summary>
    public void SetWidgetRect(int widgetIndex, PdfRect rect)
    {
        if (widgetIndex < 0 || widgetIndex >= WidgetDicts.Count)
            throw new ArgumentOutOfRangeException(nameof(widgetIndex), widgetIndex,
                $"Widget index must be in [0, {WidgetDicts.Count}).");
        PdfDictionary widget = WidgetDicts[widgetIndex];

        // Buttons: capture the on-state and current /AS BEFORE dropping /AP, because
        // EnsureButtonAppearance is a no-op while an on-state appearance exists.
        string? onState = null;
        bool isButton = this is PdfButtonField { Kind: not ButtonKind.PushButton };
        if (isButton)
        {
            if (FormFieldTree.Resolve(Doc, widget.Get(new PdfName("AP"))) is PdfDictionary ap &&
                FormFieldTree.Resolve(Doc, ap.Get(new PdfName("N"))) is PdfDictionary n)
            {
                foreach (KeyValuePair<PdfName, PdfObject> kvp in n)
                {
                    if (kvp.Key.Value != "Off") { onState = kvp.Key.Value; break; }
                }
            }
            widget.Remove(new PdfName("AP"));
        }

        widget[new PdfName("Rect")] = FieldAuthor.RectArray(rect);

        if (isButton && onState is not null)
            FieldAppearanceGenerator.EnsureButtonAppearance(
                Doc, widget, onState, isRadio: ((PdfButtonField)this).Kind == ButtonKind.Radio);
        else
            FieldAppearanceGenerator.Regenerate(Doc, this);
    }
```

Check the existing `using` block of `PdfFormField.cs` — it already has `PdfLibrary.Core`, `PdfLibrary.Core.Primitives`, `PdfLibrary.Structure`, `System.Linq`; add `PdfLibrary.Builder`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringRenameGeometry"`
Expected: PASS.

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test PdfLibrary.Tests` — all green.

```bash
git add PdfLibrary/Editing/Forms/PdfFormField.cs PdfLibrary.Tests/Editing/Forms/AuthoringRenameGeometryTests.cs
git commit -m "feat(forms): Rename + SetWidgetRect field mutations with appearance regeneration

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Base property setters — flags + font

**Files:**
- Modify: `PdfLibrary/Editing/Forms/PdfFormField.cs` (convert `IsReadOnly`, `IsRequired`, `FontName`, `FontSize` from `internal set` auto-properties to backing-field properties with public mutating setters + internal read-path setters)
- Modify: `PdfLibrary/Editing/Forms/FormFieldTree.cs:138-165` (read path switches to the internal setters)
- Modify: `PdfLibrary/Editing/Forms/Standard14FontMap.cs` (add `IsKnown`)
- Test: `PdfLibrary.Tests/Editing/Forms/AuthoringBasePropertyTests.cs`

**Interfaces:**
- Consumes: `FieldFlags.ReadOnly`(=1)/`Required`(=2); `FieldAppearanceGenerator.Regenerate`.
- Produces:
  - `public bool IsReadOnly { get; set; }` / `public bool IsRequired { get; set; }` — write the `/Ff` bit (no appearance change needed).
  - `public string FontName { get; set; }` — standard-14 `/DA` resource name, validated via `Standard14FontMap.IsKnown`; rewrites the field's `/DA` and regenerates.
  - `public double FontSize { get; set; }` — `>= 0`, `0` = auto; rewrites `/DA` and regenerates.
  - `internal void SetIsReadOnlyInternal(bool)` / `SetIsRequiredInternal(bool)` / `SetFontNameInternal(string)` / `SetFontSizeInternal(double)` — read-path setters (no dict writes).
  - `private protected void SetFlagBit(int bit, bool on)` — shared with Task 8.
  - `public static bool Standard14FontMap.IsKnown(string? daName)`

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Editing/Forms/AuthoringBasePropertyTests.cs`:

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringBasePropertyTests
{
    private static readonly PdfRect Rect = new(72, 700, 372, 720);

    [Fact]
    public void SetReadOnlyAndRequired_PersistAsFlagBits()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        field.IsReadOnly = true;
        field.IsRequired = true;

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        PdfFormField back = reopened.Forms["t"]!;
        Assert.True(back.IsReadOnly);
        Assert.True(back.IsRequired);
    }

    [Fact]
    public void ClearReadOnly_ClearsOnlyThatBit()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        field.IsReadOnly = true;
        field.IsRequired = true;
        field.IsReadOnly = false;

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        PdfFormField back = reopened.Forms["t"]!;
        Assert.False(back.IsReadOnly);
        Assert.True(back.IsRequired);
    }

    [Fact]
    public void SetFont_RewritesDa_AndPersists()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        field.Value = "styled";
        field.FontName = "Cour";
        field.FontSize = 14;

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        PdfFormField back = reopened.Forms["t"]!;
        Assert.Equal("Cour", back.FontName);
        Assert.Equal(14, back.FontSize, 3);
        Assert.Equal("styled", Assert.IsType<PdfTextField>(back).Value);
    }

    [Fact]
    public void SetFontName_Unknown_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        Assert.Throws<ArgumentException>(() => field.FontName = "ComicSans");
        Assert.Equal("Helv", field.FontName); // unchanged
    }

    [Fact]
    public void SetFontSize_Negative_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        Assert.Throws<ArgumentOutOfRangeException>(() => field.FontSize = -1);
    }

    [Fact]
    public void ReadingAFormedDoc_DoesNotWriteFlagOrDaEntries()
    {
        // Regression: the read path must use the internal setters — reading a field whose
        // flags come from an inherited /Ff must NOT materialize /Ff or /DA on the field dict.
        byte[] formed = FormTestDocs.WithTextField("plain");
        using PdfDocumentEditor editor = PdfDocumentEditor.Open(new MemoryStream(formed));
        PdfFormField field = editor.Forms["plain"]!;
        _ = field.IsReadOnly;
        _ = field.FontName;
        // The builder-produced field dict has no /Ff; enumeration must not add one.
        Assert.False(field.Dict.ContainsKey(new PdfName("Ff")));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringBaseProperty"`
Expected: FAIL — the flag/font setters don't exist (`IsReadOnly` has an `internal` setter, so `field.IsReadOnly = true` is a compile error from the test project... note `InternalsVisibleTo` makes internal setters CALLABLE from tests, so these lines may compile and silently pass the wrong way. The `SaveAndReopen` assertions still fail because nothing was written to the dict — that is the real red state.)

- [ ] **Step 3: Implement**

In `PdfLibrary/Editing/Forms/Standard14FontMap.cs` add:

```csharp
    /// <summary>True when <paramref name="daName"/> is a recognised standard-14 /DA resource name.</summary>
    public static bool IsKnown(string? daName) => daName switch
    {
        "Helv" or "HeBo" or "HeOb" or "HeBO"
        or "TiRo" or "TiBo" or "TiIt" or "TiBI"
        or "Cour" or "CoBo" or "CoOb" or "CoBO"
        or "Symb" or "Symbol" or "ZaDb" => true,
        _ => false,
    };
```

In `PdfLibrary/Editing/Forms/PdfFormField.cs` replace the four auto-properties (`IsReadOnly`, `IsRequired`, `FontName`, `FontSize`) with (add `using System.Globalization;`):

```csharp
    private bool _isReadOnly;

    /// <summary>/Ff bit 1 — read-only in interactive viewers. Setting writes the flag bit.</summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set { _isReadOnly = value; SetFlagBit(FieldFlags.ReadOnly, value); }
    }

    internal void SetIsReadOnlyInternal(bool value) => _isReadOnly = value;

    private bool _isRequired;

    /// <summary>/Ff bit 2 — required for submission. Setting writes the flag bit.</summary>
    public bool IsRequired
    {
        get => _isRequired;
        set { _isRequired = value; SetFlagBit(FieldFlags.Required, value); }
    }

    internal void SetIsRequiredInternal(bool value) => _isRequired = value;

    private string _fontName = "Helv";

    /// <summary>
    /// Font resource name from the field's effective /DA (own or inherited from the AcroForm
    /// default), e.g. "Helv", "Cour", "TiRo". Setting validates against the standard-14 names,
    /// rewrites the field's own /DA, and regenerates the appearance.
    /// </summary>
    public string FontName
    {
        get => _fontName;
        set
        {
            if (!Standard14FontMap.IsKnown(value))
                throw new ArgumentException(
                    $"'{value}' is not a standard-14 /DA resource name (e.g. Helv, TiRo, Cour, ZaDb).",
                    nameof(value));
            _fontName = value;
            WriteDaAndRegenerate();
        }
    }

    internal void SetFontNameInternal(string value) => _fontName = value;

    private double _fontSize;

    /// <summary>
    /// Font size from the field's effective /DA, in PDF points. <c>0</c> means auto-size.
    /// Setting rewrites the field's own /DA and regenerates the appearance.
    /// </summary>
    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Font size must be >= 0 (0 = auto).");
            _fontSize = value;
            WriteDaAndRegenerate();
        }
    }

    internal void SetFontSizeInternal(double value) => _fontSize = value;

    /// <summary>Sets or clears a 1-based /Ff bit on the field's own dictionary.</summary>
    private protected void SetFlagBit(int bit, bool on)
    {
        int ff = Dict.Get(new PdfName("Ff")) is PdfInteger fi ? (int)fi.Value : 0;
        int mask = 1 << (bit - 1);
        ff = on ? ff | mask : ff & ~mask;
        Dict[new PdfName("Ff")] = new PdfInteger(ff);
    }

    private void WriteDaAndRegenerate()
    {
        string size = _fontSize > 0 ? _fontSize.ToString("0.##", CultureInfo.InvariantCulture) : "0";
        Dict[new PdfName("DA")] = PdfString.FromText($"/{_fontName} {size} Tf 0 g");
        FieldAppearanceGenerator.Regenerate(Doc, this);
    }
```

In `PdfLibrary/Editing/Forms/FormFieldTree.cs` `BuildField` (lines 153-165), replace the four assignments that now have side-effecting setters:

```csharp
        field.FullName      = fullName;
        field.PartialName   = partialName;
        field.Type          = type;
        field.SetIsReadOnlyInternal(isReadOnly);
        field.SetIsRequiredInternal(isRequired);
        field.Dict          = dict;
        field.Doc           = doc;
        field.WidgetDicts   = widgets;

        // Effective /DA (own or inherited from the AcroForm default) → font name + size.
        FieldDa da = FieldDaParser.Parse(inherited.Da);
        field.SetFontNameInternal(da.FontName);
        field.SetFontSizeInternal(da.FontSize);
```

IMPORTANT ordering bug to avoid: `SetFlagBit`/`WriteDaAndRegenerate` dereference `Dict`/`Doc`, and `BuildField` previously assigned `IsReadOnly` BEFORE `Dict`. The internal setters make the ordering irrelevant (they touch only backing fields) — do not "simplify" back to the public setters.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringBaseProperty"`
Expected: PASS.

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test PdfLibrary.Tests` — all green (FormReadTests / FieldWidgetTests confirm the read path is unchanged).

```bash
git add PdfLibrary/Editing/Forms/PdfFormField.cs PdfLibrary/Editing/Forms/FormFieldTree.cs PdfLibrary/Editing/Forms/Standard14FontMap.cs PdfLibrary.Tests/Editing/Forms/AuthoringBasePropertyTests.cs
git commit -m "feat(forms): settable IsReadOnly/IsRequired/FontName/FontSize with /Ff and /DA writes

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: Type-specific setters — text (MaxLength/IsMultiline/Quadding) + choice Options

**Files:**
- Modify: `PdfLibrary/Editing/Forms/PdfFormField.cs` (`PdfTextField` and `PdfChoiceField` sections)
- Modify: `PdfLibrary/Editing/Forms/FormFieldTree.cs:189-196` and `:333-338` (read path switches to internal setters)
- Test: `PdfLibrary.Tests/Editing/Forms/AuthoringTypedPropertyTests.cs`

**Interfaces:**
- Consumes: Task 7's `SetFlagBit` (private protected on the base); `FieldFlags.Multiline`(=13); `FieldAppearanceGenerator.Regenerate`; `PdfChoiceField.SelectedValues` setter (existing — already rewrites `/V`+`/I` and regenerates).
- Produces:
  - `public int? MaxLength { get; set; }` on `PdfTextField` — writes/removes `/MaxLen`.
  - `public bool IsMultiline { get; set; }` on `PdfTextField` — flag + regenerate.
  - `public int Quadding { get; set; }` on `PdfTextField` — 0..2, writes `/Q`, regenerates.
  - `public IReadOnlyList<(string Export, string Display)> Options { get; set; }` on `PdfChoiceField` — rewrites `/Opt`, drops stale selections, regenerates.
  - Internal read-path setters: `SetMaxLengthInternal(int?)`, `SetIsMultilineInternal(bool)`, `SetQuaddingInternal(int)` on `PdfTextField`; `SetOptionsInternal(IReadOnlyList<(string, string)>)` on `PdfChoiceField`. (`IsComb`/`IsPassword`/`IsCombo`/`IsMultiSelect` stay `internal set` — out of scope v1.)

- [ ] **Step 1: Write the failing tests**

Create `PdfLibrary.Tests/Editing/Forms/AuthoringTypedPropertyTests.cs`:

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class AuthoringTypedPropertyTests
{
    private static readonly PdfRect Rect = new(72, 640, 372, 720);

    [Fact]
    public void SetMaxLength_WritesAndClearsMaxLen()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        field.MaxLength = 10;

        using PdfDocumentEditor mid = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfTextField>(mid.Forms["t"]);
        Assert.Equal(10, back.MaxLength);

        back.MaxLength = null;
        using PdfDocumentEditor final = AuthoringTestHelper.SaveAndReopen(mid);
        Assert.Null(Assert.IsType<PdfTextField>(final.Forms["t"]).MaxLength);
    }

    [Fact]
    public void SetMultiline_PersistsFlag_AndValueStillFills()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        field.IsMultiline = true;
        field.Value = "line one\nline two";

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfTextField>(reopened.Forms["t"]);
        Assert.True(back.IsMultiline);
        Assert.Equal("line one\nline two", back.Value);
    }

    [Fact]
    public void SetQuadding_Persists()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        field.Quadding = 2;

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        Assert.Equal(2, Assert.IsType<PdfTextField>(reopened.Forms["t"]).Quadding);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void SetQuadding_OutOfRange_Throws(int bad)
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        Assert.Throws<ArgumentOutOfRangeException>(() => field.Quadding = bad);
    }

    [Fact]
    public void SetMaxLength_Negative_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfTextField field = editor.Forms.AddTextField(0, "t", Rect);
        Assert.Throws<ArgumentOutOfRangeException>(() => field.MaxLength = -5);
    }

    [Fact]
    public void SetOptions_RewritesOpt_AndDropsStaleSelection()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfChoiceField dd = editor.Forms.AddDropdown(0, "dd", new PdfRect(72, 700, 272, 720),
            new[] { ("a", "A"), ("b", "B") });
        dd.SelectedValues = new[] { "b" };

        dd.Options = new[] { ("a", "A"), ("c", "C") }; // "b" no longer exists

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfChoiceField>(reopened.Forms["dd"]);
        Assert.Equal(2, back.Options.Count);
        Assert.Equal(("c", "C"), back.Options[1]);
        Assert.Empty(back.SelectedValues); // stale selection dropped
    }

    [Fact]
    public void SetOptions_KeepsSurvivingSelection()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfChoiceField dd = editor.Forms.AddDropdown(0, "dd", new PdfRect(72, 700, 272, 720),
            new[] { ("a", "A"), ("b", "B") });
        dd.SelectedValues = new[] { "a" };

        dd.Options = new[] { ("a", "Renamed A"), ("c", "C") };

        using PdfDocumentEditor reopened = AuthoringTestHelper.SaveAndReopen(editor);
        var back = Assert.IsType<PdfChoiceField>(reopened.Forms["dd"]);
        Assert.Equal(new[] { "a" }, back.SelectedValues);
        Assert.Equal(new[] { 0 }, back.SelectedIndices);
    }

    [Fact]
    public void SetOptions_Empty_Throws()
    {
        using PdfDocumentEditor editor = AuthoringTestHelper.OpenPlainSinglePage();
        PdfChoiceField dd = editor.Forms.AddDropdown(0, "dd", new PdfRect(72, 700, 272, 720),
            new[] { ("a", "A") });
        Assert.Throws<ArgumentException>(() => dd.Options = Array.Empty<(string, string)>());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringTypedProperty"`
Expected: FAIL (setter assignments compile via InternalsVisibleTo but write nothing to the dict; round-trip assertions go red).

- [ ] **Step 3: Implement**

In `PdfLibrary/Editing/Forms/PdfFormField.cs`, `PdfTextField`: replace the `MaxLength`, `IsMultiline`, `Quadding` auto-properties with:

```csharp
    private int? _maxLength;

    /// <summary>/MaxLen, or null if not set. Setting writes or removes /MaxLen.</summary>
    public int? MaxLength
    {
        get => _maxLength;
        set
        {
            if (value is < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxLength must be >= 0 or null.");
            _maxLength = value;
            if (value is int len)
                Dict[new PdfName("MaxLen")] = new PdfInteger(len);
            else
                Dict.Remove(new PdfName("MaxLen"));
        }
    }

    internal void SetMaxLengthInternal(int? value) => _maxLength = value;

    private bool _isMultiline;

    /// <summary>/Ff bit 13. Setting writes the flag and regenerates the appearance.</summary>
    public bool IsMultiline
    {
        get => _isMultiline;
        set
        {
            _isMultiline = value;
            SetFlagBit(FieldFlags.Multiline, value);
            FieldAppearanceGenerator.Regenerate(Doc, this);
        }
    }

    internal void SetIsMultilineInternal(bool value) => _isMultiline = value;

    private int _quadding;

    /// <summary>/Q: 0=left, 1=centre, 2=right. Setting writes /Q and regenerates the appearance.</summary>
    public int Quadding
    {
        get => _quadding;
        set
        {
            if (value is < 0 or > 2)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Quadding: 0=left, 1=centre, 2=right.");
            _quadding = value;
            Dict[new PdfName("Q")] = new PdfInteger(value);
            FieldAppearanceGenerator.Regenerate(Doc, this);
        }
    }

    internal void SetQuaddingInternal(int value) => _quadding = value;
```

In `PdfChoiceField`, replace the `Options` auto-property with:

```csharp
    private IReadOnlyList<(string Export, string Display)> _options = Array.Empty<(string, string)>();

    /// <summary>
    /// Options from /Opt (export value, display text pairs). Setting rewrites /Opt, drops any
    /// selection whose export no longer exists (re-writing /V and /I), and regenerates the
    /// appearance.
    /// </summary>
    public IReadOnlyList<(string Export, string Display)> Options
    {
        get => _options;
        set
        {
            if (value is null || value.Count == 0)
                throw new ArgumentException("A choice field needs at least one option.", nameof(value));
            _options = value;
            var opt = new PdfArray();
            foreach ((string export, string display) in value)
            {
                if (export == display)
                    opt.Add(PdfString.FromText(export));
                else
                    opt.Add(new PdfArray { PdfString.FromText(export), PdfString.FromText(display) });
            }
            Dict[new PdfName("Opt")] = opt;
            // Re-set the surviving selection: SelectedValues rewrites /V + /I against the new
            // Options and regenerates the appearance.
            SelectedValues = SelectedValues.Where(v => value.Any(o => o.Export == v)).ToList();
        }
    }

    internal void SetOptionsInternal(IReadOnlyList<(string Export, string Display)> value) => _options = value;
```

In `PdfLibrary/Editing/Forms/FormFieldTree.cs` switch the read path to the internal setters:

`BuildTextField` (lines ~189-196) — the object initializer becomes:

```csharp
        var tf = new PdfTextField
        {
            IsComb      = FieldFlags.Has(ff, FieldFlags.Comb),
            IsPassword  = FieldFlags.Has(ff, FieldFlags.Password),
        };
        tf.SetMaxLengthInternal(maxLen);
        tf.SetIsMultilineInternal(FieldFlags.Has(ff, FieldFlags.Multiline));
        tf.SetQuaddingInternal(q);
        tf.SetValueInternal(valueStr);
        return tf;
```

`BuildChoiceField` (lines ~333-338):

```csharp
        var choiceField = new PdfChoiceField
        {
            IsCombo       = isCombo,
            IsMultiSelect = FieldFlags.Has(ff, FieldFlags.MultiSelect),
        };
        choiceField.SetOptionsInternal(options);
        choiceField.SetSelectedValuesInternal(selectedValues);
        choiceField.SetSelectedIndicesInternal(selectedIndices);
        return choiceField;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests --filter "FullyQualifiedName~AuthoringTypedProperty"`
Expected: PASS.

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test PdfLibrary.Tests` — all green (ChoiceFillTests / TextAppearance* pin the fill path).

```bash
git add PdfLibrary/Editing/Forms/PdfFormField.cs PdfLibrary/Editing/Forms/FormFieldTree.cs PdfLibrary.Tests/Editing/Forms/AuthoringTypedPropertyTests.cs
git commit -m "feat(forms): settable MaxLength/IsMultiline/Quadding + choice Options with stale-selection drop

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: Docs + version 2.3.0

**Files:**
- Modify: `PdfLibrary/PdfLibrary.csproj:11` (`<Version>2.2.0</Version>` → `<Version>2.3.0</Version>`)
- Modify: `Docs/Guide.md` (add a "Authoring form fields" subsection to the forms section)
- Modify: `Docs/ApiSurfaceAudit.md` ONLY IF it enumerates `PdfFormFields`/`PdfFormField` members (check first; add the new members to keep it truthful, else skip)

**Interfaces:**
- Consumes: everything shipped in Tasks 1-8.
- Produces: user-facing docs; version metadata for the eventual 2.3.0 publish (the publish itself is user-gated — do NOT run `dotnet nuget push` or tag).

- [ ] **Step 1: Bump the version**

In `PdfLibrary/PdfLibrary.csproj` line 11: `<Version>2.2.0</Version>` → `<Version>2.3.0</Version>`.

- [ ] **Step 2: Write the Guide section**

Open `Docs/Guide.md`, find the forms/fill section (search for "Forms" or "Flatten"), and append this subsection after the fill/flatten content (adjust the heading level to match siblings):

```markdown
### Authoring form fields

`editor.Forms` can create, remove, rename, move, and restyle AcroForm fields on any
existing document — including one with no form at all (the /AcroForm dictionary is
bootstrapped on first use, with appearance streams generated up front; /NeedAppearances
is never relied on). Geometry is PDF user space (Y-up), the same convention as
`PdfFieldWidget.Rect`.

```csharp
using PdfDocumentEditor editor = PdfDocumentEditor.Open("plain.pdf");

PdfTextField name = editor.Forms.AddTextField(0, "name", new PdfRect(72, 700, 372, 720));
name.Value = "Jane Doe";                       // fillable immediately
name.FontName = "Cour"; name.FontSize = 12;    // standard-14 restyling

PdfButtonField subscribe = editor.Forms.AddCheckbox(0, "subscribe", new PdfRect(72, 670, 86, 684));
subscribe.Check();

PdfButtonField size = editor.Forms.AddRadioGroup("size", new[]
{
    new PdfRadioOptionPlacement(0, new PdfRect(72, 640, 86, 654), "S"),
    new PdfRadioOptionPlacement(0, new PdfRect(100, 640, 114, 654), "M"),
});
size.SelectedOption = "M";

PdfChoiceField color = editor.Forms.AddDropdown(0, "color",
    new PdfRect(72, 610, 272, 630), new[] { ("r", "Red"), ("g", "Green") });

editor.Forms.AddSignatureField(0, "sig", new PdfRect(72, 540, 272, 590)); // unsigned placeholder

name.Rename("fullName");
name.SetWidgetRect(0, new PdfRect(72, 700, 500, 724));  // move/resize + appearance regen
editor.Forms.Remove("color");                            // widgets + field tree entry + prune

editor.Save("form.pdf");
```

Notes:

- Names are root-level partial names: non-empty, no `.`, unique — violations throw
  `ArgumentException` before anything is modified.
- All authoring entry points throw `InvalidOperationException` on dynamic XFA documents
  (check `Forms.IsDynamicXfa` first), and `Remove` refuses a **signed** signature field.
- `PdfFormField.Widgets` remains a read-time snapshot: after `SetWidgetRect`, re-read the
  field from `editor.Forms` for fresh geometry.
```

- [ ] **Step 3: Check ApiSurfaceAudit.md**

Run: `grep -n "PdfFormFields\|PdfFormField" Docs/ApiSurfaceAudit.md | head -20`
If it lists forms members, append the new API (AddTextField/AddCheckbox/AddRadioGroup/AddDropdown/AddSignatureField/Remove on `PdfFormFields`; Rename/SetWidgetRect + now-settable properties on `PdfFormField`; `PdfRadioOptionPlacement`). If it does not enumerate member-level API, skip.

- [ ] **Step 4: Full suite + commit**

Run: `dotnet test PdfLibrary.Tests` — all green.

```bash
git add PdfLibrary/PdfLibrary.csproj Docs/Guide.md
git add Docs/ApiSurfaceAudit.md 2>/dev/null || true
git commit -m "docs(forms): authoring guide section; bump to 2.3.0

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

Do NOT push, tag, or publish — the user gates those.
