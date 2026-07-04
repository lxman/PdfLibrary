# Brief: standard-14 font name on FreeText annotations (`/DA`)

**For:** the PdfLibrary agent. **Repo:** `C:\Users\jorda\RiderProjects\PDF` (branch off `master`).
**Requested by:** Focal (annotation text-editing + inspector feature). **Folds into:** the pending **2.2.0** publish.
**Status:** uncommitted handoff brief — implement, test, commit in this repo.

---

## Goal

Let callers pick which of the **14 standard PDF fonts** a FreeText annotation uses, instead of always Helvetica. The font is written into the annotation's `/DA` string and must round-trip on reopen and bake correctly into the `/AP` appearance.

## Key finding (this is mostly already done)

The downstream path **already honors a variable font name** — the appearance generator parses the font from `/DA`, the resolver synthesizes the font dict, the content builder references it, and the `/DA` string is surfaced verbatim on read. Only **two write literals** and the **resolver's BaseFont map** are hardcoded to Helvetica. So this is a small, surgical change.

Already font-aware (no change needed):
- `PdfLibrary\Editing\Annotations\AnnotationAppearanceGenerator.cs` → `GenerateFreeText` (parses `/DA` via `FieldDaParser`, calls `AppearanceFontResolver.Resolve`).
- `PdfLibrary\Editing\Forms\FieldDaParser.cs` (extracts font name, strips leading `/`).
- `PdfLibrary\Editing\Annotations\AnnotationContentBuilder.cs` → `FreeText(...)` (references the resource name).
- `PdfLibrary\Editing\PdfAnnotationInfo.cs` `DefaultAppearance` + its populate at `PdfPageCollection.Annotations.cs:120` (raw `/DA` surfaced verbatim).

## The cross-repo contract — standard-14 `/DA` resource names

Focal sends, and expects back, these `/DA` font names. Map each to its `/BaseFont`:

| `/DA` name | `/BaseFont` |
|---|---|
| Helv | Helvetica |
| HeBo | Helvetica-Bold |
| HeOb | Helvetica-Oblique |
| HeBO | Helvetica-BoldOblique |
| TiRo | Times-Roman |
| TiBo | Times-Bold |
| TiIt | Times-Italic |
| TiBI | Times-BoldItalic |
| Cour | Courier |
| CoBo | Courier-Bold |
| CoOb | Courier-Oblique |
| CoBO | Courier-BoldOblique |
| Symb (also accept "Symbol") | Symbol |
| ZaDb | ZapfDingbats |

Default everywhere is `Helv` → keeps all existing callers working.

---

## Task 1 — thread `fontName` through the editing-path `AddFreeText`

**Files:**
- `PdfLibrary\Editing\Annotations\PdfPageAnnotator.cs` — `AddFreeText` (lines ~55–68)
- `PdfLibrary\Editing\PdfPageCollection.Annotations.cs` — `AddFreeText` (lines ~90–93)
- Test: `PdfLibrary.Tests\Editing\Annotations\AnnotationTextRetrofitTests.cs`

**Step 1 — failing test** (xUnit; match the existing idiom in that file — `BlankPage()`, `SaveToBytes`, `SavedAnnotHasApN`, `RenderNonWhiteInRect`):

```csharp
[Fact]
public void AddFreeText_WithFont_RoundTripsFontInDa()
{
    var rect = new PdfRect(100, 600, 400, 660);
    byte[] saved;
    using (var ms = new MemoryStream(BlankPage()))
    using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms))
    {
        editor.Pages.AddFreeText(0, rect, "Times text", 18.0, PdfColor.Black, quadding: 0, fontName: "TiRo");
        saved = SaveToBytes(editor);
    }
    using var ms2 = new MemoryStream(saved);
    using PdfDocumentEditor reopened = PdfDocumentEditor.Open(ms2);
    PdfAnnotationInfo a = Assert.Single(reopened.Pages.GetAnnotations(0));
    Assert.Contains("/TiRo", a.DefaultAppearance);
}
```

Run: `dotnet test --filter "FullyQualifiedName~AnnotationTextRetrofitTests.AddFreeText_WithFont_RoundTripsFontInDa"` → expect FAIL (no `fontName` param).

**Step 2 — implement.** `PdfPageAnnotator.cs`:

```csharp
internal static int AddFreeText(PdfDocument doc, PdfDictionary page, PdfIndirectReference pageRef,
    PdfRect rect, string text, double fontSize, PdfColor color, int quadding, string fontName = "Helv")
{
    PdfDictionary annot = NewAnnot(doc, page, pageRef, "FreeText", rect, out PdfIndirectReference annotRef);
    annot[new PdfName("Contents")] = PdfString.FromText(text);
    string da = string.Format(CultureInfo.InvariantCulture,
        "/{0} {1:0.####} Tf {2:0.###} {3:0.###} {4:0.###} rg",
        string.IsNullOrEmpty(fontName) ? "Helv" : fontName, fontSize, color.R, color.G, color.B);
    annot[new PdfName("DA")] = PdfString.FromText(da);
    annot[new PdfName("Q")] = new PdfInteger(quadding);
    AnnotationAppearanceGenerator.Generate(doc, annot);
    return annotRef.ObjectNumber;
}
```

`PdfPageCollection.Annotations.cs`:

```csharp
public int AddFreeText(int index, PdfRect rect, string text, double fontSize, PdfColor color,
    int quadding = 0, string fontName = "Helv")
{
    PdfDictionary page = PageAt(index);
    return PdfPageAnnotator.AddFreeText(_document, page, PageRef(index), rect, text, fontSize, color, quadding, fontName);
}
```

Run the filter again → PASS (and the existing `AddFreeText_RoundTrips_Renders` stays green).

**Commit:** `feat(annotations): AddFreeText accepts standard-14 font name (editing path)`

---

## Task 2 — thread `fontName` through the builder path

**Files (locate exact lines yourself):**
- `git grep -n "class PdfFreeTextAnnotation"` — the builder annotation class
- `PdfLibrary\Builder\...\PdfDocumentWriter.cs` — `WriteFreeTextAnnotation` (~line 2577) and the FreeText appearance call (~line 2622)
- builder `AddFreeText` author method — `git grep -n "AddFreeText"` under `Builder`
- the existing builder-path FreeText test (match its idiom)

**Step 1 — failing test:** add a builder-path variant that builds with `fontName: "Cour"`, saves, reopens via `PdfDocumentEditor`, asserts `Assert.Contains("/Cour", a.DefaultAppearance)`.

**Step 2 — implement:**
- `PdfFreeTextAnnotation`: add `public string FontName { get; set; } = "Helv";`
- builder `AddFreeText` author method: add `string fontName = "Helv"` param → assign to `FontName`.
- `WriteFreeTextAnnotation` (~2577–2578): interpolate `freeText.FontName` (guard empty → `"Helv"`) into the `/DA` format string (currently hardcodes `/Helv`).
- appearance call (~2622): pass the font name instead of the literal:
  ```csharp
  content = Editing.Annotations.AnnotationContentBuilder.FreeText(
      h, string.IsNullOrEmpty(ft.FontName) ? "Helv" : ft.FontName, ft.FontSize, colorOps, ft.Text);
  ```

Run the test → PASS.

**Commit:** `feat(annotations): AddFreeText font name on builder path`

---

## Task 3 — extend `AppearanceFontResolver` BaseFont mapping to all 14

**Files:**
- `PdfLibrary\Editing\Forms\AppearanceFontResolver.cs` — BaseFont switch (lines ~55–60)
- Test: `PdfLibrary.Tests\Editing\Annotations\AnnotationTextRetrofitTests.cs`

Currently the synthesized font dict maps only `ZaDb`/`Symbol` correctly and everything else → Helvetica, so Times/Courier would silently render as Helvetica.

**Step 1 — failing test:**

```csharp
[Theory]
[InlineData("TiRo")]
[InlineData("Cour")]
[InlineData("HeBo")]
public void AddFreeText_WithFont_BakesAppearanceWithThatFont(string fontName)
{
    var rect = new PdfRect(100, 600, 400, 660);
    byte[] saved;
    using (var ms = new MemoryStream(BlankPage()))
    using (PdfDocumentEditor editor = PdfDocumentEditor.Open(ms))
    {
        editor.Pages.AddFreeText(0, rect, "Glyphs", 18.0, PdfColor.Black, quadding: 0, fontName: fontName);
        saved = SaveToBytes(editor);
    }
    Assert.True(SavedAnnotHasApN(saved, "FreeText"));
    Assert.True(RenderNonWhiteInRect(saved, rect) > 0, $"{fontName} FreeText did not render");
}
```

If a helper exists to read the AP's `/Resources /Font /BaseFont`, prefer asserting the exact BaseFont (e.g. `Times-Roman`) so the test fails before the switch is extended; otherwise this guards "renders with a resolvable font" and the switch below provides correctness.

**Step 2 — implement** (replace the switch):

```csharp
string baseFont = resName switch
{
    "Helv" => "Helvetica",
    "HeBo" => "Helvetica-Bold",
    "HeOb" => "Helvetica-Oblique",
    "HeBO" => "Helvetica-BoldOblique",
    "TiRo" => "Times-Roman",
    "TiBo" => "Times-Bold",
    "TiIt" => "Times-Italic",
    "TiBI" => "Times-BoldItalic",
    "Cour" => "Courier",
    "CoBo" => "Courier-Bold",
    "CoOb" => "Courier-Oblique",
    "CoBO" => "Courier-BoldOblique",
    "Symb" => "Symbol",
    "Symbol" => "Symbol",
    "ZaDb" => "ZapfDingbats",
    _ => "Helvetica",
};
```

Run `dotnet test` (full suite) → PASS. **This resolver is shared with AcroForm field appearances — confirm no form-fill regressions.**

**Commit:** `feat(annotations): map all standard-14 /DA names to /BaseFont in appearance resolver`

---

## After the three tasks — 2.2.0 publish

Bump `PdfLibrary\PdfLibrary.csproj` version to **2.2.0** and cut the owner-approved GitHub Release. This is the gate Focal waits on (it also carries the already-pending AddHighlight/AddNote + Multiply-highlight `/AP` + flatten fix). Once published to nuget.org, Focal flips `LxmanPdfLibraryVersion` default to `2.2.0` and consumes it.

## Acceptance

- `dotnet test` green in this repo, including the new font round-trip + bake tests and no form-fill regressions.
- `AddFreeText(..., fontName: "TiRo")` round-trips `/TiRo` in `/DA` and bakes a Times appearance; default (no `fontName`) is unchanged Helvetica.
