using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

namespace PdfLibrary.Integration.Documents;

/// <summary>
/// Large-target colour diagnostic for the Focal CMYK raster path vs Adobe. Every page carries a small
/// "raster trigger" overprint swatch so the whole page routes to Focal's CmykPageRenderer (overprint is
/// the only thing that flips a page onto the raster path). The big swatches then isolate the suspected
/// trouble paths:
///   1. RGB round-trip   — large solid DeviceRGB squares (no blend, no alpha): SWOP RGB->CMYK->sRGB loss.
///   2. RGB blend / RGB   — large DeviceRGB over DeviceRGB at each blend mode (complement-form blend).
///   3. Separation blend  — large Separation over Separation Multiply/Screen (CMYK-space vs PCS blend).
///   4. Native-CMYK control — large DeviceCMYK solids + the overprint swatch: expected to MATCH Adobe.
/// All hit areas are >=75x150 pt so they are easy to sample by eye against Adobe.
/// </summary>
public class ColorDiagnosticTestDocument : ITestDocument
{
    public string Name => "ColorDiagnostic";
    public string Description => "Large-target RGB round-trip, RGB blend, Separation blend, and native-CMYK control swatches (every page forced onto the CMYK raster path via an overprint trigger)";

    private const double LeftMargin = 50;

    public void Generate(string outputPath)
    {
        PdfDocumentBuilder doc = new PdfDocumentBuilder()
            .WithMetadata(m => m.SetTitle("Colour Diagnostic (CMYK raster path)").SetAuthor("PdfLibrary.Integration"));

        // === Page 1: Solids — native CMYK controls vs DeviceRGB round-trip ===
        doc.AddPage(PdfPageSize.Letter, page =>
        {
            AddRasterTrigger(page);
            page.AddText("Page 1 - Solids: native CMYK (control) vs DeviceRGB (round-trip)", LeftMargin, 750, "Helvetica-Bold", 14);

            // Native CMYK solids — should MATCH Adobe (no RGB round-trip).
            page.AddText("Native DeviceCMYK solids (expected to MATCH Adobe):", LeftMargin, 712, "Helvetica", 10);
            Solid(page, LeftMargin,        700, PdfColor.CmykCyan,    "CMYK Cyan (1,0,0,0)");
            Solid(page, LeftMargin + 165,  700, PdfColor.CmykMagenta, "CMYK Magenta (0,1,0,0)");
            Solid(page, LeftMargin + 330,  700, PdfColor.CmykYellow,  "CMYK Yellow (0,0,1,0)");

            // DeviceRGB solids — expected to DESATURATE through SWOP.
            page.AddText("DeviceRGB solids (suspect: desaturated by SWOP round-trip):", LeftMargin, 492, "Helvetica", 10);
            Solid(page, LeftMargin,        480, PdfColor.Red,   "RGB Red (1,0,0)");
            Solid(page, LeftMargin + 165,  480, PdfColor.Green, "RGB Green (0,1,0)");
            Solid(page, LeftMargin + 330,  480, PdfColor.Blue,  "RGB Blue (0,0,1)");

            page.AddText("Compare each square Adobe vs Focal. CMYK row should match; RGB row is the round-trip test.",
                LeftMargin, 250, "Helvetica", 9);
        });

        // === Page 2: RGB blend over RGB (Multiply / Screen / Darken), full opacity ===
        doc.AddPage(PdfPageSize.Letter, page =>
        {
            AddRasterTrigger(page);
            page.AddText("Page 2 - DeviceRGB blend over DeviceRGB (opacity 1.0)", LeftMargin, 750, "Helvetica-Bold", 14);

            BlendTriple(page, 700, "Multiply", PdfColor.Red, PdfColor.Blue,
                "Multiply: blue over red - overlap should be BLACK (0,0,0) in Adobe");
            BlendTriple(page, 500, "Screen", PdfColor.Red, PdfColor.Blue,
                "Screen: blue over red - overlap should be MAGENTA (255,0,255) in Adobe");
            BlendTriple(page, 300, "Darken", PdfColor.Red, PdfColor.Blue,
                "Darken: blue over red - overlap should be BLACK (0,0,0) in Adobe");
        });

        // === Page 3: RGB blend over RGB (Lighten / Overlay) + Normal alpha ===
        doc.AddPage(PdfPageSize.Letter, page =>
        {
            AddRasterTrigger(page);
            page.AddText("Page 3 - DeviceRGB blend over DeviceRGB + Normal alpha", LeftMargin, 750, "Helvetica-Bold", 14);

            BlendTriple(page, 700, "Lighten", PdfColor.Red, PdfColor.Blue,
                "Lighten: blue over red - overlap should be MAGENTA (255,0,255) in Adobe");
            BlendTriple(page, 500, "Overlay", PdfColor.Red, PdfColor.Blue,
                "Overlay: blue over red");
            BlendTriple(page, 300, "Normal", PdfColor.Red, PdfColor.Blue,
                "Normal @ 0.5 alpha: overlap = 0.5*red + 0.5*blue (straight RGB composite)", 0.5);
        });

        // === Page 4: Separation blend (no RGB) + Separation solid control ===
        doc.AddPage(PdfPageSize.Letter, page =>
        {
            AddRasterTrigger(page);
            page.AddText("Page 4 - Separation blend (no RGB; CMYK-space vs PCS blend)", LeftMargin, 750, "Helvetica-Bold", 14);

            // Separation solid controls — should MATCH Adobe (Separation base resolves correctly).
            // NOTE: colorant colour comes from the library's name->CMYK map (GetCmykForColorant), not the
            // name string. Use names the library actually knows: PMS485=red, PMS300=blue, PMS375=green.
            page.AddText("Separation solids (control - expected to MATCH Adobe):", LeftMargin, 712, "Helvetica", 10);
            Solid(page, LeftMargin,       700, PdfColor.FromSeparation("PMS485", 0.85), "PMS485 (red) 0.85");
            Solid(page, LeftMargin + 165, 700, PdfColor.FromSeparation("PMS300", 0.85), "PMS300 (blue) 0.85");
            Solid(page, LeftMargin + 330, 700, PdfColor.FromSeparation("PMS375", 0.85), "PMS375 (green) 0.85");

            // Separation Multiply / Screen over a Separation backdrop — suspect blend-space divergence.
            BlendTriple(page, 480,
                "Multiply",
                PdfColor.FromSeparation("PMS485", 0.85),
                PdfColor.FromSeparation("PMS300", 0.85),
                "Multiply: PMS300 blue over PMS485 red (Separation, suspect: Focal lighter than Adobe)");
            BlendTriple(page, 270,
                "Screen",
                PdfColor.FromSeparation("PMS485", 0.85),
                PdfColor.FromSeparation("PMS375", 0.85),
                "Screen: PMS375 green over PMS485 red (Separation)");
        });

        doc.Save(outputPath);
    }

    /// <summary>Small CMYK-black-over-CMYK-cyan overprint swatch at top-right. Forces the page onto the
    /// raster path (OverprintDetector) and doubles as a native-CMYK overprint control.</summary>
    private static void AddRasterTrigger(PdfPageBuilder page)
    {
        const double tx = 430;
        page.AddText("raster trigger / CMYK overprint", tx, 778, "Helvetica", 7);
        page.AddRectangle(PdfRect.FromPoints(tx, 725, 110, 45), PdfColor.CmykCyan, null, 0);
        page.AddPath()
            .Rectangle(tx + 30, 735, 50, 25)
            .Fill(PdfColor.CmykBlack)
            .WithFillOverprint()
            .WithOverprintMode(1);
    }

    /// <summary>A large 150x150 solid swatch with a caption beneath it. <paramref name="top"/> is the
    /// swatch top edge in PDF user space (origin bottom-left).</summary>
    private static void Solid(PdfPageBuilder page, double left, double top, PdfColor color, string label)
    {
        const double s = 150;
        page.AddRectangle(PdfRect.FromPoints(left, top - s, s, s), color, null, 0);
        page.AddText(label, left, top - s - 14, "Helvetica", 8);
    }

    /// <summary>Two large overlapping rectangles producing three big measurable zones — pure base (left),
    /// blended overlap (centre), pure overlay (right) — each 75x150 pt. The overlay is offset right by
    /// half its width so all three zones are exposed.</summary>
    private static void BlendTriple(PdfPageBuilder page, double rowTop, string mode,
        PdfColor baseColor, PdfColor overColor, string label, double opacity = 1.0)
    {
        const double w = 150, h = 150;
        double bottom = rowTop - h;

        // Base rectangle (left).
        page.AddRectangle(PdfRect.FromPoints(LeftMargin, bottom, w, h), baseColor, null, 0);

        // Overlay rectangle, shifted right by w/2 so [base | overlap | overlay] are each w/2 wide.
        page.AddPath()
            .Rectangle(LeftMargin + w * 0.5, bottom, w, h)
            .Fill(overColor)
            .FillOpacity(opacity)
            .WithBlendMode(mode);

        page.AddText(label, LeftMargin, bottom - 14, "Helvetica", 9);
    }
}
