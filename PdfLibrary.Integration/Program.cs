using System;
using System.Collections.Generic;
using System.IO;
using PdfLibrary.Integration;
using PdfLibrary.Integration.Documents;

// Configuration
const string baseDir = @"C:\Users\jorda\RiderProjects\PDF\TestPDFs\targeted_custom_generated";
const string goldenDir = "golden";      // Subdirectory for golden PDFs and images
const string testDir = "test";          // Subdirectory for test outputs
const double renderScale = 2.0;         // 144 DPI for better comparison
const double matchThreshold = 95.0;     // Minimum % match to pass (allows for antialiasing differences)

// Parse command line
string mode = args.Length > 0 ? args[0].ToLower() : "help";

// Register all test documents
ITestDocument[] testDocuments =
[
    new ColorSpaceTestDocument(),
    new PathDrawingTestDocument(),
    new TransparencyTestDocument(),
    new ClippingPathTestDocument(),
    new LineStyleTestDocument(),
    new TextBasicsTestDocument(),
    new TextLayoutTestDocument(),
    new TextRenderingTestDocument(),
    new EmbeddedFontsTestDocument(),
];

switch (mode)
{
    case "baseline":
        return GenerateBaseline(testDocuments, baseDir, goldenDir, renderScale);

    case "test":
        return RunTests(testDocuments, baseDir, goldenDir, testDir, renderScale, matchThreshold);

    case "generate":
        // Legacy mode: just generate PDFs without images (for backwards compatibility)
        return GeneratePdfsOnly(testDocuments, baseDir);

    default:
        PrintHelp();
        return 0;
}

static void PrintHelp()
{
    Console.WriteLine("PDF Test Document Generator");
    Console.WriteLine("===========================");
    Console.WriteLine();
    Console.WriteLine("Usage: PdfLibrary.Integration <command>");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  baseline  - Generate golden baseline PDFs and images");
    Console.WriteLine("              Creates PDFs and renders them to images.");
    Console.WriteLine("              Manually verify these look correct before committing.");
    Console.WriteLine();
    Console.WriteLine("  test      - Run tests comparing current rendering against baseline");
    Console.WriteLine("              Regenerates PDFs, renders them, and compares to golden images.");
    Console.WriteLine("              Generates an HTML report with results.");
    Console.WriteLine();
    Console.WriteLine("  generate  - Generate PDFs only (legacy mode)");
    Console.WriteLine();
    Console.WriteLine("  help      - Show this help message");
}

static int GenerateBaseline(ITestDocument[] documents, string baseDir, string goldenDir, double scale)
{
    string outputDir = Path.Combine(baseDir, goldenDir);
    Directory.CreateDirectory(outputDir);

    Console.WriteLine("Generating Golden Baseline");
    Console.WriteLine("==========================");
    Console.WriteLine($"Output directory: {outputDir}");
    Console.WriteLine($"Render scale: {scale}x ({72 * scale} DPI)");
    Console.WriteLine();

    var success = 0;
    var failed = 0;

    foreach (ITestDocument doc in documents)
    {
        string pdfPath = Path.Combine(outputDir, $"{doc.Name}.pdf");
        string imagePath = Path.Combine(outputDir, $"{doc.Name}_golden.png");

        Console.WriteLine($"  {doc.Name}");
        Console.WriteLine($"    {doc.Description}");

        try
        {
            // Generate PDF
            doc.Generate(pdfPath);
            Console.WriteLine($"    PDF: {pdfPath}");

            // Render to image
            PdfImageRenderer.RenderToImage(pdfPath, imagePath, scale);
            Console.WriteLine($"    Image: {imagePath}");

            success++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ERROR: {ex.Message}");
            failed++;
        }

        Console.WriteLine();
    }

    Console.WriteLine("==========================");
    Console.WriteLine($"Generated: {success}, Failed: {failed}");
    Console.WriteLine();
    Console.WriteLine("IMPORTANT: Manually verify these images look correct before committing!");
    Console.WriteLine("Use Adobe Acrobat or another PDF viewer to check the PDFs.");

    return failed > 0 ? 1 : 0;
}

static int RunTests(ITestDocument[] documents, string baseDir, string goldenDir, string testDir, double scale, double threshold)
{
    string goldenPath = Path.Combine(baseDir, goldenDir);
    string testPath = Path.Combine(baseDir, testDir);
    Directory.CreateDirectory(testPath);

    Console.WriteLine("Running Rendering Tests");
    Console.WriteLine("=======================");
    Console.WriteLine($"Golden baseline: {goldenPath}");
    Console.WriteLine($"Test output: {testPath}");
    Console.WriteLine($"Match threshold: {threshold}%");
    Console.WriteLine();

    var results = new List<TestResult>();
    var passed = 0;
    var failed = 0;

    foreach (ITestDocument doc in documents)
    {
        string goldenPdfPath = Path.Combine(goldenPath, $"{doc.Name}.pdf");
        string goldenImagePath = Path.Combine(goldenPath, $"{doc.Name}_golden.png");
        string testPdfPath = Path.Combine(testPath, $"{doc.Name}.pdf");
        string testImagePath = Path.Combine(testPath, $"{doc.Name}_actual.png");
        string diffImagePath = Path.Combine(testPath, $"{doc.Name}_diff.png");

        Console.WriteLine($"  {doc.Name}");

        try
        {
            // Check golden baseline exists
            if (!File.Exists(goldenImagePath))
            {
                Console.WriteLine($"    SKIP: No golden baseline found");
                results.Add(new TestResult(doc.Name, doc.Description, false, 0, "No golden baseline - run 'baseline' first"));
                failed++;
                Console.WriteLine();
                continue;
            }

            // Generate test PDF
            doc.Generate(testPdfPath);

            // Render test image
            PdfImageRenderer.RenderToImage(testPdfPath, testImagePath, scale);

            // Compare images
            ComparisonResult comparison = ImageComparer.Compare(goldenImagePath, testImagePath, diffImagePath);

            if (!comparison.Success)
            {
                Console.WriteLine($"    FAIL: {comparison.Message}");
                results.Add(new TestResult(doc.Name, doc.Description, false, 0, comparison.Message));
                failed++;
            }
            else if (comparison.MatchPercentage >= threshold)
            {
                Console.WriteLine($"    PASS ({comparison.MatchPercentage:F2}% match)");
                results.Add(new TestResult(doc.Name, doc.Description, true, comparison.MatchPercentage, null));
                passed++;

                // Clean up diff image for passing tests
                if (File.Exists(diffImagePath))
                    File.Delete(diffImagePath);
            }
            else
            {
                Console.WriteLine($"    FAIL ({comparison.MatchPercentage:F2}% match, threshold {threshold}%)");
                results.Add(new TestResult(doc.Name, doc.Description, false, comparison.MatchPercentage, null));
                failed++;
            }

            // Copy golden image to test dir for report
            string goldenCopy = Path.Combine(testPath, $"{doc.Name}_golden.png");
            File.Copy(goldenImagePath, goldenCopy, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ERROR: {ex.Message}");
            results.Add(new TestResult(doc.Name, doc.Description, false, 0, ex.Message));
            failed++;
        }

        Console.WriteLine();
    }

    // Generate HTML report
    string reportPath = Path.Combine(testPath, "report.html");
    ReportGenerator.GenerateHtmlReport(results, reportPath, testPath);

    Console.WriteLine("=======================");
    Console.WriteLine($"Passed: {passed}, Failed: {failed}");
    Console.WriteLine($"Report: {reportPath}");

    return failed > 0 ? 1 : 0;
}

static int GeneratePdfsOnly(ITestDocument[] documents, string outDir)
{
    Directory.CreateDirectory(outDir);

    Console.WriteLine($"Generating {documents.Length} test PDF documents...");
    Console.WriteLine($"Output directory: {outDir}");
    Console.WriteLine();

    foreach (ITestDocument doc in documents)
    {
        string outputPath = Path.Combine(outDir, $"{doc.Name}.pdf");

        Console.WriteLine($"  Generating: {doc.Name}");
        Console.WriteLine($"    {doc.Description}");

        try
        {
            doc.Generate(outputPath);
            Console.WriteLine($"    -> {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    ERROR: {ex.Message}");
        }

        Console.WriteLine();
    }

    Console.WriteLine("Done!");
    return 0;
}
