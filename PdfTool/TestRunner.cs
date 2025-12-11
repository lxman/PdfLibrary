using System;
using System.IO;

namespace PdfTool;

/// <summary>
/// Simple test runner for advanced PDF features
/// </summary>
public static class TestRunner
{
    public static void RunTests()
    {
        Console.WriteLine("PDF Advanced Features Test Suite");
        Console.WriteLine("================================\n");

        // Create output directory
        var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "test-output");
        Directory.CreateDirectory(outputDir);

        // Test 1: Advanced features
        var testFile = Path.Combine(outputDir, "advanced-features-test.pdf");
        Console.WriteLine($"Creating test document: {testFile}");

        try
        {
            TestAdvancedFeatures.CreateTestDocument(testFile);
            Console.WriteLine("✓ Test document created successfully\n");

            Console.WriteLine("Test document includes:");
            Console.WriteLine("  • Page 1: Separation color spaces (spot colors)");
            Console.WriteLine("  • Page 2: CTM transformations (translate, rotate, scale)");
            Console.WriteLine("  • Page 3: Overprint modes and blend modes");
            Console.WriteLine("  • Page 4: Combined features\n");

            Console.WriteLine($"Open the file in Adobe Acrobat or Ghostscript:");
            Console.WriteLine($"  {testFile}\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error creating test document: {ex.Message}");
            Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
        }
    }
}
