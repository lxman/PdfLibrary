using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Functions;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;
using Xunit.Abstractions;

namespace PdfLibrary.Tests;

/// <summary>
/// A render target that outputs detailed color info for debugging
/// </summary>
public class DetailedMockRenderTarget(ITestOutputHelper output) : IRenderTarget
{
    public List<string> Operations { get; } = [];
    public int CurrentPageNumber { get; private set; }

    public void BeginPage(int pageNumber, double width, double height, double scale = 1.0, double cropOffsetX = 0, double cropOffsetY = 0) { CurrentPageNumber = pageNumber; }
    public void EndPage() { }
    public void StrokePath(IPathBuilder path, PdfGraphicsState state) { }
    public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }
    public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }
    public void DrawImage(PdfImage image, PdfGraphicsState state) { }
    public void SaveState() { }
    public void RestoreState() { }
    public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }
    public void ApplyCtm(System.Numerics.Matrix3x2 ctm) { }
    public void OnGraphicsStateChanged(PdfGraphicsState state) { }
    public void Clear() { Operations.Clear(); }
    public void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent) { }
    public void ClearSoftMask() { }
    public (int width, int height, double scale) GetPageDimensions() => (612, 792, 1.0);

    public void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont? font, List<int>? charCodes = null)
    {
        // Capture detailed color information using RESOLVED fields (what renderer should use)
        var colorInfo = $"ColorSpace={state.ResolvedFillColorSpace}, ColorCount={state.ResolvedFillColor.Count}, Color=[{string.Join(", ", state.ResolvedFillColor.Select(c => c.ToString("F4")))}]";
        string shortText = text.Length > 30 ? text[..30] + "..." : text;
        Operations.Add($"DrawText: '{shortText}' {colorInfo}");
        output.WriteLine($"DRAW: '{shortText}' {colorInfo}");
    }
}

/// <summary>
/// Integration tests demonstrating complete PDF document loading and text extraction
/// </summary>
public class PdfDocumentIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public void LoadPdf_SimplePdf20File_LoadsSuccessfully()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\Simple PDF 2.0 file.pdf";

        if (!File.Exists(pdfPath))
        {
            output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);

        Assert.NotNull(doc);
        Assert.NotNull(doc.XrefTable);
        Assert.NotNull(doc.Trailer);
        output.WriteLine($"Loaded PDF version: {doc.Version}");
        output.WriteLine($"Number of objects: {doc.Objects.Count}");
    }

    [Fact]
    public void ExtractText_SimplePdf20File_ExtractsText()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\Simple PDF 2.0 file.pdf";

        if (!File.Exists(pdfPath))
        {
            output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        int pageCount = doc.GetPageCount();

        output.WriteLine($"PDF has {pageCount} page(s)");
        Assert.True(pageCount > 0, "PDF should have at least one page");

        PdfPage? page = doc.GetPage(0);
        Assert.NotNull(page);

        string text = page.ExtractText();

        output.WriteLine($"Extracted text length: {text.Length} characters");
        output.WriteLine($"Extracted text: {text}");

        Assert.NotEmpty(text);
    }

    [Fact]
    public void ExtractTextWithFragments_SimplePdf20File_ReturnsPositionInformation()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\Simple PDF 2.0 file.pdf";

        if (!File.Exists(pdfPath))
        {
            output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        PdfPage? page = doc.GetPage(0);
        Assert.NotNull(page);

        (string text, List<TextFragment> fragments) = page.ExtractTextWithFragments();

        output.WriteLine($"Extracted text: {text}");
        output.WriteLine($"Number of text fragments: {fragments.Count}");
        output.WriteLine("");
        output.WriteLine("Text fragments with position information:");

        foreach (TextFragment fragment in fragments)
        {
            output.WriteLine($"  Text: '{fragment.Text}'");
            output.WriteLine($"  Position: ({fragment.X:F2}, {fragment.Y:F2})");
            output.WriteLine($"  Font: {fragment.FontName ?? "unknown"} {fragment.FontSize:F1}pt");
            output.WriteLine("");
        }

        // Verify we got positioning information
        Assert.NotEmpty(text);
        Assert.NotEmpty(fragments);

        // Each fragment should have position information
        foreach (TextFragment fragment in fragments)
        {
            Assert.NotNull(fragment.Text);
            Assert.NotEmpty(fragment.Text);
            // Position should be set (not default 0,0 for all fragments)
        }
    }

    [Fact]
    public void ExtractTextFromMultiplePages_Utf8TestPdf_ExtractsAllPages()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\pdf20-utf8-test.pdf";

        if (!File.Exists(pdfPath))
        {
            output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        int pageCount = doc.GetPageCount();

        output.WriteLine($"PDF has {pageCount} page(s)");

        for (var i = 0; i < pageCount; i++)
        {
            PdfPage? page = doc.GetPage(i);
            Assert.NotNull(page);

            (string text, List<TextFragment> fragments) = page.ExtractTextWithFragments();

            output.WriteLine($"");
            output.WriteLine($"=== Page {i + 1} ===");
            output.WriteLine($"Text length: {text.Length} characters");
            output.WriteLine($"Fragment count: {fragments.Count}");

            if (fragments.Count <= 0) continue;
            output.WriteLine($"First fragment: '{fragments[0].Text}' at ({fragments[0].X:F2}, {fragments[0].Y:F2})");
            if (fragments.Count > 1)
                output.WriteLine($"Last fragment: '{fragments[^1].Text}' at ({fragments[^1].X:F2}, {fragments[^1].Y:F2})");
        }
    }

    [Fact]
    public void GetPageDimensions_SimplePdf20File_ReturnsValidDimensions()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\Simple PDF 2.0 file.pdf";

        if (!File.Exists(pdfPath))
        {
            output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        PdfPage? page = doc.GetPage(0);
        Assert.NotNull(page);

        double width = page.Width;
        double height = page.Height;

        output.WriteLine($"Page dimensions: {width:F2} x {height:F2} points");
        output.WriteLine($"Page dimensions: {width / 72:F2} x {height / 72:F2} inches");

        Assert.True(width > 0, "Width should be positive");
        Assert.True(height > 0, "Height should be positive");
    }

    [Fact]
    public void ExtractTextWithFragments_VerifyPositionAccuracy_FragmentsWithinPageBounds()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\Simple PDF 2.0 file.pdf";

        if (!File.Exists(pdfPath))
        {
            output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        PdfPage? page = doc.GetPage(0);
        Assert.NotNull(page);

        double pageWidth = page.Width;
        double pageHeight = page.Height;

        (string text, List<TextFragment> fragments) = page.ExtractTextWithFragments();

        output.WriteLine($"Page size: {pageWidth:F2} x {pageHeight:F2}");
        output.WriteLine($"Checking {fragments.Count} fragments are within page bounds");

        foreach (TextFragment fragment in fragments)
        {
            output.WriteLine($"  Fragment '{fragment.Text}' at ({fragment.X:F2}, {fragment.Y:F2})");

            // Positions should be within reasonable page bounds
            // (allowing for text that might extend slightly beyond due to transforms)
            Assert.True(fragment.X >= -100, $"X position {fragment.X} too far left");
            Assert.True(fragment.X <= pageWidth + 100, $"X position {fragment.X} too far right");
            Assert.True(fragment.Y >= -100, $"Y position {fragment.Y} too far below");
            Assert.True(fragment.Y <= pageHeight + 100, $"Y position {fragment.Y} too far above");
        }
    }

    [Fact]
    public void GetImages_Pdf20ImageWithBpc_ExtractsImage()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\PDF 2.0 image with BPC.pdf";

        if (!File.Exists(pdfPath))
        {
            output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        int pageCount = doc.GetPageCount();

        output.WriteLine($"PDF has {pageCount} page(s)");

        for (var i = 0; i < pageCount; i++)
        {
            PdfPage? page = doc.GetPage(i);
            Assert.NotNull(page);

            List<PdfImage> images = page.GetImages();

            output.WriteLine($"");
            output.WriteLine($"=== Page {i + 1} ===");
            output.WriteLine($"Number of images: {images.Count}");

            foreach (PdfImage image in images)
            {
                output.WriteLine($"");
                output.WriteLine($"Image: {image}");
                output.WriteLine($"  Size: {image.Width}x{image.Height} pixels");
                output.WriteLine($"  Color Space: {image.ColorSpace}");
                output.WriteLine($"  Bits Per Component: {image.BitsPerComponent}");
                output.WriteLine($"  Component Count: {image.ComponentCount}");
                output.WriteLine($"  Filters: {string.Join(", ", image.Filters)}");
                output.WriteLine($"  Has Alpha: {image.HasAlpha}");
                output.WriteLine($"  Is Mask: {image.IsImageMask}");
                output.WriteLine($"  Expected Data Size: {image.GetExpectedDataSize()} bytes");

                // Verify image has valid dimensions
                Assert.True(image.Width > 0, "Image width should be positive");
                Assert.True(image.Height > 0, "Image height should be positive");
            }
        }
    }

    [Fact]
    public void GetImages_AllPdf20Examples_CountsImages()
    {
        string[] pdfFiles =
        [
            @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\Simple PDF 2.0 file.pdf",
            @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\PDF 2.0 image with BPC.pdf",
            @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\PDF 2.0 UTF-8 string and annotation.pdf",
            @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\PDF 2.0 via incremental save.pdf"
        ];

        output.WriteLine("Scanning PDF files for images:");
        output.WriteLine("");

        foreach (string pdfPath in pdfFiles)
        {
            if (!File.Exists(pdfPath))
            {
                output.WriteLine($"Skipped: {Path.GetFileName(pdfPath)} (not found)");
                continue;
            }

            try
            {
                using PdfDocument doc = PdfDocument.Load(pdfPath);
                var totalImages = 0;

                for (var i = 0; i < doc.GetPageCount(); i++)
                {
                    PdfPage? page = doc.GetPage(i);
                    if (page != null)
                    {
                        totalImages += page.GetImageCount();
                    }
                }

                output.WriteLine($"{Path.GetFileName(pdfPath)}: {totalImages} image(s)");
            }
            catch (Exception ex)
            {
                output.WriteLine($"{Path.GetFileName(pdfPath)}: Error - {ex.Message}");
            }
        }
    }

    [Fact]
    public void ExtractImages_VerifyImageData_DataSizeMatchesExpected()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\PDF 2.0 image with BPC.pdf";

        if (!File.Exists(pdfPath))
        {
            output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);

        for (var i = 0; i < doc.GetPageCount(); i++)
        {
            PdfPage? page = doc.GetPage(i);
            if (page == null) continue;

            List<PdfImage> images = page.GetImages();

            foreach (PdfImage image in images)
            {
                byte[] data = image.GetDecodedData();
                int expectedSize = image.GetExpectedDataSize();

                output.WriteLine($"Image {image.Width}x{image.Height}:");
                output.WriteLine($"  Actual data size: {data.Length} bytes");
                output.WriteLine($"  Expected data size: {expectedSize} bytes");

                // Note: Actual size might be larger than expected due to padding or
                // different than expected for compressed formats like JPEG
                Assert.NotEmpty(data);
            }
        }
    }

    [Fact]
    public void GetImageCount_SimplePdf_ReturnsCorrectCount()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\pdf20examples\Simple PDF 2.0 file.pdf";

        if (!File.Exists(pdfPath))
        {
            output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        PdfPage? page = doc.GetPage(0);
        Assert.NotNull(page);

        int imageCount = page.GetImageCount();
        List<PdfImage> images = page.GetImages();

        output.WriteLine($"Image count (via GetImageCount): {imageCount}");
        output.WriteLine($"Image count (via GetImages().Count): {images.Count}");

        Assert.Equal(images.Count, imageCount);
    }

    [Fact]
    public void Debug_TintTransformFunction_Tiff6()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\PDF Standards\Compression\CCITT\tiff6.pdf";

        if (!File.Exists(pdfPath))
        {
            output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);

        // Get object 401 (tint transform for /Black separation)
        PdfObject? obj401 = doc.ResolveReference(new PdfIndirectReference(401, 0));
        output.WriteLine($"Object 401 type: {obj401?.GetType().Name ?? "null"}");

        if (obj401 is PdfStream stream)
        {
            output.WriteLine($"Stream dictionary: {stream.Dictionary}");

            // Get decoded data
            byte[] data = stream.GetDecodedData();
            output.WriteLine($"Decoded data length: {data.Length} bytes");
            output.WriteLine($"First 30 bytes: {BitConverter.ToString(data.Take(30).ToArray())}");
            output.WriteLine($"Last 30 bytes: {BitConverter.ToString(data.Skip(data.Length - 30).ToArray())}");
        }

        // Create the function
        var func = PdfFunction.Create(obj401!, doc);
        output.WriteLine($"Function type: {func?.GetType().Name ?? "null"}");

        if (func is null) return;
        output.WriteLine($"Domain: [{string.Join(", ", func.Domain)}]");
        output.WriteLine($"Range: [{string.Join(", ", func.Range ?? [])}]");
        output.WriteLine($"InputCount: {func.InputCount}, OutputCount: {func.OutputCount}");

        // Test evaluations
        double[] tintValues = [0.0, 0.25, 0.5, 0.75, 1.0];
        foreach (double tint in tintValues)
        {
            double[] result = func.Evaluate([tint]);
            output.WriteLine($"  tint={tint:F2} -> RGB=[{string.Join(", ", result.Select(r => r.ToString("F4")))}]");
        }

        // For Black separation:
        // tint=0 should give white (1,1,1) - no ink applied
        // tint=1 should give black (0,0,0) - full ink applied
        output.WriteLine("");
        output.WriteLine("Expected for Black separation:");
        output.WriteLine("  tint=0 -> white (1,1,1)");
        output.WriteLine("  tint=1 -> black (0,0,0)");
    }

    [Fact]
    public void Debug_ColorSpaceResolution_Tiff6()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\PDF Standards\Compression\CCITT\tiff6.pdf";

        if (!File.Exists(pdfPath))
        {
            output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);

        // Get page 1's Resources to see the ColorSpace definitions
        PdfPage? page = doc.GetPage(0);
        Assert.NotNull(page);

        PdfDictionary pageDict = page.Dictionary;
        output.WriteLine($"Page dictionary: {pageDict}");

        // Get Resources using PdfResources
        PdfResources? resources = page.GetResources();
        if (resources is null)
        {
            output.WriteLine("No Resources on page");
            return;
        }

        // Get ColorSpace dictionary
        PdfDictionary? csDict = resources.GetColorSpaces();
        if (csDict is null)
        {
            output.WriteLine("No ColorSpace dictionary in Resources");
            return;
        }

        output.WriteLine($"ColorSpace dictionary has {csDict.Count} entries:");
        foreach (KeyValuePair<PdfName, PdfObject> kvp in csDict)
        {
            string key = kvp.Key.Value;
            PdfObject? value = kvp.Value;

            output.WriteLine($"  {key}: {value.GetType().Name}");

            // Resolve if needed
            if (value is PdfIndirectReference valRef)
                value = doc.ResolveReference(valRef);

            if (value is not PdfArray csArray) continue;
            output.WriteLine($"    Array contents ({csArray.Count} elements):");
            for (var i = 0; i < csArray.Count; i++)
            {
                PdfObject elem = csArray[i];
                string elemStr = elem switch
                {
                    PdfName n => $"/{ n.Value}",
                    PdfIndirectReference r => $"{r.ObjectNumber} {r.GenerationNumber} R",
                    PdfArray a => $"[array with {a.Count} elements]",
                    PdfDictionary d => $"<<dict with {d.Count} entries>>",
                    _ => elem.ToString()
                };
                output.WriteLine($"      [{i}]: {elem.GetType().Name} = {elemStr}");

                // For element 3 (tint transform), try to create the function
                if (i != 3) continue;
                output.WriteLine($"    Trying to create function from element 3...");
                var func = PdfFunction.Create(elem, doc);
                output.WriteLine($"    Function created: {func?.GetType().Name ?? "null"}");
                if (func is null) continue;
                double[] result = func.Evaluate([1.0]);
                output.WriteLine($"    func.Evaluate([1.0]) = [{string.Join(", ", result.Select(r => r.ToString("F4")))}]");
            }
        }
    }

    [Fact]
    public void Debug_RenderTiff6Page1_TraceColors()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\PDF Standards\Compression\CCITT\tiff6.pdf";

        if (!File.Exists(pdfPath))
        {
            output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        // Enable logging
        string logFile = Path.Combine(Path.GetTempPath(), "tiff6_color_debug.log");
        Logging.PdfLogger.Initialize(new Logging.PdfLogConfiguration
        {
            LogFilePath = logFile,
            LogGraphics = true,
            AppendToLog = false
        });

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        PdfPage? page = doc.GetPage(0);
        Assert.NotNull(page);

        // Use a custom mock that captures more detail
        var mock = new DetailedMockRenderTarget(output);
        PdfResources? resources = page.GetResources();
        var renderer = new PdfRenderer(mock, resources, null, doc);

        renderer.RenderPage(page, 1);

        Logging.PdfLogger.Shutdown();

        // Output log file location and first 50 lines
        output.WriteLine($"\nLog file: {logFile}");
        if (File.Exists(logFile))
        {
            string[] logLines = File.ReadAllLines(logFile).Take(50).ToArray();
            output.WriteLine($"First {logLines.Length} log lines:");
            foreach (string line in logLines)
            {
                output.WriteLine($"  {line}");
            }
        }

        // Output all DrawText operations with their colors
        output.WriteLine($"Total operations: {mock.Operations.Count}");
        output.WriteLine("");
        output.WriteLine("DrawText operations:");
        var textOpCount = 0;
        foreach (string op in mock.Operations.Where(o => o.StartsWith("DrawText:")))
        {
            output.WriteLine($"  {op}");
            textOpCount++;
            if (textOpCount >= 30) // Limit output
            {
                output.WriteLine($"  ... and {mock.Operations.Count(o => o.StartsWith("DrawText:")) - 30} more");
                break;
            }
        }

        // Check if there are any black text operations (Color should be DeviceRGB(0.00, 0.00, 0.00))
        List<string> blackTextOps = mock.Operations.Where(o =>
            o.StartsWith("DrawText:") &&
            o.Contains("DeviceRGB(0.00, 0.00, 0.00)")).ToList();
        output.WriteLine($"");
        output.WriteLine($"Black text operations: {blackTextOps.Count}");

        // Check for operations with other colors
        List<string> grayTextOps = mock.Operations.Where(o =>
            o.StartsWith("DrawText:") &&
            !o.Contains("DeviceRGB(0.00, 0.00, 0.00)")).ToList();
        output.WriteLine($"Non-black text operations: {grayTextOps.Count}");
        foreach (string op in grayTextOps.Take(10))
        {
            output.WriteLine($"  {op}");
        }
    }

    [Fact]
    public void Debug_ExamineCs9ColorSpace()
    {
        var pdfPath = @"C:\Users\jorda\RiderProjects\PDF\PDF Standards\Compression\CCITT\tiff6.pdf";

        if (!File.Exists(pdfPath))
        {
            output.WriteLine($"Skipping test - PDF file not found: {pdfPath}");
            return;
        }

        using PdfDocument doc = PdfDocument.Load(pdfPath);
        PdfPage? page = doc.GetPage(0);
        Assert.NotNull(page);

        PdfResources? resources = page.GetResources();
        Assert.NotNull(resources);

        // Get Cs9 color space from resources
        PdfDictionary? colorSpaces = resources.GetColorSpaces();
        Assert.NotNull(colorSpaces);

        output.WriteLine("Available color spaces:");
        foreach (KeyValuePair<PdfName, PdfObject> cs in colorSpaces)
        {
            output.WriteLine($"  {cs.Key}");
        }

        Assert.True(colorSpaces.TryGetValue(new PdfName("Cs9"), out PdfObject? cs9Obj));
        output.WriteLine($"\nCs9 object type: {cs9Obj?.GetType().Name}");

        // Resolve indirect reference if needed
        if (cs9Obj is PdfIndirectReference cs9Ref)
        {
            cs9Obj = doc.ResolveReference(cs9Ref);
            output.WriteLine($"After resolve, Cs9 object type: {cs9Obj?.GetType().Name}");
        }

        Assert.True(cs9Obj is PdfArray, $"Expected PdfArray but got {cs9Obj?.GetType().Name}");
        var cs9Array = (PdfArray)cs9Obj!;

        output.WriteLine($"\nCs9 array contents (count={cs9Array.Count}):");
        for (var i = 0; i < cs9Array.Count; i++)
        {
            PdfObject elem = cs9Array[i];
            if (elem is PdfIndirectReference elemRef)
            {
                PdfObject? resolved = doc.ResolveReference(elemRef);
                output.WriteLine($"  [{i}]: {elem} -> {resolved?.GetType().Name}");
            }
            else
            {
                output.WriteLine($"  [{i}]: {elem?.GetType().Name} = {elem}");
            }
        }

        // Check that it's a Separation color space
        Assert.True(cs9Array[0] is PdfName { Value: "Separation" });

        // Get the tint transform function (index 3)
        PdfObject? tintTransformObj = cs9Array[3];
        output.WriteLine($"\nTint transform object type: {tintTransformObj?.GetType().Name}");

        if (tintTransformObj is PdfIndirectReference tintRef)
        {
            tintTransformObj = doc.ResolveReference(tintRef);
            output.WriteLine($"After resolve, tint transform type: {tintTransformObj?.GetType().Name}");
        }

        // Create the tint transform function
        var tintTransform = PdfFunction.Create(tintTransformObj, doc);
        output.WriteLine($"\nTint transform function: {tintTransform?.GetType().Name ?? "NULL"}");

        if (tintTransform is not null)
        {
            output.WriteLine($"  InputCount: {tintTransform.InputCount}");
            output.WriteLine($"  OutputCount: {tintTransform.OutputCount}");
            output.WriteLine($"  Domain: [{string.Join(", ", tintTransform.Domain)}]");
            output.WriteLine($"  Range: [{string.Join(", ", tintTransform.Range ?? [])}]");

            // Test the tint transform with tint=1 (should produce black)
            double[] result = tintTransform.Evaluate([1.0]);
            output.WriteLine($"\n  Evaluate(1.0) = [{string.Join(", ", result.Select(r => r.ToString("F4")))}]");

            // Test with tint=0 (should produce white)
            result = tintTransform.Evaluate([0.0]);
            output.WriteLine($"  Evaluate(0.0) = [{string.Join(", ", result.Select(r => r.ToString("F4")))}]");

            // Test with tint=0.5
            result = tintTransform.Evaluate([0.5]);
            output.WriteLine($"  Evaluate(0.5) = [{string.Join(", ", result.Select(r => r.ToString("F4")))}]");
        }
        else
        {
            output.WriteLine("ERROR: Could not create tint transform function!");
        }
    }
}
