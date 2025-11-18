using PdfLibrary.Structure;
using PdfLibrary.Document;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

/// <summary>
/// Extracts and compares raw decoded image data from PDFium and PdfLibrary
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        string pdfPath = @"C:\Users\jorda\RiderProjects\PDF\PDF Standards\PDF20_AN002-AF.pdf";

        Console.WriteLine("=== Decoded Image Data Comparison ===\n");
        Console.WriteLine($"Loading PDF: {pdfPath}\n");

        // Extract from PdfLibrary
        Console.WriteLine("Extracting decoded data from PdfLibrary...");
        var pdfLibraryData = ExtractFromPdfLibrary(pdfPath);

        if (pdfLibraryData != null)
        {
            Console.WriteLine($"✓ PdfLibrary decoded data: {pdfLibraryData.Length} bytes");
            File.WriteAllBytes("PdfLibrary_DecodedImage.bin", pdfLibraryData);
            Console.WriteLine("✓ Saved: PdfLibrary_DecodedImage.bin");

            // Show first 100 bytes as hex
            Console.WriteLine("\nFirst 100 bytes (hex):");
            ShowHex(pdfLibraryData, 100);

            // Show byte value distribution
            Console.WriteLine("\nByte value distribution (first 1000 bytes):");
            AnalyzeByteDistribution(pdfLibraryData, 1000);

            Console.WriteLine("\nByte value distribution (ALL bytes):");
            AnalyzeByteDistribution(pdfLibraryData, pdfLibraryData.Length);
        }
        else
        {
            Console.WriteLine("✗ Failed to extract PdfLibrary data");
        }

        Console.WriteLine("\n=== Comparison completed ===");
    }

    private static byte[]? ExtractFromPdfLibrary(string pdfPath)
    {
        try
        {
            using var stream = File.OpenRead(pdfPath);
            var document = PdfDocument.Load(stream);

            var catalog = document.GetCatalog();
            if (catalog == null) return null;

            var pageTree = catalog.GetPageTree();
            if (pageTree == null) return null;

            var pages = pageTree.GetPages();
            if (pages == null || pages.Count == 0) return null;

            var firstPage = pages[0];
            var resources = firstPage.GetResources();
            if (resources == null) return null;

            // Get the Im0 XObject (the logo image)
            var xObjStream = resources.GetXObject("Im0");
            if (xObjStream == null)
            {
                Console.WriteLine("Im0 XObject not found");
                return null;
            }

            Console.WriteLine("Found XObject: Im0");
            Console.WriteLine("\n  Raw XObject Dictionary:");
            foreach (var kvp in xObjStream.Dictionary)
            {
                string value = kvp.Value switch
                {
                    PdfInteger i => i.Value.ToString(),
                    PdfName n => $"/{n.Value}",
                    PdfArray a => $"[{a.Count} items]",
                    PdfStream s => $"<stream {s.Length} bytes>",
                    PdfIndirectReference r => $"Reference to {r.ObjectNumber} {r.GenerationNumber} R",
                    _ => kvp.Value.ToString() ?? "null"
                };
                Console.WriteLine($"    /{kvp.Key.Value}: {value}");
            }
            Console.WriteLine();

            var image = new PdfImage(xObjStream, document);

            Console.WriteLine($"  -> Width: {image.Width}, Height: {image.Height}");
            Console.WriteLine($"  -> ColorSpace: {image.ColorSpace}");
            Console.WriteLine($"  -> BitsPerComponent: {image.BitsPerComponent}");
            Console.WriteLine($"  -> Filters: {string.Join(", ", image.Filters)}");

            // Check for DecodeParms
            if (xObjStream.Dictionary.TryGetValue(new PdfName("DecodeParms"), out PdfObject? decodeParmsObj))
            {
                Console.WriteLine($"  -> DecodeParms found: {decodeParmsObj}");
                if (decodeParmsObj is PdfDictionary dpDict)
                {
                    foreach (var kvp in dpDict)
                    {
                        Console.WriteLine($"      {kvp.Key.Value} = {kvp.Value}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"  -> DecodeParms: None");
            }

            // Get palette information
            var palette = image.GetIndexedPalette(out string? baseColorSpace, out int hival);
            if (palette != null)
            {
                Console.WriteLine($"  -> Palette: {palette.Length} bytes, Base: {baseColorSpace}, Hival: {hival}");
                Console.WriteLine($"  -> Number of palette entries: {hival + 1}");

                // Show first 10 and entry 127 palette entries
                int componentsPerEntry = baseColorSpace == "DeviceRGB" ? 3 : 1;
                Console.WriteLine($"\n  Palette entries (first 10 and key entries):");
                for (int i = 0; i <= Math.Min(9, hival); i++)
                {
                    int offset = i * componentsPerEntry;
                    if (componentsPerEntry == 3 && offset + 2 < palette.Length)
                    {
                        Console.WriteLine($"    [{i}] = RGB({palette[offset]}, {palette[offset+1]}, {palette[offset+2]})");
                    }
                    else if (componentsPerEntry == 1 && offset < palette.Length)
                    {
                        Console.WriteLine($"    [{i}] = Gray({palette[offset]})");
                    }
                }

                // Show entry 127 (0x7F) specifically since it appears most in the decoded data
                if (hival >= 127)
                {
                    int offset127 = 127 * componentsPerEntry;
                    Console.WriteLine($"\n  Key palette entry:");
                    if (componentsPerEntry == 3 && offset127 + 2 < palette.Length)
                    {
                        Console.WriteLine($"    [127 (0x7F)] = RGB({palette[offset127]}, {palette[offset127+1]}, {palette[offset127+2]})");
                    }
                    else if (componentsPerEntry == 1 && offset127 < palette.Length)
                    {
                        Console.WriteLine($"    [127 (0x7F)] = Gray({palette[offset127]})");
                    }
                }
            }

            // Check if there's an SMask (alpha channel)
            if (xObjStream.Dictionary.TryGetValue(new PdfName("SMask"), out PdfObject? smaskObj))
            {
                Console.WriteLine("\n  *** SMask (Alpha Channel) found! ***");

                // Resolve SMask reference
                if (smaskObj is PdfIndirectReference smaskRef && document != null)
                    smaskObj = document.ResolveReference(smaskRef);

                if (smaskObj is PdfStream smaskStream)
                {
                    Console.WriteLine($"  SMask is a stream with {smaskStream.Length} bytes");

                    // Show SMask dictionary
                    Console.WriteLine("\n  SMask Dictionary:");
                    foreach (var kvp in smaskStream.Dictionary)
                    {
                        string value = kvp.Value switch
                        {
                            PdfInteger i => i.Value.ToString(),
                            PdfName n => $"/{n.Value}",
                            PdfArray a => $"[{a.Count} items]",
                            _ => kvp.Value.ToString() ?? "null"
                        };
                        Console.WriteLine($"    /{kvp.Key.Value}: {value}");
                    }

                    // Extract SMask data
                    var smaskImage = new PdfImage(smaskStream, document);
                    Console.WriteLine($"\n  SMask: {smaskImage.Width}x{smaskImage.Height}, ColorSpace: {smaskImage.ColorSpace}, BPC: {smaskImage.BitsPerComponent}");

                    byte[] smaskData = smaskImage.GetDecodedData();
                    Console.WriteLine($"  SMask decoded: {smaskData.Length} bytes");

                    File.WriteAllBytes("PdfLibrary_SMask.bin", smaskData);
                    Console.WriteLine($"  ✓ Saved: PdfLibrary_SMask.bin");

                    // Show SMask byte distribution
                    Console.WriteLine("\n  SMask byte distribution (first 1000 bytes):");
                    AnalyzeByteDistribution(smaskData, Math.Min(1000, smaskData.Length));
                }
            }

            // Save the raw compressed data for external verification
            byte[] compressedData = image.GetEncodedData();
            File.WriteAllBytes("PdfLibrary_CompressedImage.bin", compressedData);
            Console.WriteLine($"\n✓ Saved compressed data: {compressedData.Length} bytes");

            return image.GetDecodedData();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
            return null;
        }
    }

    private static void ShowHex(byte[] data, int count)
    {
        count = Math.Min(count, data.Length);
        for (int i = 0; i < count; i++)
        {
            if (i % 16 == 0)
                Console.Write($"{i:X4}: ");

            Console.Write($"{data[i]:X2} ");

            if ((i + 1) % 16 == 0)
                Console.WriteLine();
        }
        Console.WriteLine();
    }

    private static void AnalyzeByteDistribution(byte[] data, int count)
    {
        count = Math.Min(count, data.Length);
        var histogram = new Dictionary<byte, int>();

        for (int i = 0; i < count; i++)
        {
            byte b = data[i];
            histogram[b] = histogram.GetValueOrDefault(b, 0) + 1;
        }

        var topValues = histogram.OrderByDescending(kvp => kvp.Value).Take(10);

        Console.WriteLine("Top 10 byte values:");
        foreach (var kvp in topValues)
        {
            Console.WriteLine($"  {kvp.Key,3} (0x{kvp.Key:X2}): {kvp.Value,4} occurrences");
        }
    }
}
