# Bug Brief: `forms.Flatten()` corrupts complex multi-page AcroForms (W-2)

> **RESOLVED 2026-06-28 — see the "Resolution" section at the bottom.** Investigation disproved the
> serializer/page-0 re-parenting theory below (the serializer round-trips faithfully; nothing lands
> on page 0). The real defects were a geometry-API bug (`PageIndex` collapsing to 0 for orphaned
> widgets) plus three flatten bugs (nested-field removal no-op, un-appeared widgets left live, and
> remove-without-bake data loss). Fixes are in the **working tree + PdfLibrary's local feed**
> (`2.2.0-dev*`); commit + public release pending owner approval. Original report kept verbatim.

**Status:** ~~Open~~ **FIXED in working tree (2026-06-28)** — was confirmed in **published `Lxman.PdfLibrary` 2.1.0** AND source (`PdfLibrary/Editing/Forms/FormFlattener.cs`).
**Reported by:** PdfLibrary (consumer app) — its "Export Flattened" produced a file that silently lost all entered data.
**Severity:** High — flattening a real-world complex form **silently loses user data** and corrupts document structure. Plain save (no flatten) is unaffected and works correctly.

---

## TL;DR

On a simple builder-made single-page form, `Flatten()` works (the existing unit test passes). On a **complex multi-page / multi-copy** AcroForm (the IRS fillable **W-2**, producer "Designer 6.5", 272 fields across page indices 1,2,3,5,7,9), `Flatten()` + `Save()` produces a broken file:

1. **Fields are NOT removed** — `Forms.Count` stays **272** after flatten (should be **0**).
2. **Values are NOT baked** into page content — the entered text does not appear when rendered.
3. **~22 widgets are re-parented to page index 0** (the instructions page, which originally had **zero** widgets) — a structural corruption. The touched fields' widgets migrate off their real pages onto page 0.

Net effect: the flattened form looks blank (no baked values, widget `/AP` no longer drawn), still contains a live AcroForm, and has widgets stranded on the wrong page.

---

## Reproduction (faithful, against published 2.1.0 — also fails on current source)

Fixture: `TestPDFs/fw2.pdf` (the blank fillable W-2; 11 pages, 272 fields).

```csharp
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

byte[] bytes = File.ReadAllBytes("fw2.pdf");
using (var editor = PdfDocumentEditor.Open(new MemoryStream(bytes), null))
{
    var forms = editor.Forms;
    // two EDITABLE (ro=false) text fields on Copy1 (page index 2):
    foreach (var name in new[] {
        "topmostSubform[0].Copy1[0].BoxA_ReadOrder[0].f2_01[0]",
        "topmostSubform[0].Copy1[0].Col_Left[0].f2_04[0]" })
        if (forms.TryGet(name, out PdfFormField? f) && f is PdfTextField t) t.Value = "TEST123";

    forms.Flatten();                              // <-- the defect
    using var fs = File.Create("out_flat.pdf");
    editor.Save(fs, PdfSaveOptions.Default);
}

// reopen and inspect
using var ed2 = PdfDocumentEditor.Open(File.OpenRead("out_flat.pdf"), null);
// observed: ed2.Forms.Count == 272  (expected 0)
//           the two values are present but their widgets are on page index 0
//           page histogram gains "0:22" and loses ~1 widget from each real page
```

### Observed vs expected

| | `Flatten()` = **false** (plain save) | `Flatten()` = **true** |
|---|---|---|
| `Forms.Count` after reopen | 272 (fields retained — correct) | **272** — *expected 0* ❌ |
| Entered values | present, widgets on correct page (idx 2) ✓ | present but widgets on **page 0** ❌ |
| Widget page histogram | `1:46 2:45 3:45 5:45 7:45 9:46` (== original) ✓ | `0:22 1:42 2:40 3:42 5:42 7:42 9:42` — **22 widgets migrated to page 0** ❌ |
| Rendered result | values visible via `/AP` on Copy1 ✓ | values **not** baked; form appears blank ❌ |

The **plain-save column proves the data path and serializer are fine** — the corruption is introduced specifically by the `Flatten()` step (and/or how the post-flatten document is serialized).

---

## Where to look

`PdfLibrary/Editing/Forms/FormFlattener.cs`:

- **`FlattenAll`** → `FormFieldTree.Read(doc)` snapshot → `FlattenField` per field → `PruneAcroFormIfEmpty`.
- **`FlattenField`**: for each widget, `FindOwningPage(doc, pages, widget)`; if null → `continue` (widget neither painted nor removed); then `RemoveFieldFromAcroForm(doc, field.Dict)` unconditionally.

Two hypotheses, both consistent with the evidence — please verify which (or both):

1. **`FindOwningPage` fails to match the W-2's widgets** (returns null), so widgets are never painted and never removed from `/Annots`. The W-2's fields are deeply nested hierarchical subforms (`topmostSubform[0].CopyA[0].Col_Left[0]…`) and the widgets may be referenced via a structure the object-number / reference-equality match in `FindOwningPage` doesn't catch (e.g. inherited `/P`, indirect-in-array vs direct, or kid widgets under a non-terminal field). If matching fails, nothing is removed — but `Forms.Count` staying 272 then implies field removal isn't taking effect either (see #2).

2. **Field removal / serialization doesn't persist, and the full-rewrite serializer re-parents orphaned widgets to page 0.** `RemoveFieldFromAcroForm` runs but `Forms.Count` is still 272 on reopen — so either the removal isn't serialized, or `FormFieldTree.Read` reconstructs fields from surviving widget `/Parent` chains. The **page-0 migration is the strongest clue**: when the writer serializes a widget whose page membership is broken/ambiguous (orphaned, or `/P` dangling), it appears to default it to the first page. Check how `PdfDocumentWriter` assigns widget `/P` and page `/Annots` membership on full rewrite for widgets that were not cleanly removed.

The "22 widgets to page 0" count is roughly stable regardless of how many fields are edited, which points at a systematic mis-assignment during flatten/serialize rather than something tied to the edited fields.

---

## Acceptance / regression test

Add a flattener test using a **complex multi-page form fixture** (the W-2 `fw2.pdf`, or a hand-built multi-page multi-copy form with hierarchical subform field names). After `set value → Flatten() → Save() → reopen`:

- `editor.Forms.Count == 0` (AcroForm fully removed).
- No widget annotations remain on any page (`/Annots` has no `/Widget` of the flattened fields); in particular **no widgets land on a page that had none originally** (assert the page-0 widget count stays 0).
- The entered values are present as **baked page content** on the **correct** page (rasterize Copy1/page-index-2 and assert non-background ink in the field rects — mirrors the existing appearance-render test approach).
- Keep the existing simple-form flatten test green.

The current single-page builder fixture (`AddTextField`+`AddCheckbox`, one page) is too simple to exercise this path — it must be supplemented, not replaced.

---

## Coordination

- **Consumer impact:** PdfLibrary's "Export Flattened" is unusable until this lands; PdfLibrary's plain "Save" is unaffected and correct. PdfLibrary will guard/disable the flatten action meanwhile.
- This is **not** caused by the consumer — reproduced with the exact published 2.1.0 library calls.

### Handoff: publish into the LOCAL feed, NOT nuget.org

We are co-developing the library and PdfLibrary locally and do **not** want public releases for every iteration. When this fix is ready:

1. Commit the change in this repo.
2. From the repo root, run **`.\pack-local.ps1`** — it builds the current source, packs `Lxman.PdfLibrary` as a fresh `2.2.0-dev<timestamp>` into PdfLibrary's local NuGet feed (`C:\Users\jorda\RiderProjects\PdfLibrary\.nuget\local-feed`), **and pins PdfLibrary to that exact version** (writes its gitignored `Directory.Build.props.local`). PdfLibrary then picks it up on a plain `dotnet build` — no `dotnet restore --force` needed (a floating ref alone leaves restore on a stale prior dev build).

**Do NOT create a GitHub Release** and **do NOT run the `publish-nuget.yml` workflow** for this — that publishes to nuget.org (the public, effectively irreversible path) and requires the owner's explicit approval. The local pack is the whole handoff: no tag, no Release, no nuget.org push, no version bump in the csproj (the script overrides the package version per-pack). A real public release happens only later, once a batch of fixes is ready and the owner approves.

---

## Resolution (2026-06-28)

Investigation (instrumented repros against `TestPDFs/fw2.pdf` + inspection of PdfLibrary's actual
`fw23.pdf`) found the brief's lead hypotheses were wrong, and uncovered the true causes.

### What was DISPROVEN
- **"Serializer re-parents ~22 widgets to page 0."** Not real. Page 0's content stream is byte-identical
  input→output; every surviving widget's `/P` is correct; Chrome, Adobe, SkiaSharp, and the WPF viewer
  all render page 1 clean. The "page 0" reading was a measurement artifact of orphaned widgets (below).
- **"Filled fields painted onto the instructions page."** The page content is clean. The fields *appeared*
  on page 1 only in PdfLibrary-based renderers (WPF viewer, Avalonia target) because of the geometry bug.

### Real root causes (all fixed, test-first, full suite green)
1. **RC-A — `PageIndex` collapses to 0 (the visible corruption).** `FormFieldTree.PopulateWidgets` did
   `int pageIndex = -1; dict.TryGetValue(objNum, out pageIndex);` — on a miss `TryGetValue` writes
   `default(int)=0`, clobbering the intended `-1`. So any widget not in a page's `/Annots` (an orphan)
   reported page 0. Geometry-driven renderers (which read `PdfFieldWidget.PageIndex`) then drew it on
   page 1. Chrome/Adobe read `/Annots` directly, so they were unaffected. **Fix:** capture the bool,
   set `-1` on miss. The WPF viewer already skipped `PageIndex < 0`, so no consumer change was needed.
2. **RC1 — nested fields never removed (creates the orphans).** `RemoveFieldFromAcroForm` scanned only
   the top-level `/AcroForm/Fields`; the W-2's terminal fields are nested under `topmostSubform[0]`, so
   removal no-op'd → `Forms.Count` stayed 272, `/AcroForm` (and `/XFA`) survived, and removed widgets
   lingered as orphans. **Fix:** recurse the field tree, pruning emptied parent subforms.
3. **RC3 — remove-without-bake data loss.** On the "`/AP` present but `/N` not a Form XObject" path the
   widget was removed without painting. **Fix:** only remove an un-appeared widget when the field has no
   value to lose.
4. **RC2 — un-appeared widgets left live / values not baked.** Widgets with no `/AP` (252/272 on this XFA
   form) were `continue`d without removal or generation. **Fix:** generate an appearance for valued
   text/choice fields before baking; remove empty un-appeared widgets cleanly.
5. **XFA gate.** A *hybrid* form (W-2): full flatten empties `/Fields`, so `/AcroForm` + `/XFA` are
   removed wholesale — no stale XFA. A *dynamic* XFA form (no bakeable AcroForm): `Flatten()` now refuses
   with `InvalidOperationException` and the new `PdfFormFields.IsDynamicXfa` detector lets the app decline
   to edit/flatten — stripping `/XFA` there would destroy the only representation of the form.

### Tests
`PdfLibrary.Tests/Editing/Forms/`: `OrphanWidgetPageIndexTests` (RC-A), `FlattenHierarchicalTests`
(RC1/RC2/RC3 + no-widget-on-page-0 + value-bakes-on-its-page), `XfaFlattenGateTests` (detector, refuse,
hybrid-strip). Existing simple-form `FlattenTests` stay green. Full suite: 1314 passing.

### Delivery
Packed to PdfLibrary's local feed via `pack-local.ps1` as `2.2.0-dev<timestamp>`. No commit, no nuget.org
publish yet — both await owner approval. When publishing publicly, this is a **minor** bump (additive
`IsDynamicXfa` API + bug fixes): target **2.2.0**.
