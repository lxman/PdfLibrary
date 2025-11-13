using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Examples;

/// <summary>
/// Example demonstrating basic PDF page access
/// </summary>
class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("PdfLibrary - Page Access Example");
        Console.WriteLine("=================================\n");

        // Check if PDF path was provided
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: PdfLibrary.Examples <path-to-pdf-file>");
            Console.WriteLine("\nExample:");
            Console.WriteLine("  PdfLibrary.Examples document.pdf");
            return;
        }

        string pdfPath = args[0];

        if (!File.Exists(pdfPath))
        {
            Console.WriteLine($"Error: File not found: {pdfPath}");
            return;
        }

        try
        {
            // Load the PDF document
            Console.WriteLine($"Loading PDF: {pdfPath}");
            using PdfDocument document = PdfDocument.Load(pdfPath);

            // Display document information
            Console.WriteLine($"PDF Version: {document.Version}");
            Console.WriteLine($"Total Objects: {document.Objects.Count}");
            Console.WriteLine($"XRef Entries: {document.XrefTable.Count}\n");

            // Get catalog information
            PdfCatalog? catalog = document.GetCatalog();
            if (catalog != null)
            {
                Console.WriteLine("Document Catalog:");
                Console.WriteLine($"  Page Layout: {catalog.PageLayout ?? "Not specified"}");
                Console.WriteLine($"  Page Mode: {catalog.PageMode ?? "Not specified"}");
                Console.WriteLine($"  Language: {catalog.Language ?? "Not specified"}\n");
            }

            // Get page count
            int pageCount = document.GetPageCount();
            Console.WriteLine($"Total Pages: {pageCount}\n");

            // Display information for each page
            List<PdfPage> pages = document.GetPages();
            for (var i = 0; i < Math.Min(pages.Count, 5); i++) // Show first 5 pages
            {
                PdfPage page = pages[i];
                Console.WriteLine($"Page {i + 1}:");
                Console.WriteLine($"  Size: {page.Width:F2} x {page.Height:F2} points");
                Console.WriteLine($"  Rotation: {page.Rotate}°");

                PdfRectangle mediaBox = page.GetMediaBox();
                Console.WriteLine($"  MediaBox: {mediaBox}");

                PdfRectangle cropBox = page.GetCropBox();
                Console.WriteLine($"  CropBox: {cropBox}");

                // Get page resources
                PdfResources? resources = page.GetResources();
                if (resources != null)
                {
                    List<string> fontNames = resources.GetFontNames();
                    if (fontNames.Count > 0)
                        Console.WriteLine($"  Fonts: {string.Join(", ", fontNames)}");

                    List<string> xobjectNames = resources.GetXObjectNames();
                    if (xobjectNames.Count > 0)
                        Console.WriteLine($"  XObjects: {string.Join(", ", xobjectNames)}");
                }

                // Get content streams
                List<PdfStream> contents = page.GetContents();
                Console.WriteLine($"  Content Streams: {contents.Count}");
                foreach (PdfStream stream in contents)
                {
                    Console.WriteLine($"    - Length: {stream.Length} bytes");

                    // Check if compressed
                    if (!stream.Dictionary.TryGetValue(new PdfName("Filter"), out PdfObject filter))
                        continue;
                    string filterName = filter switch
                    {
                        PdfName name => name.Value,
                        PdfArray { Count: > 0 } array when array[0] is PdfName name => name.Value,
                        _ => "Unknown"
                    };
                    Console.WriteLine($"      Filter: {filterName}");
                }

                Console.WriteLine();
            }

            if (pages.Count > 5)
            {
                Console.WriteLine($"... and {pages.Count - 5} more pages");
            }

            // Demonstrate text extraction from first page
            if (pages.Count > 0)
            {
                Console.WriteLine("\n=== Text Extraction Example ===");
                Console.WriteLine("Extracting text from first page...\n");

                try
                {
                    PdfPage firstPage = pages[0];
                    string text = firstPage.ExtractText();

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        // Display first 500 characters of extracted text
                        string preview = text.Length > 500 ? text[..500] + "..." : text;
                        Console.WriteLine("Extracted Text:");
                        Console.WriteLine(new string('-', 60));
                        Console.WriteLine(preview);
                        Console.WriteLine(new string('-', 60));
                        Console.WriteLine($"\nTotal characters extracted: {text.Length}");

                        // Extract with fragments to show positioning
                        (_, List<TextFragment> fragments) = firstPage.ExtractTextWithFragments();
                        Console.WriteLine($"Total text fragments: {fragments.Count}");

                        if (fragments.Count > 0)
                        {
                            Console.WriteLine("\nFirst 3 text fragments with positions:");
                            for (var i = 0; i < Math.Min(3, fragments.Count); i++)
                            {
                                TextFragment fragment = fragments[i];
                                Console.WriteLine($"  {i + 1}. \"{fragment.Text}\"");
                                Console.WriteLine($"     Position: ({fragment.X:F2}, {fragment.Y:F2})");
                                Console.WriteLine($"     Font: {fragment.FontName ?? "Unknown"}, Size: {fragment.FontSize:F2}pt");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("No text found on first page (may contain only images).");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error extracting text: {ex.Message}");
                }
            }

            // Display document info
            PdfDictionary? info = document.GetInfo();
            if (info != null)
            {
                Console.WriteLine("\nDocument Information:");

                if (info.TryGetValue(new PdfName("Title"), out PdfObject title) && title is PdfString titleStr)
                    Console.WriteLine($"  Title: {titleStr}");

                if (info.TryGetValue(new PdfName("Author"), out PdfObject author) && author is PdfString authorStr)
                    Console.WriteLine($"  Author: {authorStr}");

                if (info.TryGetValue(new PdfName("Subject"), out PdfObject subject) && subject is PdfString subjectStr)
                    Console.WriteLine($"  Subject: {subjectStr}");

                if (info.TryGetValue(new PdfName("Creator"), out PdfObject creator) && creator is PdfString creatorStr)
                    Console.WriteLine($"  Creator: {creatorStr}");

                if (info.TryGetValue(new PdfName("Producer"), out PdfObject producer) && producer is PdfString producerStr)
                    Console.WriteLine($"  Producer: {producerStr}");
            }

            Console.WriteLine("\n✓ PDF loaded and analyzed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Error: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"  Details: {ex.InnerException.Message}");
        }
    }
}
