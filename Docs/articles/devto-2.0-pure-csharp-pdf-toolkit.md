---
title: "A Pure-C# PDF Toolkit for .NET: Read, Create, Edit, Optimize, Render, Fill Forms — and No Native Dependencies"
tags: dotnet, csharp, pdf, opensource
cover_image: <upload the 1000x420 cover here>
---

Most PDF libraries in .NET are wrappers. PDFium, MuPDF, Ghostscript — solid engines, but they ship native binaries you P/Invoke into. That buys you per-RID packages, Native AOT friction, the "works on my box, throws `DllNotFoundException` in the Alpine container" dance, and a deployment story that's never quite boring.

PdfLibrary takes the other road: the entire PDF engine — parsing, content streams, fonts, and **every image codec** (baseline and progressive JPEG, JPEG 2000, JBIG2, CCITT G3/G4 fax, LZW, Flate) — is pure managed C#. No vendored native PDF library, no native image decoders.

The first version of this post had an honest asterisk: rendering a page to pixels went through SkiaSharp, which carries a native component, and I wasn't going to pretend otherwise. **2.0 removed the asterisk.** Rendering is now a small geometry-only interface the core hands flattened paths to — bring your own target, or use the in-box WPF one, which is managed WPF and ships no native code. The packages you install now have zero native dependencies. More on that below.

And rendering was always the flashy demo, not the point. So let me actually show you the toolkit.

## What it does

One core package — `Lxman.PdfLibrary` — covers the whole document lifecycle, all managed:

- **Read & extract** — parse any PDF 1.x or 2.0; pull text, with positions if you want them.
- **Create** — build documents from scratch with a fluent builder.
- **Edit** — add / remove / reorder / rotate pages, stamp, watermark, annotate, set metadata.
- **Optimize** — losslessly shrink: compression, object/xref streams, font subsetting, optional image recompression.
- **Fill forms** — read and fill AcroForm fields, regenerate appearances, flatten.

Pixels are the one thing that lives in a separate package, because most of the above never needs them.

## Extract text

```csharp
using PdfLibrary.Structure;

using var doc = PdfDocument.Load("input.pdf");
string text = doc.GetPage(0).ExtractText();
```

## Edit a document

```csharp
using PdfLibrary.Structure;

using var doc = PdfDocument.Load("input.pdf");
var edit = doc.Edit();

edit.Pages.RemoveAt(2);     // delete the 3rd page
edit.Pages.Rotate(0, 90);   // rotate the 1st page 90°
edit.Pages.Move(5, 0);      // move the 6th page to the front

edit.Save("edited.pdf");
```

## Optimize / shrink

```csharp
using PdfLibrary.Optimization;

using var doc = PdfDocument.Load("input.pdf");
using var output = File.Create("optimized.pdf");

PdfOptimizer.Optimize(doc, output);   // lossless by default
```

## Fill a form

New and front-and-center in 2.0. Set field values, optionally flatten, save:

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Editing.Forms;

using var doc = PdfDocument.Load("form.pdf");
var edit = doc.Edit();

((PdfTextField)edit.Forms["applicant.name"]!).Value = "Ada Lovelace";
((PdfButtonField)edit.Forms["subscribe"]!).Check();

edit.Forms.Flatten();   // optional: bake the values into the page
edit.Save("filled.pdf");
```

Setting a value regenerates the field's appearance stream, so it renders correctly in any viewer afterward. (There's also a pure-WPF viewer in the repo that overlays *native* controls on the form fields so a person can fill them by hand and hit Save — but that's a post of its own.)

## Render — and the "no native dependencies" story

Here's the 2.0 change worth understanding. The core flattens everything — including text, down to glyph outlines — and calls a small interface, `IRenderTarget`, with nothing but geometry: *fill this path, stroke this path, draw this image, set this clip.* About seventeen methods, none of which require any PDF knowledge to implement. The hard part — operators, fonts, color, clipping — stays in the core.

That makes rendering bring-your-own:

- **On Windows**, install `Lxman.PdfLibrary.Rendering.Wpf` and render a page to a retained WPF `DrawingGroup` — vector, so it's crisp at any zoom — then host it or rasterize it. Managed WPF, no native code.

  ```csharp
  using PdfLibrary.Rendering.Wpf;

  using var doc = PdfDocument.Load("input.pdf");
  DrawingGroup page = doc.GetPage(0).RenderToDrawing(scale: 2.0);
  // host `page` in an Image/DrawingImage, or render it into a
  // RenderTargetBitmap and PngBitmapEncoder it to a file.
  ```

- **Anywhere else**, implement `IRenderTarget` against a canvas you already have — SkiaSharp, ImageSharp, System.Drawing, a printer, an SVG writer, a game engine. The repo ships an SVG target as a worked example; it's small.

So the honest breakdown, 2.0 edition:

- Parse, extract, create, edit, optimize, fill forms → **fully managed, ship no native code.**
- Render to a vector visual or a bitmap on Windows → `Lxman.PdfLibrary.Rendering.Wpf`, **still no native code.**
- Render to a raster image cross-platform → **you bring the canvas.** WPF won't help you on Linux; that's the one line to be precise about. But "implement ~17 geometry methods" is a very different proposition from bundling and P/Invoking a native PDF engine.

No P/Invoke, no `SetDllDirectory`, no `DllNotFoundException` at 2am.

## Threading

PdfLibrary is built for the **one-document-per-request** model — the standard ASP.NET Core pattern. Load a `PdfDocument` per request, work with it, dispose it. Under that model it's thread-safe, and the process-wide caches (glyph paths, font resolution, ICC profiles) are synchronized. The one thing you must not do is share a single `PdfDocument` across threads. (WPF rendering additionally wants an STA thread, as WPF always does.)

## Install

```
dotnet add package Lxman.PdfLibrary
# rendering on Windows (optional):
dotnet add package Lxman.PdfLibrary.Rendering.Wpf
```

## Links

- NuGet: **Lxman.PdfLibrary** · **Lxman.PdfLibrary.Rendering.Wpf**
- Source (MIT): **github.com/lxman/PdfLibrary**
- Targets .NET 8, 9, and 10. Current release: **2.0.0**.

If you try it and something doesn't behave the way you expect, open an issue with the PDF attached — edge cases in the wild are how this kind of library gets better.
