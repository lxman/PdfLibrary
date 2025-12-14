using PdfLibrary.Document;
using PdfLibrary.Structure;

// ==================== CONFIGURATION ====================
string inputPdf = args.Length > 0
    ? args[0]
    : @"C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Examples\TestPdfs\invoice.pdf";

string outputDir = args.Length > 1
    ? args[1]
    : @"C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Examples\TestPdfs\ExtractedImages";

Console.WriteLine("PDF Image Extractor\n");
Console.WriteLine($"Input PDF: {inputPdf}");
Console.WriteLine($"Output Directory: {outputDir}\n");

// Check if input file exists
if (!File.Exists(inputPdf))
{
    Console.WriteLine($"ERROR: PDF file not found: {inputPdf}");
    Console.WriteLine("\nUsage: ExtractImages <input.pdf> [output-directory]");
    return 1;
}

// Create output directory
Directory.CreateDirectory(outputDir);

// ==================== EXTRACT IMAGES ====================
try
{
    using var document = PdfDocument.Load(inputPdf);

    Console.WriteLine($"PDF loaded successfully");
    Console.WriteLine($"Total pages: {document.PageCount}\n");

    int totalImages = 0;
    int savedImages = 0;

    // Process each page (0-based indexing)
    for (int pageIndex = 0; pageIndex < document.PageCount; pageIndex++)
    {
        int pageNum = pageIndex + 1; // For display purposes
        var page = document.GetPage(pageIndex);

        if (page == null)
        {
            Console.WriteLine($"Page {pageNum}: ERROR - Could not load page");
            continue;
        }

        var images = page.GetImages();

        if (images.Count > 0)
        {
            Console.WriteLine($"Page {pageNum}: Found {images.Count} image(s)");
            totalImages += images.Count;

            // Extract each image
            for (int imgNum = 0; imgNum < images.Count; imgNum++)
            {
                var image = images[imgNum];

                try
                {
                    // Determine output format based on filters
                    string extension = DetermineExtension(image);
                    string filename = $"page{pageNum}_img{imgNum + 1}{extension}";
                    string outputPath = Path.Combine(outputDir, filename);

                    // Get image data
                    byte[] imageData;
                    if (extension == ".jpg" || extension == ".jpeg")
                    {
                        // For JPEG images, save encoded data directly
                        imageData = image.GetEncodedData();
                    }
                    else
                    {
                        // For other formats, would need to decode and re-encode
                        // For now, save raw decoded data
                        imageData = image.GetDecodedData();

                        // If we got decoded data, we should convert to PNG or another format
                        // For this example, we'll save as .raw and note the dimensions
                        filename = $"page{pageNum}_img{imgNum + 1}.raw";
                        outputPath = Path.Combine(outputDir, filename);
                    }

                    // Save to file
                    File.WriteAllBytes(outputPath, imageData);
                    savedImages++;

                    // Show image info
                    Console.WriteLine($"  ✓ {filename}");
                    Console.WriteLine($"    - Size: {image.Width}x{image.Height}");
                    Console.WriteLine($"    - Color: {image.ColorSpace}");
                    Console.WriteLine($"    - BPC: {image.BitsPerComponent}");
                    Console.WriteLine($"    - Filters: {string.Join(", ", image.Filters)}");
                    Console.WriteLine($"    - Data: {FormatBytes(imageData.Length)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ Error extracting image {imgNum + 1}: {ex.Message}");
                }
            }
            Console.WriteLine();
        }
    }

    // ==================== SUMMARY ====================
    Console.WriteLine("Extraction complete!");
    Console.WriteLine($"\nSummary:");
    Console.WriteLine($"  Total images found: {totalImages}");
    Console.WriteLine($"  Successfully saved: {savedImages}");
    Console.WriteLine($"  Output directory: {outputDir}");

    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"\nERROR: {ex.Message}");
    Console.WriteLine($"\nStack trace:\n{ex.StackTrace}");
    return 1;
}

// ==================== HELPER FUNCTIONS ====================

static string DetermineExtension(PdfImage image)
{
    // Check filters to determine format
    var filters = image.Filters;

    if (filters.Count > 0)
    {
        string firstFilter = filters[0];

        return firstFilter switch
        {
            "DCTDecode" => ".jpg",
            "JPEG2000Decode" or "JPXDecode" => ".jp2",
            "CCITTFaxDecode" => ".tif",
            "JBIG2Decode" => ".jb2",
            _ => ".raw"
        };
    }

    return ".raw";
}

static string FormatBytes(long bytes)
{
    string[] suffixes = { "B", "KB", "MB", "GB" };
    int suffixIndex = 0;
    double size = bytes;

    while (size >= 1024 && suffixIndex < suffixes.Length - 1)
    {
        size /= 1024;
        suffixIndex++;
    }

    return $"{size:F2} {suffixes[suffixIndex]}";
}
