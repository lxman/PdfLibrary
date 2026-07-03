# Forms Authoring API — Design Spec

Adds field **authoring** to `PdfDocumentEditor.Forms`: create, delete, rename, move/resize,
and restyle AcroForm fields on an *existing* document. Today the editor's forms surface is
read/fill/flatten only; field creation exists solely in the from-scratch builder
(`PdfPageBuilder.AddTextField` et al.). This spec closes that gap so a consumer (Focal's
Forms Design face is the driving one) can turn any plain PDF into a fillable form and edit
the form of a PDF that already has one.

This is sub-project 1 of the Focal forms-design feature; the Focal UI that consumes it is
a separate later spec in the Focal repo.

## 1. Scope

**v1 field types** — exactly the set the builder already renders, so appearance generation
is reuse, not new drawing code:

- Text (`/Tx`): single-line and multiline, quadding, max-length
- Checkbox (`/Btn`)
- Radio group (`/Btn` with per-option widgets, possibly spanning pages)
- Dropdown (`/Ch` combo)
- Signature placeholder (`/Sig`, unsigned)

**Non-goals (v1):** list boxes, push buttons, JavaScript/actions, calculation order,
explicit tab order (creation order = annot order = tab order), field hierarchy (all
created fields are root-level; `Rename` therefore edits a root `/T`), per-widget removal
inside a radio group (remove the group), styling beyond the standard-14 font name/size
already modeled on `PdfFormField`, and authoring on dynamic XFA (guarded exactly like
`Flatten`).

## 2. API surface

### 2.1 Creation — on `PdfFormFields`

```csharp
PdfTextField      AddTextField(int pageIndex, string name, PdfRect rect);
PdfButtonField    AddCheckbox(int pageIndex, string name, PdfRect rect);
PdfButtonField    AddRadioGroup(string name, IReadOnlyList<PdfRadioOptionPlacement> options);
PdfChoiceField    AddDropdown(int pageIndex, string name, PdfRect rect,
                              IReadOnlyList<(string Export, string Display)> options);
PdfSignatureField AddSignatureField(int pageIndex, string name, PdfRect rect);

public readonly record struct PdfRadioOptionPlacement(int PageIndex, PdfRect Rect, string OnState);
```

- `rect` is PDF user space, Y-up — the same convention `PdfFieldWidget.Rect` already
  documents, so a UI can round-trip geometry through one coordinate story.
- `name` is the partial (= full, v1 is root-level) name; validated non-empty, no `.`,
  and unique against the live field tree → `ArgumentException` on violation.
- `pageIndex` validated against the page count.
- Dropdown requires ≥ 1 option; radio group requires ≥ 1 placement with unique non-`Off`
  on-state names.
- Each method returns the live field object read back through `FormFieldTree`, so the
  caller can immediately set a value through the existing fill path.

### 2.2 AcroForm bootstrap

Creating the first field on a document with **no** `/AcroForm` creates the dictionary —
catalog-wired, `/Fields` array, `/DA` `(/Helv 0 Tf 0 g)`, `/DR` with the Helvetica
standard-14 resource (same defaults the builder writes). `/NeedAppearances` is never set:
we always generate appearance streams ourselves. This is the load-bearing requirement —
the design tool must work on any plain PDF.

### 2.3 Deletion — on `PdfFormFields`

```csharp
bool Remove(string fullName);   // true when found and removed
```

Removes the field's widget annotations from their pages' `/Annots`, removes the field
from `/AcroForm /Fields` (walking `/Kids` for fields that came in with hierarchy), then
runs the existing `FormFlattener.PruneAcroFormIfEmpty`. Signed signature fields
(`IsSigned`) refuse with `InvalidOperationException` — deleting a signed field silently
invalidates the signature; the caller must flatten or leave it.

### 2.4 Mutation — on `PdfFormField` (and subtypes)

```csharp
void Rename(string newPartialName);                 // uniqueness-validated, updates /T
void SetWidgetRect(int widgetIndex, PdfRect rect);  // writes /Rect, regenerates AP at new size
```

Property setters promoted from `internal set` to public where design needs them, each
writing the corresponding dict entry and regenerating the appearance via the existing
`FieldAppearanceGenerator` path:

- Base: `IsReadOnly`, `IsRequired` (flag bits), `FontName` (standard-14 resource name,
  e.g. `"Helv"`/`"Cour"`/`"TiRo"` — validated against `Standard14FontMap`), `FontSize`
  (`0` = auto). Font changes rewrite the field's `/DA`.
- `PdfTextField`: `MaxLength`, `IsMultiline`, `Quadding` (`IsComb`/`IsPassword` stay
  read-only v1).
- `PdfChoiceField`: `Options` (rewrites `/Opt`; selection values no longer present are
  dropped from `/V`/`/I`).

Everything else on the field surface keeps its current read/fill contract unchanged.

## 3. Semantics

- **Widget shape:** single-widget fields use the merged field/widget dictionary (one
  dict is both the field and the annotation) — the builder's shape, and the most common
  in the wild. Radio groups get a parent field dict + one widget per option.
- **Appearances:** every creation and geometry/style mutation ends in
  `FieldAppearanceGenerator.Regenerate` (or `ButtonStateWriter` for button states), so a
  created empty text field, a checked new checkbox, and a resized dropdown all render
  correctly in Focal and external viewers without `/NeedAppearances`.
- **`Widgets` snapshot contract unchanged:** `PdfFormField.Widgets` stays a read-time
  snapshot; after `SetWidgetRect` (or any authoring mutation) callers re-read the field
  from `editor.Forms` for fresh geometry — same documented contract as today.
- **Persistence:** standard editor model — mutations live in the loaded `PdfDocument`;
  `editor.Save(...)` writes them. No new save machinery.
- **XFA guard:** all authoring entry points call the same dynamic-XFA guard as `Flatten`
  (creating AcroForm widgets a dynamic-XFA viewer will never show is a trap).
- **Fill interop:** a field created by this API must be fillable through the existing
  fill path in the same session (create → set `Value` → save) and after reopen.

## 4. Implementation shape

New `Editing/Forms/FieldAuthor.cs` (internal static) does dict construction: AcroForm
bootstrap, field dict + widget wiring into page `/Annots` and `/Fields`, removal, rename,
rect updates. `PdfFormFields`/`PdfFormField` methods are thin validated wrappers over it.
Appearance generation, button state writing, `/DA` parsing, and the standard-14 map are
the existing `Editing/Forms` machinery, reused as-is. The builder's `FormField` builders
are NOT reused (they target the new-document writer pipeline); what carries over is their
*dict recipes* — flags, `/MK`, `/AP` states — transcribed into `FieldAuthor` and pinned
by tests comparing against builder output.

## 5. Testing

Round-trip (create → save → reopen → assert) in PdfLibrary.Tests for every type:

- Create on a **blank** doc: AcroForm bootstrapped, field present with correct type,
  geometry, flags; fillable via existing fill path; renders (appearance stream present
  and non-empty; parity-checked against the builder's output for an identical field).
- Create on an **already-formed** doc: existing fields untouched, no name collision.
- Validation: duplicate name, dotted name, bad page index, empty dropdown options,
  duplicate radio on-states → throws, document unmodified.
- `Remove`: widget gone from `/Annots`, field gone from `/Fields`; removing the last
  field prunes `/AcroForm`; signed signature refuses.
- `Rename`: `/T` updated, old name unresolvable, collision throws.
- `SetWidgetRect`: `/Rect` updated, appearance regenerated at the new size (BBox check);
  radio per-widget move.
- Property setters: flag bits, `/DA` rewrite for font changes, `/Opt` rewrite with
  stale-selection drop, multiline/quadding appearance effects.
- Dynamic-XFA doc: every authoring entry point throws, document unmodified.

## 6. Versioning / release

Ships as **Lxman.PdfLibrary 2.3.0** (new public API ⇒ minor bump). This publish also
carries the still-unpublished 2.2.1 search changes (`Width`, `TextOffset` — currently
local-feed-only), so one nuget.org publish gets Focal's main building from the public
feed again. Publish and any push of the PDF repo remain ask-first.
