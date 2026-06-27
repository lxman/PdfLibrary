# Plan D3 — Pure-WPF Viewer Rewire + Fillable Forms

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rewire `PdfLibrary.Wpf.Viewer` onto the SkiaSharp-free `WpfRenderTarget` (drop all Skia from the viewer), overlay native WPF controls over form fields so users can fill and save them, and drop `Generator`'s Skia dependency. This is the payoff slice — the pure-WPF fillable viewer.

**Architecture:** Replace the `SkiaRenderer`/`SKElement` display with a control that hosts the `DrawingGroup` from `page.RenderToDrawing(scale)` (vector, via an `Image`+`DrawingImage`) plus a transparent `Canvas` overlay for form controls. Load via `PdfDocumentEditor.Open` so `Forms`/`Pages`/`Save` are available. Export/print re-platform to `RenderTargetBitmap` rendered from the `DrawingGroup`. The overlay rebuilds after every render (zoom/navigation), positioning controls via `PageGeometry.MapRectToImage`.

**Tech Stack:** C# 12, .NET 10-windows, WPF, `PdfLibrary.Rendering.Wpf` (D2b), `PdfLibrary` forms API (D1).

## Global Constraints

- After D3, `PdfLibrary.Wpf.Viewer` has **zero SkiaSharp** (no `PdfLibrary.Rendering.SkiaSharp` ref, no `SkiaSharp.Views.WPF`, no `using SkiaSharp`) and no dead `OpenTK`/`OpenTK.GLWpfControl` packages. `Generator` has zero SkiaSharp.
- The viewer references `PdfLibrary.Rendering.Wpf` and renders via `page.RenderToDrawing(scale)` / `WpfRenderTarget`.
- Skia stays elsewhere (the shipped `PdfLibrary.Rendering.SkiaSharp` package, the render-test gate, `PdfLibrary.Integration`) — untouched.
- Coordinate split: `page.GetGeometry(pixelScale).MapRectToImage(widget.Rect) → ImageRect` (pixels); convert to DIUs by `÷ VisualTreeHelper.GetDpi(this).DpiScaleX/Y`. Guard `widget.PageIndex == -1` (orphan).
- **GUI verification is manual** (per the TDD skill's GUI exception): each task gate is a clean build (0W/0E) + the per-task manual check; the final task is a full manual verification checklist. Extractable non-UI logic (e.g. a field→control factory) gets unit tests where practical.

## File Structure

- `PdfLibrary.Wpf.Viewer/PdfLibrary.Wpf.Viewer.csproj` (modify) — swap refs, drop dead packages.
- `PdfLibrary.Wpf.Viewer/SkiaRenderer.xaml` + `.cs` (replace) → `WpfPageView.xaml` + `.cs` (new control: page host + overlay).
- `PdfLibrary.Wpf.Viewer/MainWindow.xaml.cs` (modify) — load via editor, render via RenderToDrawing, re-platform export/print, build overlay, Save/Flatten.
- `PdfLibrary.Wpf.Viewer/MainWindow.xaml` (modify) — replace the `SkiaRenderer` element; add Save/Flatten toolbar buttons.
- `PdfLibrary.Examples/00-Generator/GenerateLogo.cs` (delete) + a static logo asset; `Generator.csproj` (modify) — drop Skia.

---

## Task 1: WPF page host + render-backend swap (display path)

**Files:**
- Create: `PdfLibrary.Wpf.Viewer/WpfPageView.xaml` + `WpfPageView.xaml.cs`
- Modify: `MainWindow.xaml` (use the new control), `MainWindow.xaml.cs` (load via editor, render via RenderToDrawing), `PdfLibrary.Wpf.Viewer.csproj` (add `PdfLibrary.Rendering.Wpf` ref — keep the Skia ref for now; Task 2 removes it)
- Delete: `SkiaRenderer.xaml` + `.cs`

**Background:** Swap the page DISPLAY from Skia to WPF vector. Load the document via `PdfDocumentEditor.Open` (gives `Pages`, `Forms`, `Save`). Render the current page with `page.RenderToDrawing(pixelScale)` → a `DrawingGroup` hosted in an `Image` via `DrawingImage`. The control also carries a transparent `Canvas` overlay (populated in Task 3). Export/print still use Skia in this task (re-platformed in Task 2) — so the Skia ref stays until Task 2.

**Interfaces:**
- Consumes: `PdfDocumentEditor.Open(path)→PdfDocumentEditor`; `editor.Pages[i]→PdfPage`; `PdfLibrary.Rendering.Wpf.WpfPageExtensions.RenderToDrawing(this PdfPage, double scale)→DrawingGroup`.
- Produces: `WpfPageView` control with `void ShowPage(DrawingGroup pageDrawing, int pixelWidth, int pixelHeight, double dpiScale)` and a public `Canvas Overlay { get; }`.

- [ ] **Step 1: Read the current code**

Read `MainWindow.xaml.cs` (esp. `LoadPdfDocument` ~117, `RenderPage` ~166–204, `SetZoomAsync`, the export `SaveImageToFile` ~352, print `RenderPageToBitmap` ~417), `SkiaRenderer.xaml(.cs)`, `MainWindow.xaml` (the `ScrollViewer`/`SkiaRenderer` layout), and `.superpowers/sdd/d3-map.md` §2 + §7 for the exact call sites.

- [ ] **Step 2: Create `WpfPageView`**

`WpfPageView.xaml` — a `Grid` containing an `Image` (the page) and a `Canvas` (the overlay) stacked:
```xml
<UserControl x:Class="PdfLibrary.Wpf.Viewer.WpfPageView" ...>
  <Grid x:Name="Root" Background="White" HorizontalAlignment="Left" VerticalAlignment="Top">
    <Image x:Name="PageImage" Stretch="Fill" SnapsToDevicePixels="True"/>
    <Canvas x:Name="OverlayCanvas" Background="Transparent"/>
  </Grid>
</UserControl>
```
`WpfPageView.xaml.cs`:
```csharp
public partial class WpfPageView : UserControl
{
    public WpfPageView() => InitializeComponent();
    public Canvas Overlay => OverlayCanvas;

    /// <summary>Display a rendered page. pixelWidth/Height are the DrawingGroup's pixel size;
    /// dpiScale converts pixels→DIUs so the control sizes correctly on high-DPI displays.</summary>
    public void ShowPage(DrawingGroup pageDrawing, int pixelWidth, int pixelHeight, double dpiScale)
    {
        var img = new DrawingImage(pageDrawing);
        img.Freeze();
        PageImage.Source = img;
        double diuW = pixelWidth / dpiScale, diuH = pixelHeight / dpiScale;
        Root.Width = diuW; Root.Height = diuH;
        PageImage.Width = diuW; PageImage.Height = diuH;
        OverlayCanvas.Width = diuW; OverlayCanvas.Height = diuH;
        OverlayCanvas.Children.Clear();   // overlay rebuilt by MainWindow (Task 3)
    }
}
```

- [ ] **Step 3: Rewire `MainWindow`**

- In `MainWindow.xaml`, replace `<local:SkiaRenderer x:Name="PdfRenderer" .../>` with `<local:WpfPageView x:Name="PdfView" .../>` inside the `ScrollViewer`.
- In `MainWindow.xaml.cs`:
  - Add a field `private PdfDocumentEditor? _editor;` (keep `_pdfDoc` only if still needed; prefer the editor).
  - `LoadPdfDocument`: replace `_pdfDoc = PdfDocument.Load(...)` with `_editor = PdfDocumentEditor.Open(filePath);` (and set page count from `_editor.Pages.Count`). Remove the `PdfRenderer.SetDocument` call (no longer needed).
  - `RenderPage()`: replace the Skia block (`GetOrCreateRenderTarget` + `page.Render` + `FinalizeRendering`) with:
    ```csharp
    PdfPage page = _editor!.Pages[_currentPage - 1];
    double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
    double pixelScale = _zoomLevel * DiusPerPdfPoint * dpiScale;
    DrawingGroup dg = page.RenderToDrawing(pixelScale);
    dg.Freeze();
    PageGeometry geo = page.GetGeometry(pixelScale);
    PdfView.ShowPage(dg, geo.PixelWidth, geo.PixelHeight, dpiScale);
    BuildOverlay(page, geo, dpiScale);   // implemented in Task 3 — add an empty stub now
    ```
  - Add an empty `private void BuildOverlay(PdfPage page, PageGeometry geo, double dpiScale) { }` placeholder (Task 3 fills it).
  - Add `using PdfLibrary.Rendering.Wpf;`, `using PdfLibrary.Editing;`, `using PdfLibrary.Document;` as needed. (Leave the `using SkiaSharp;`/`PdfLibrary.Rendering.SkiaSharp` imports for now — export/print still use them; Task 2 removes them.)
- Add the `PdfLibrary.Rendering.Wpf` ProjectReference to the csproj (keep the Skia ref for now).

> **Verify-as-you-go:** `DiusPerPdfPoint` constant + `_zoomLevel`/`_currentPage` names; how `RenderPage` is invoked (Dispatcher) — keep the same threading. `PdfDocumentEditor.Open` signature + `Pages.Count`. If `RenderToDrawing` runs on the UI thread (STA), no extra freeze/marshal is needed beyond `dg.Freeze()`.

- [ ] **Step 4: Build + manual check**

Build: `dotnet build PdfLibrary.Wpf.Viewer/PdfLibrary.Wpf.Viewer.csproj -c Debug --nologo` (0 errors). Run: `dotnet run --project PdfLibrary.Wpf.Viewer -- <some.pdf>` — a page renders (vector), Prev/Next + zoom work. (Manual; GUI.)

- [ ] **Step 5: Commit**
```bash
git add PdfLibrary.Wpf.Viewer/
git commit -m "feat(viewer): render pages via WpfRenderTarget DrawingGroup (drop SKElement display)"
```

---

## Task 2: Re-platform export + print; drop all Skia + dead OpenTK from the viewer

**Files:** Modify `MainWindow.xaml.cs` (export `SaveImageToFile` ~352, print `RenderPageToBitmap` ~417), `PdfLibrary.Wpf.Viewer.csproj`

**Background:** Export and print are the last Skia users in the viewer. Re-platform both to render the `DrawingGroup` into a WPF `RenderTargetBitmap` → `PngBitmapEncoder`. Then remove the Skia ProjectReference + `SkiaSharp.Views.WPF` + the dead `OpenTK`/`OpenTK.GLWpfControl` packages, and the `using SkiaSharp;` imports.

**Interfaces:**
- Consumes: `page.RenderToDrawing(scale)→DrawingGroup`, `page.GetGeometry(scale)→PageGeometry` (PixelWidth/Height).
- Produces: a shared private helper `static RenderTargetBitmap RenderPageBitmap(PdfPage page, double pixelScale)` used by both export and print.

- [ ] **Step 1: Add the bitmap helper + re-platform export/print**

```csharp
private static RenderTargetBitmap RenderPageBitmap(PdfPage page, double pixelScale)
{
    DrawingGroup dg = page.RenderToDrawing(pixelScale);
    PageGeometry geo = page.GetGeometry(pixelScale);
    var visual = new DrawingVisual();
    using (DrawingContext dc = visual.RenderOpen())
        dc.DrawDrawing(dg);
    var rtb = new RenderTargetBitmap(geo.PixelWidth, geo.PixelHeight, 96, 96, PixelFormats.Pbgra32);
    rtb.Render(visual);
    rtb.Freeze();
    return rtb;
}
```
- `SaveImageToFile` (export): replace the `SkiaSharpRenderTarget` block with `RenderTargetBitmap rtb = RenderPageBitmap(page, pixelScale);` then a `PngBitmapEncoder` → file stream:
  ```csharp
  var enc = new PngBitmapEncoder();
  enc.Frames.Add(BitmapFrame.Create(rtb));
  using FileStream fs = File.Create(outputPath);
  enc.Save(fs);
  ```
- `RenderPageToBitmap` (print): return the `RenderTargetBitmap` from `RenderPageBitmap(page, printScale)` directly (it IS a `BitmapSource` the `PrintDialog` can use) — drop the `SKImage.Encode(Png)`→`MemoryStream`→`BitmapImage` round-trip.

> Verify-as-you-go: the print/export scale (printers may use a different DPI than the screen — `DrawingGroup` is resolution-independent, so render at the print target's pixel size); the existing method signatures + how the printed/exported bitmap is consumed.

- [ ] **Step 2: Strip Skia from the viewer**

- `MainWindow.xaml.cs`: remove `using SkiaSharp;` and `using PdfLibrary.Rendering.SkiaSharp;` (no remaining references after Step 1).
- `PdfLibrary.Wpf.Viewer.csproj`: remove `<ProjectReference ...PdfLibrary.Rendering.SkiaSharp...>`, `<PackageReference Include="SkiaSharp.Views.WPF" .../>`, `<PackageReference Include="OpenTK" .../>`, `<PackageReference Include="OpenTK.GLWpfControl" .../>`.

- [ ] **Step 3: Build + Skia-free check + manual**

Build (0W/0E). Verify: `grep -rln "SkiaSharp\|OpenTK\|SKElement\|SKImage" PdfLibrary.Wpf.Viewer --include=*.cs --include=*.xaml --include=*.csproj | grep -v /obj/` → no output (or only the `WpfPageView` doc-comment if any). Run the viewer; export a page to PNG and print-preview — both produce correct output (manual).

- [ ] **Step 4: Commit**
```bash
git add PdfLibrary.Wpf.Viewer/
git commit -m "feat(viewer): re-platform export/print to RenderTargetBitmap; drop all SkiaSharp + dead OpenTK"
```

---

## Task 3: Form-control overlay + write-back

**Files:** Modify `MainWindow.xaml.cs` (implement `BuildOverlay` + a control factory)

**Background:** For each form-field widget on the current page, place a native WPF control over its rect (via `PageGeometry.MapRectToImage` → DIUs) on the `PdfView.Overlay` canvas, initialized from the field's value, with edits written back through the field setters (which regenerate the appearance). Rebuilt every render (already called from `RenderPage`).

**Interfaces:**
- Consumes: `_editor.Forms` (`IReadOnlyCollection<PdfFormField>`); `field.Widgets` (`PdfFieldWidget{PageIndex,Rect,OnStateName,Field}`); `PageGeometry.MapRectToImage`; field types `PdfTextField`(`Value`,`IsMultiline`,`MaxLength`), `PdfButtonField`(`Kind`,`IsChecked`,`Check()`,`Uncheck()`,`Options`,`SelectedOption`), `PdfChoiceField`(`Options`(Export,Display),`IsCombo`,`IsMultiSelect`,`SelectedValues`), `PdfSignatureField`.

- [ ] **Step 1: Implement `BuildOverlay`**

```csharp
private void BuildOverlay(PdfPage page, PageGeometry geo, double dpiScale)
{
    Canvas overlay = PdfView.Overlay;
    overlay.Children.Clear();
    if (_editor is null) return;
    int pageIndex = _currentPage - 1;

    foreach (PdfFormField field in _editor.Forms)
    foreach (PdfFieldWidget widget in field.Widgets)
    {
        if (widget.PageIndex != pageIndex) continue;       // includes -1 orphans → skipped
        ImageRect ir = geo.MapRectToImage(widget.Rect);
        FrameworkElement? control = CreateControl(field, widget);
        if (control is null) continue;
        Canvas.SetLeft(control, ir.X / dpiScale);
        Canvas.SetTop(control, ir.Y / dpiScale);
        control.Width = ir.Width / dpiScale;
        control.Height = ir.Height / dpiScale;
        overlay.Children.Add(control);
    }
}

private FrameworkElement? CreateControl(PdfFormField field, PdfFieldWidget widget)
{
    switch (field)
    {
        case PdfTextField tf:
        {
            var tb = new TextBox { Text = tf.Value ?? "", AcceptsReturn = tf.IsMultiline,
                VerticalContentAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1) };
            if (tf.MaxLength is { } ml and > 0) tb.MaxLength = ml;
            tb.LostFocus += (_, _) => tf.Value = tb.Text;     // write-back regenerates appearance
            return tb;
        }
        case PdfButtonField bf when bf.Kind == ButtonKind.Checkbox:
        {
            var cb = new CheckBox { IsChecked = bf.IsChecked, VerticalAlignment = VerticalAlignment.Center };
            cb.Checked += (_, _) => bf.Check();
            cb.Unchecked += (_, _) => bf.Uncheck();
            return cb;
        }
        case PdfButtonField bf when bf.Kind == ButtonKind.Radio:
        {
            var rb = new RadioButton { GroupName = field.FullName, VerticalAlignment = VerticalAlignment.Center,
                IsChecked = widget.OnStateName != null && widget.OnStateName == bf.SelectedOption };
            string? on = widget.OnStateName;
            rb.Checked += (_, _) => { if (on != null) bf.SelectedOption = on; };
            return rb;
        }
        case PdfChoiceField cf when cf.IsCombo:
        {
            var combo = new ComboBox();
            foreach ((string export, string display) in cf.Options) combo.Items.Add(new ComboBoxItem { Content = display, Tag = export });
            // preselect
            string? sel = cf.SelectedValues.Count > 0 ? cf.SelectedValues[0] : null;
            if (sel != null) combo.SelectedIndex = IndexOfExport(cf, sel);
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem item && item.Tag is string ex)
                    cf.SelectedValues = new[] { ex };
            };
            return combo;
        }
        case PdfChoiceField cf:   // list box
        {
            var lb = new ListBox { SelectionMode = cf.IsMultiSelect ? SelectionMode.Multiple : SelectionMode.Single };
            foreach ((string export, string display) in cf.Options) lb.Items.Add(new ListBoxItem { Content = display, Tag = export });
            lb.SelectionChanged += (_, _) =>
                cf.SelectedValues = lb.SelectedItems.Cast<ListBoxItem>().Select(i => (string)i.Tag).ToArray();
            return lb;
        }
        case PdfSignatureField:
            return new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)),
                Child = new TextBlock { Text = "Signature", Foreground = Brushes.Gray,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        default:
            return null;   // push buttons, unknown
    }
}

private static int IndexOfExport(PdfChoiceField cf, string export)
{
    for (int i = 0; i < cf.Options.Count; i++) if (cf.Options[i].Export == export) return i;
    return -1;
}
```

> Verify-as-you-go: the EXACT field-type member names + `ButtonKind` enum values + `PdfChoiceField.Options` tuple element names (`Export`/`Display`) — read `PdfLibrary/Editing/Forms/PdfFormField.cs`. Adjust the switch to the real API. If a writeback setter differs (e.g. `SelectedValues` type), match it. Add `using System.Windows.Controls;`, `using System.Windows.Media;`, `using System.Linq;`.

- [ ] **Step 2: Build + manual check**

Build (0W/0E). Run on a form PDF (e.g. one with text fields + checkboxes): controls appear over fields, positioned correctly at multiple zoom levels, prefilled from values; typing/checking updates the in-memory field (verify by re-rendering — the baked appearance under the control updates, or by Save in Task 4).

- [ ] **Step 3: Commit**
```bash
git add PdfLibrary.Wpf.Viewer/
git commit -m "feat(viewer): native form-control overlay over fields with write-back"
```

---

## Task 4: Save / Flatten commands

**Files:** Modify `MainWindow.xaml` (toolbar buttons), `MainWindow.xaml.cs` (handlers)

**Background:** Persist edits. Add a Save button (`_editor.Save(path)` via a SaveFileDialog) and a Flatten button (`_editor.Forms.Flatten()` then re-render so the now-baked fields show without controls).

- [ ] **Step 1: Add buttons + handlers**

In `MainWindow.xaml`, add to the toolbar: `<Button Content="Save" Click="SaveButton_Click"/>` and `<Button Content="Flatten" Click="FlattenButton_Click"/>` (match the existing toolbar button style). Handlers:
```csharp
private void SaveButton_Click(object sender, RoutedEventArgs e)
{
    if (_editor is null) return;
    var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = "filled.pdf" };
    if (dlg.ShowDialog() != true) return;
    _editor.Save(dlg.FileName);
}

private void FlattenButton_Click(object sender, RoutedEventArgs e)
{
    if (_editor is null) return;
    _editor.Forms.Flatten();
    RenderPage();   // re-render: fields now baked; rebuild overlay (now empty)
}
```

> Verify-as-you-go: `_editor.Save` / `_editor.Forms.Flatten()` exact signatures; how the existing toolbar/buttons are styled + the `RenderPage`/`RenderPageAsync` entry point name to call after flatten. After Flatten, `_editor.Forms` is empty so the overlay rebuilds empty — confirm.

- [ ] **Step 2: Build + manual check** (0W/0E). Fill a form → Save → reopen the saved file in the viewer (or another reader) → values persisted. Flatten → controls disappear, baked values show.

- [ ] **Step 3: Commit**
```bash
git add PdfLibrary.Wpf.Viewer/
git commit -m "feat(viewer): Save and Flatten form commands"
```

---

## Task 5: De-Skia Generator

**Files:** Delete `PdfLibrary.Examples/00-Generator/GenerateLogo.cs`; add a static logo asset; modify `Generator.csproj`; update any caller.

**Background:** `Generator`'s only Skia use is `GenerateLogo.cs` (draws a "PdfLibrary" logo JPEG via `SKCanvas`). Replace with a pre-generated static asset and remove the `SkiaSharp` PackageReference. (Generator targets plain `net10.0` — no `System.Drawing`; the static-asset path is cleanest.)

- [ ] **Step 1: Capture the logo, then de-Skia**

- Run the existing `GenerateLogo` once (or build the bytes) to produce the logo JPEG; save it as `PdfLibrary.Examples/00-Generator/assets/logo.jpg` and mark it `<Content CopyToOutputDirectory="PreserveNewest">` (or `<EmbeddedResource>`).
- Replace the logo-generation call site to read the static asset bytes instead of generating them (find `GenerateLogo`'s caller — likely a sample that embeds the logo in a generated PDF). If the logo is only produced for its own sake (no downstream consumer), simply delete the generation step.
- Delete `GenerateLogo.cs`.
- `Generator.csproj`: remove `<PackageReference Include="SkiaSharp" .../>`.

> Verify-as-you-go: where `GenerateLogo` is called (grep `GenerateLogo`); whether the logo feeds a generated example PDF (then load the static asset there) or is standalone (then drop it). Keep the example's output equivalent.

- [ ] **Step 2: Build + Skia-free check**

Build Generator (0W/0E). `grep -rln "SkiaSharp" PdfLibrary.Examples/00-Generator --include=*.cs --include=*.csproj | grep -v /obj/` → no output. Run the generator if it has a runnable entry; confirm it still produces its example output.

- [ ] **Step 3: Commit**
```bash
git add PdfLibrary.Examples/00-Generator/ 
git commit -m "chore(generator): drop SkiaSharp — logo from static asset"
```

---

## Task 6: Verification + manual checklist

**Files:** none (verification).

- [ ] **Step 1: Build + Skia-audit + suite**

- `dotnet build PdfLibrary.Wpf.Viewer/PdfLibrary.Wpf.Viewer.csproj -c Release --nologo` (0W/0E); `dotnet build Generator (the csproj) -c Release` (0W/0E).
- Viewer + Generator Skia-free: `grep -rln "SkiaSharp" PdfLibrary.Wpf.Viewer PdfLibrary.Examples/00-Generator --include=*.cs --include=*.xaml --include=*.csproj | grep -v /obj/` → no output.
- Skia still present where intended (unchanged): `PdfLibrary.Rendering.SkiaSharp`, the render tests, `PdfLibrary.Integration` still build/test.
- Full suite: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly" --nologo` + `dotnet test PdfLibrary.Rendering.Wpf.Tests/...` (PASS).

- [ ] **Step 2: Manual verification checklist (the user drives the GUI)**

Hand the user this checklist (the GUI can't be auto-verified):
1. `dotnet run --project PdfLibrary.Wpf.Viewer -- <a normal PDF>` — pages render (vector, crisp), Prev/Next + zoom work.
2. Open the figure-label PDFs that exercised earlier bugs (e.g. main.pdf pages 5/8) — labels/images positioned correctly; lines not too thick.
3. Open a **form** PDF — text/checkbox/radio/combo controls appear over fields, correctly placed at 100% and zoomed; prefilled.
4. Fill a text field, toggle a checkbox/radio, pick a combo option → Save → reopen the saved file (here or in another reader) — values persisted.
5. Flatten → controls vanish, values baked into the page.
6. Export a page to PNG and print-preview — correct output.

- [ ] **Step 3: No commit.**

## Self-Review Notes

- **Spec coverage:** delivers Component 3 — the pure-WPF fillable viewer. After D3 the viewer is SkiaSharp-free (vector rendering via D2b), forms are fillable via native controls (D1 geometry) and saveable; Generator is de-Skia'd. Skia remains the cross-platform raster backend + test gate (untouched), per the approved scope.
- **TDD exception:** GUI wiring is verified by build-clean gates + the manual checklist (the TDD skill's documented GUI exception); non-UI logic (control factory, geometry) leans on D1's tested `PageGeometry`.
- **Coordinate split** honored: library `PageGeometry` (PDF→pixels) + viewer DPI conversion (pixels→DIUs); overlay rebuilt per render since zoom re-renders.

## Out of scope / deferred (2.0 follow-ons)

- True instant-zoom via a `ScaleTransform` on the vector visual (vs. the current re-render-on-zoom) — a perf nicety; the current model already yields crisp vector output.
- Scrolling multi-page view (viewer is single-page by design today).
- Field validation / JS actions / digital signing.
