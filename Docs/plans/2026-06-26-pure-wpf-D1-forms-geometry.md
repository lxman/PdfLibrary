# Plan D1 — Forms Geometry API

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the geometry a UI needs to place native controls over PDF form fields: a public `PdfFieldWidget` (per-widget Rect + page index + on-state) and a public `PageGeometry` (PDF↔rendered-image transform). Cross-platform core; no rendering dependency.

**Architecture:** `PageGeometry` is a value type built from a page's public CropBox/MediaBox/Rotate + a scale — the same initial transform the renderers apply, plus its inverse and pixel size. `PdfFormField.Widgets` is promoted from an internal list of raw widget dicts to a public list of `PdfFieldWidget` projections (Rect from `/Rect`, page index by scanning page `/Annots`, on-state from `/AP /N`). Filling already works (existing public `field.Value`/`.Check()`/etc. + `Forms.Flatten()`); this plan only adds the geometry.

**Tech Stack:** C# 12, .NET 8/9/10, `System.Numerics`, xUnit.

## Global Constraints

- Core `PdfLibrary` stays SkiaSharp-free; multi-target net8.0/9.0/10.0.
- New public types live in their existing namespaces: `PageGeometry` in `PdfLibrary.Document` (next to `PdfPage`); `PdfFieldWidget` in `PdfLibrary.Editing.Forms` (next to `PdfFormField`).
- The coordinate contract matches the renderers exactly: PDF user space (Y-up) → image pixels (Y-down, top-left), rotation-0 = `matrix(scale, 0, 0, -scale, -cropX*scale, (cropY+height)*scale)` where `cropX=cropBox.X1`, `cropY=cropBox.Y1`, `width=cropBox.Width`, `height=cropBox.Height`. (Same as `SkiaSharpRenderTarget.BeginPage` / `SvgRenderTarget.InitialTransform`.)
- `Rect` on widgets is `PdfLibrary.Builder.PdfRect` (`Left/Bottom/Right/Top`, PDF coords), matching `PdfAnnotationInfo.Rect`.
- Full suite stays green; xUnit via global usings.

## File Structure

- `PdfLibrary/Document/PageGeometry.cs` (create) — the transform value type.
- `PdfLibrary/Document/PdfPage.cs` (modify) — add `GetGeometry(double scale)`.
- `PdfLibrary/Editing/Forms/PdfFieldWidget.cs` (create) — the widget projection type.
- `PdfLibrary/Editing/Forms/PdfFormField.cs` (modify) — `internal Widgets` → `internal WidgetDicts` + public `IReadOnlyList<PdfFieldWidget> Widgets`.
- `PdfLibrary/Editing/Forms/FormFieldTree.cs` (modify) — set `WidgetDicts`; build the public `Widgets` projection at read time (page-index map + on-state).
- `PdfLibrary/Editing/Forms/FormFlattener.cs` (modify) — `field.Widgets` → `field.WidgetDicts`.
- Tests: `PdfLibrary.Tests/Document/PageGeometryTests.cs`, `PdfLibrary.Tests/Editing/Forms/FieldWidgetTests.cs`, `PdfLibrary.Tests/Editing/Forms/FormGeometryRoundTripTests.cs`.

---

## Task 1: `PageGeometry` + `PdfPage.GetGeometry`

**Files:**
- Create: `PdfLibrary/Document/PageGeometry.cs`
- Modify: `PdfLibrary/Document/PdfPage.cs`
- Test: `PdfLibrary.Tests/Document/PageGeometryTests.cs`

**Background:** The transform a client needs to place controls over fields. `PdfToImage` maps PDF user space → rendered-image pixels (identical to the renderers' initial transform). `ImageToPdf` is the inverse (click→PDF hit-testing). Built from the page's public `GetCropBox()` (a `PdfRectangle` with `X1`/`Y1`/`Width`/`Height`), `Rotate` (0/90/180/270), and a scale.

**Interfaces:**
- Consumes: `PdfPage.GetCropBox()` → `PdfRectangle` (`X1`,`Y1`,`Width`,`Height`); `PdfPage.Rotate` (int); `PdfLibrary.Builder.PdfRect`.
- Produces:
  - `public readonly struct PageGeometry` with `Matrix3x2 PdfToImage`, `Matrix3x2 ImageToPdf`, `int PixelWidth`, `int PixelHeight`, and `PdfRect MapRectToImage(PdfRect pdfRect)`.
  - `public PageGeometry PdfPage.GetGeometry(double scale = 1.0)`.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Document/PageGeometryTests.cs`:

```csharp
using System.Numerics;
using PdfLibrary.Builder;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Document;

public class PageGeometryTests
{
    // A simple 1-page letter PDF (no crop, no rotation) via the builder.
    private static PdfDocument OneLetterPage()
    {
        byte[] pdf = PdfDocumentBuilder.Create().AddPage(_ => { }).ToByteArray();
        return PdfDocument.Load(new MemoryStream(pdf));
    }

    [Fact]
    public void PdfToImage_RotationZero_MapsBottomLeftToImageBottomLeft()
    {
        using PdfDocument doc = OneLetterPage();
        PdfPage page = doc.GetPage(0)!;
        double h = page.GetMediaBox().Height;
        PageGeometry g = page.GetGeometry(2.0);

        // PDF origin (0,0) is the page bottom-left → image bottom (y = h*scale), x = 0.
        Vector2 origin = Vector2.Transform(Vector2.Zero, g.PdfToImage);
        Assert.Equal(0, origin.X, 3);
        Assert.Equal(h * 2.0, origin.Y, 3);

        // PixelWidth/Height reflect cropbox * scale.
        Assert.Equal((int)Math.Round(page.GetCropBox().Width * 2.0), g.PixelWidth);
        Assert.Equal((int)Math.Round(page.GetCropBox().Height * 2.0), g.PixelHeight);
    }

    [Fact]
    public void ImageToPdf_IsInverseOfPdfToImage()
    {
        using PdfDocument doc = OneLetterPage();
        PageGeometry g = doc.GetPage(0)!.GetGeometry(1.5);
        var p = new Vector2(123.4f, 567.8f);
        Vector2 roundTrip = Vector2.Transform(Vector2.Transform(p, g.PdfToImage), g.ImageToPdf);
        Assert.Equal(p.X, roundTrip.X, 2);
        Assert.Equal(p.Y, roundTrip.Y, 2);
    }

    [Fact]
    public void MapRectToImage_FlipsYAndScales()
    {
        using PdfDocument doc = OneLetterPage();
        PdfPage page = doc.GetPage(0)!;
        double h = page.GetMediaBox().Height;
        PageGeometry g = page.GetGeometry(1.0);

        // A 100x20 rect near the PDF bottom maps to a rect near the image bottom.
        PdfRect img = g.MapRectToImage(new PdfRect(50, 10, 150, 30));
        Assert.Equal(50, img.Left, 3);
        Assert.Equal(150, img.Right, 3);
        // PDF y in [10,30] → image y in [h-30, h-10] (Y-flip).
        Assert.Equal(h - 30, Math.Min(img.Top, img.Bottom), 3);
        Assert.Equal(h - 10, Math.Max(img.Top, img.Bottom), 3);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PageGeometryTests"`
Expected: FAIL — `PageGeometry`/`GetGeometry` don't exist.

- [ ] **Step 3: Implement**

Create `PdfLibrary/Document/PageGeometry.cs`:

```csharp
using System.Numerics;
using PdfLibrary.Builder;

namespace PdfLibrary.Document;

/// <summary>
/// Maps a PDF page between user space (Y-up, PDF points) and rendered-image pixels
/// (Y-down, top-left origin) at a chosen scale — the same transform the renderers apply.
/// Use it to place UI (e.g. form-field controls) over a rendered page, and to hit-test clicks.
/// </summary>
public readonly struct PageGeometry
{
    /// <summary>PDF user space → image pixels.</summary>
    public Matrix3x2 PdfToImage { get; }
    /// <summary>Image pixels → PDF user space (inverse of <see cref="PdfToImage"/>).</summary>
    public Matrix3x2 ImageToPdf { get; }
    /// <summary>Rendered image width in pixels at this scale.</summary>
    public int PixelWidth { get; }
    /// <summary>Rendered image height in pixels at this scale.</summary>
    public int PixelHeight { get; }

    internal PageGeometry(Matrix3x2 pdfToImage, int pixelWidth, int pixelHeight)
    {
        PdfToImage = pdfToImage;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        ImageToPdf = Matrix3x2.Invert(pdfToImage, out Matrix3x2 inv) ? inv : Matrix3x2.Identity;
    }

    /// <summary>Maps a rect in PDF user space to a normalized image-pixel rect.</summary>
    public PdfRect MapRectToImage(PdfRect pdfRect)
    {
        Vector2 a = Vector2.Transform(new Vector2((float)pdfRect.Left, (float)pdfRect.Bottom), PdfToImage);
        Vector2 b = Vector2.Transform(new Vector2((float)pdfRect.Right, (float)pdfRect.Top), PdfToImage);
        return new PdfRect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }
}
```

In `PdfLibrary/Document/PdfPage.cs`, add (the transform mirrors `SvgRenderTarget.InitialTransform`):

```csharp
    /// <summary>
    /// Builds the PDF→rendered-image geometry for this page at the given scale — the transform
    /// the renderers use, plus its inverse and the rendered pixel size. Place UI over a rendered
    /// page (e.g. form controls via <see cref="PdfFieldWidget"/>) using <see cref="PageGeometry"/>.
    /// </summary>
    public PageGeometry GetGeometry(double scale = 1.0)
    {
        PdfRectangle crop = GetCropBox();
        double width = crop.Width, height = crop.Height;
        double cropX = crop.X1, cropY = crop.Y1;
        int rotation = Rotate;

        double finalHeight = rotation is 90 or 270 ? width : height;
        (float tx, float ty) = rotation switch
        {
            90 => (0f, (float)width),
            180 => ((float)width, (float)height),
            270 => ((float)height, 0f),
            _ => (0f, 0f)
        };
        var rad = (float)(-rotation * Math.PI / 180.0);
        Matrix3x2 m = Matrix3x2.CreateTranslation((float)-cropX, (float)-cropY)
                    * Matrix3x2.CreateRotation(rad)
                    * Matrix3x2.CreateTranslation(tx, ty)
                    * Matrix3x2.CreateScale((float)scale, (float)-scale)
                    * Matrix3x2.CreateTranslation(0, (float)(finalHeight * scale));

        int pw = (int)Math.Round((rotation is 90 or 270 ? height : width) * scale);
        int ph = (int)Math.Round((rotation is 90 or 270 ? width : height) * scale);
        return new PageGeometry(m, pw, ph);
    }
```
(`PdfPage` already uses `System.Numerics`? add `using System.Numerics;` if the build complains. `PageGeometry` is in the same `PdfLibrary.Document` namespace, so no extra using needed.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PageGeometryTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Document/PageGeometry.cs PdfLibrary/Document/PdfPage.cs PdfLibrary.Tests/Document/PageGeometryTests.cs
git commit -m "feat(forms): PageGeometry + PdfPage.GetGeometry — PDF<->image transform for UI overlay"
```

---

## Task 2: `PdfFieldWidget` + public `PdfFormField.Widgets`

**Files:**
- Create: `PdfLibrary/Editing/Forms/PdfFieldWidget.cs`
- Modify: `PdfLibrary/Editing/Forms/PdfFormField.cs`, `FormFieldTree.cs`, `FormFlattener.cs`
- Test: `PdfLibrary.Tests/Editing/Forms/FieldWidgetTests.cs`

**Background:** Promote the internal widget-dict list to a public `PdfFieldWidget` projection. Each field can have multiple widgets (radio group; same field on >1 page). `Rect` from the widget `/Rect` array (min/max of the 4 numbers → `PdfRect`); `PageIndex` by finding which page's `/Annots` contains the widget; `OnStateName` = the first non-`Off` key of `/AP /N` (checkbox/radio), else null. The page-index scan is done **once** per document (a widget-dict→index map) to stay O(annots+widgets).

**Interfaces:**
- Consumes: internal `PdfFormField.WidgetDicts` (renamed), `PdfDocument.GetPages()`, `PdfRect`, the existing `/AP /N` enumeration (cf. `FormFieldTree.GetWidgetOnStateNames`).
- Produces:
  - `public sealed class PdfFieldWidget` with `int PageIndex`, `PdfRect Rect`, `string? OnStateName`, `PdfFormField Field`.
  - `public IReadOnlyList<PdfFieldWidget> PdfFormField.Widgets` (replaces the internal dict list of the same name; raw dicts move to internal `WidgetDicts`).

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Editing/Forms/FieldWidgetTests.cs`. Build a form doc with a text field and a checkbox at known rects, then assert the public widget geometry. **Reuse the existing form-doc helper** — read `PdfLibrary.Tests/Editing/Forms/FormTestDocs.cs` and `FormReadTests.cs` for how they build a form `PdfDocument` (a `PdfDocumentEditor` with fields); use that helper to place a text field at a known rect and a checkbox with an on-state. Sketch (adjust to the real `FormTestDocs`/editor API):

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;

namespace PdfLibrary.Tests.Editing.Forms;

public class FieldWidgetTests
{
    [Fact]
    public void Widgets_ExposeRectPageAndOnState()
    {
        // Build (or load) a single-page form with a text field at a known rect and a checkbox.
        // Use FormTestDocs (see FormReadTests for the pattern).
        using PdfDocumentEditor editor = FormTestDocs.SingleTextAndCheckbox(
            textRect: new PdfRect(100, 700, 300, 720),
            checkRect: new PdfRect(100, 660, 116, 676),
            checkOnState: "Yes");

        PdfFormField text = editor.Forms["text1"];
        PdfFieldWidget tw = Assert.Single(text.Widgets);
        Assert.Equal(0, tw.PageIndex);
        Assert.Equal(100, tw.Rect.Left, 1);
        Assert.Equal(700, tw.Rect.Bottom, 1);
        Assert.Equal(300, tw.Rect.Right, 1);
        Assert.Equal(720, tw.Rect.Top, 1);
        Assert.Null(tw.OnStateName);          // text field: no on-state
        Assert.Same(text, tw.Field);

        PdfFormField check = editor.Forms["check1"];
        PdfFieldWidget cw = Assert.Single(check.Widgets);
        Assert.Equal("Yes", cw.OnStateName);  // checkbox on-state
    }
}
```

> If `FormTestDocs` has no helper that places fields at specific rects with an on-state, add a minimal one there (it is a test helper), or build the form via `PdfDocumentBuilder` + `PdfAcroFormBuilder`/`PdfTextFieldBuilder` (see `PdfLibrary/Builder/PdfAcroFormBuilder.cs`). The assertions target the public widget geometry, not the builder call.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FieldWidgetTests"`
Expected: FAIL — `PdfFieldWidget` and the public `Widgets` don't exist.

- [ ] **Step 3: Implement**

Create `PdfLibrary/Editing/Forms/PdfFieldWidget.cs`:

```csharp
using PdfLibrary.Builder;

namespace PdfLibrary.Editing.Forms;

/// <summary>
/// One visual placement (widget annotation) of a form field: where it sits and on which page.
/// A field may have several (e.g. a radio group, or the same field repeated across pages).
/// </summary>
public sealed class PdfFieldWidget
{
    /// <summary>0-based index of the page this widget annotation appears on.</summary>
    public int PageIndex { get; }
    /// <summary>Widget rectangle in PDF user space (Y-up). Map to pixels via PageGeometry.</summary>
    public PdfRect Rect { get; }
    /// <summary>The "on" appearance state for checkbox/radio widgets (/AP /N key); null otherwise.</summary>
    public string? OnStateName { get; }
    /// <summary>The field this widget belongs to.</summary>
    public PdfFormField Field { get; }

    internal PdfFieldWidget(PdfFormField field, int pageIndex, PdfRect rect, string? onStateName)
    {
        Field = field;
        PageIndex = pageIndex;
        Rect = rect;
        OnStateName = onStateName;
    }
}
```

In `PdfFormField.cs`: rename the internal list and add the public projection (set by `FormFieldTree.Read`):

```csharp
    /// <summary>Raw widget annotation dictionaries (the visual representations).</summary>
    internal IReadOnlyList<PdfDictionary> WidgetDicts { get; set; } = Array.Empty<PdfDictionary>();

    /// <summary>The field's widget placements (rect + page + on-state), for positioning UI over fields.</summary>
    public IReadOnlyList<PdfFieldWidget> Widgets { get; internal set; } = Array.Empty<PdfFieldWidget>();
```

In `FormFlattener.cs`: change `foreach (PdfDictionary widget in field.Widgets)` → `field.WidgetDicts` (line ~28). (It needs the raw dicts.)

In `FormFieldTree.cs`: where it currently sets `field.Widgets = <dicts>`, set `field.WidgetDicts = <dicts>` instead, and after the field list is built, populate the public projection. Add an internal builder that scans pages once:

```csharp
    // Build PdfFieldWidget projections for every field. Page index is resolved by a single
    // scan of all pages' /Annots (widget dict identity), so this is O(annots + widgets).
    internal static void PopulateWidgets(PdfDocument doc, List<PdfFormField> fields)
    {
        var pageOf = new Dictionary<PdfDictionary, int>();
        List<PdfPage> pages = doc.GetPages();
        for (var pi = 0; pi < pages.Count; pi++)
        {
            PdfObject? annotsRaw = pages[pi].Dictionary.Get(new PdfName("Annots"));
            if (Resolve(doc, annotsRaw) is not PdfArray annots) continue;
            foreach (PdfObject entry in annots)
                if (ResolveToDict(doc, entry) is { } wd) pageOf[wd] = pi;
        }

        foreach (PdfFormField field in fields)
        {
            var widgets = new List<PdfFieldWidget>(field.WidgetDicts.Count);
            foreach (PdfDictionary wd in field.WidgetDicts)
            {
                int pageIndex = pageOf.TryGetValue(wd, out int pi) ? pi : -1;
                PdfRect rect = ReadRect(doc, wd);
                string? onState = GetWidgetOnStateNames(doc, wd).FirstOrDefault(s => s != "Off");
                widgets.Add(new PdfFieldWidget(field, pageIndex, rect, onState));
            }
            field.Widgets = widgets;
        }
    }

    private static PdfRect ReadRect(PdfDocument doc, PdfDictionary widget)
    {
        if (Resolve(doc, widget.Get(new PdfName("Rect"))) is PdfArray a && a.Count >= 4)
        {
            double v0 = ToDouble(a[0]), v1 = ToDouble(a[1]), v2 = ToDouble(a[2]), v3 = ToDouble(a[3]);
            return new PdfRect(Math.Min(v0, v2), Math.Min(v1, v3), Math.Max(v0, v2), Math.Max(v1, v3));
        }
        return new PdfRect(0, 0, 0, 0);
    }
```

Call `PopulateWidgets(doc, fields)` at the end of `FormFieldTree.Read` before returning. Reuse the existing `Resolve`/`ToDouble`/`GetWidgetOnStateNames` helpers in `FormFieldTree` (make `GetWidgetOnStateNames` non-private only if needed — it's already in this class). Add a small `ResolveToDict(doc, entry)` helper (resolve an indirect ref or direct dict to `PdfDictionary?`) if one doesn't already exist; `PdfPage.Dictionary` is internal and accessible here.

> Verify-as-you-go: confirm `FormFieldTree.Read` returns `List<PdfFormField>` and the exact spot it currently assigns the widget dicts; confirm `Resolve`/`ToDouble` signatures in `FormFieldTree` (they exist in `FormFlattener`; if not present in `FormFieldTree`, copy the tiny helpers or make them shared internal). `PdfPage.Dictionary` is internal — accessible from this namespace.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FieldWidgetTests"`
Expected: PASS. Also run the existing forms tests to confirm the rename didn't break flattening: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PdfLibrary.Tests.Editing.Forms"`.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Editing/Forms/ PdfLibrary.Tests/Editing/Forms/FieldWidgetTests.cs
git commit -m "feat(forms): public PdfFieldWidget + PdfFormField.Widgets (rect + page index + on-state)"
```

---

## Task 3: Headless fill + geometry round-trip test

**Files:**
- Test: `PdfLibrary.Tests/Editing/Forms/FormGeometryRoundTripTests.cs`

**Background:** An end-to-end integration test of the flow the viewer (D3) will drive: enumerate fields → read widget geometry + `PageGeometry` (D1) → set values (existing public fill API) → flatten → save → reload → assert. No new production code — this validates D1 composes with the existing fill/flatten/save pipeline.

**Interfaces:**
- Consumes: `PdfFormField.Widgets`, `PageGeometry`/`GetGeometry` (D1); `PdfTextField.Value`, `PdfButtonField.Check()`, `Forms.Flatten()`, `editor.Save()` (existing).

- [ ] **Step 1: Write the test**

Create `PdfLibrary.Tests/Editing/Forms/FormGeometryRoundTripTests.cs`:

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing.Forms;

public class FormGeometryRoundTripTests
{
    [Fact]
    public void EnumerateGeometry_Fill_Flatten_Save_Reload()
    {
        using PdfDocumentEditor editor = FormTestDocs.SingleTextAndCheckbox(
            textRect: new PdfRect(100, 700, 300, 720),
            checkRect: new PdfRect(100, 660, 116, 676),
            checkOnState: "Yes");

        // 1) Geometry the viewer would use: widget rect → image pixels.
        PdfFieldWidget tw = editor.Forms["text1"].Widgets[0];
        PdfPage page = editor.GetDocument().GetPage(tw.PageIndex)!; // see note on the doc accessor
        PageGeometry geo = page.GetGeometry(1.0);
        PdfRect pixelRect = geo.MapRectToImage(tw.Rect);
        Assert.True(pixelRect.Width > 0 && pixelRect.Height > 0); // a real on-screen region

        // 2) Fill via the existing public API.
        ((PdfTextField)editor.Forms["text1"]).Value = "Hello";
        ((PdfButtonField)editor.Forms["check1"]).Check();

        // 3) Flatten + save.
        editor.Forms.Flatten();
        byte[] outBytes;
        using (var ms = new MemoryStream()) { editor.Save(ms); outBytes = ms.ToArray(); }

        // 4) Reload: AcroForm gone (flattened), content present.
        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(outBytes));
        Assert.True(reloaded.PageCount >= 1);
        // (Optional: assert /AcroForm removed or /Fields empty via the editor on `reloaded`.)
    }
}
```

> Verify-as-you-go: the exact `FormTestDocs` helper name/signature, how to get the `PdfDocument` from a `PdfDocumentEditor` (e.g. an internal/public accessor — check `PdfDocumentEditor`; if none is public, get the page via the editor's page API, or load the saved bytes for the geometry step), and the `editor.Save(Stream)` overload. Keep the assertions (geometry yields a positive pixel rect; fill+flatten+save+reload succeeds); adapt the plumbing to the real editor API.

- [ ] **Step 2: Run + iterate to green**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~FormGeometryRoundTripTests"`
Expected: PASS (after adapting plumbing). If `FormTestDocs` can't express on-states/rects, extend it minimally.

- [ ] **Step 3: Commit**

```bash
git add PdfLibrary.Tests/Editing/Forms/FormGeometryRoundTripTests.cs
git commit -m "test(forms): headless geometry+fill+flatten+save round-trip"
```

---

## Task 4: Verification

**Files:** none.

- [ ] **Step 1: Full suite + Skia-free core + Release**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly" --nologo` (PASS); `grep -rln "using SkiaSharp\|SkiaSharp\." PdfLibrary --include=*.cs | grep -v /obj/` (no output); `dotnet build PdfLibrary/PdfLibrary.csproj -c Release --nologo` (0W/0E).

- [ ] **Step 2: No commit.**

## Self-Review Notes

- **Spec coverage:** delivers Component 1 (forms geometry) — `PageGeometry`/`GetGeometry`, public `PdfFieldWidget`/`Widgets`. The geometry matrix matches the renderers' initial transform exactly (the shared coordinate contract). Independent of D2/D3.
- **No behavior change to filling/flattening** — only an internal rename (`Widgets`→`WidgetDicts`) with call sites updated; the public `Widgets` is additive.
- **Out of scope → D2** (WPF render target + `PdfImageToRgba`), **D3** (viewer rewire + form-control overlay). Refactoring the renderers to consume `PageGeometry` is deferred (additive now).
