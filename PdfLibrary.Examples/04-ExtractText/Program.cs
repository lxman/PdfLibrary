using PdfLibrary.Builder;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Structure;

// ==================== Text extraction example ====================
// Reads the text back out of a PDF three ways:
//   • doc.ExtractAllText()             — the whole document as one string
//   • page.ExtractText()               — one page at a time
//   • page.ExtractTextWithFragments()  — text plus per-run position and font
//
// Usage:
//   dotnet run                 # extracts from a small sample built in memory
//   dotnet run -- <file.pdf>   # extracts from a file

Console.WriteLine("PdfLibrary — Text Extraction Example\n");

// ---- 1. Get the PDF bytes: a file argument, or a small in-memory sample ----
byte[] pdfBytes;
string source;
if (args.Length > 0 && File.Exists(args[0]))
{
    pdfBytes = File.ReadAllBytes(args[0]);
    source = args[0];
}
else
{
    if (args.Length > 0)
        Console.WriteLine($"(File '{args[0]}' not found — using the built-in sample instead.)\n");

    pdfBytes = BuildSample();
    source = "an in-memory sample document";
}

Console.WriteLine($"Extracting from {source}");

// PdfDocument owns the stream (leaveOpen: false) and closes it when disposed.
using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdfBytes));
Console.WriteLine($"Pages: {doc.PageCount}\n");

// ---- 2. Whole-document text ----
Console.WriteLine("== doc.ExtractAllText() (every page, joined) ==");
Console.WriteLine(Quote(doc.ExtractAllText()));
Console.WriteLine();

// ---- 3. Per-page text ----
for (int i = 0; i < doc.PageCount; i++)
{
    PdfPage page = doc.GetPage(i)!;
    Console.WriteLine($"== Page {i + 1} — page.ExtractText() ({page.Width:F0}×{page.Height:F0} pt) ==");
    Console.WriteLine(Quote(page.ExtractText()));
    Console.WriteLine();
}

// ---- 4. Text with positions and fonts (first page) ----
PdfPage first = doc.GetPage(0)!;
(string _, List<TextFragment> fragments) = first.ExtractTextWithFragments();
Console.WriteLine($"== Page 1 — page.ExtractTextWithFragments() ({fragments.Count} fragments) ==");
foreach (TextFragment f in fragments.Take(8))
    Console.WriteLine($"  '{f.Text}'  @ ({f.X:F1}, {f.Y:F1})  {f.FontName} {f.FontSize:F1}pt");
if (fragments.Count > 8)
    Console.WriteLine($"  … and {fragments.Count - 8} more.");

Console.WriteLine("\nDone!");
return;

// ---- helpers ----

// Indent multi-line extracted text so it stands out from the surrounding log.
static string Quote(string text) =>
    string.Join('\n', text.Replace("\r\n", "\n").Split('\n').Select(line => "  │ " + line));

static byte[] BuildSample() =>
    PdfDocumentBuilder.Create()
        .WithMetadata(m => m.SetTitle("Text Extraction Sample").SetAuthor("PdfLibrary Examples"))
        .AddPage(p =>
        {
            p.FromTopLeft();
            p.AddText("Chapter 1: Introduction", 72, 72, "Helvetica-Bold", 20);
            p.AddText("PdfLibrary reads text back out of a PDF with layout awareness.", 72, 112, "Helvetica", 12);
            p.AddText("Each line becomes one or more positioned fragments.", 72, 132, "Helvetica", 12);
        })
        .AddPage(p =>
        {
            p.FromTopLeft();
            p.AddText("Chapter 2: Details", 72, 72, "Helvetica-Bold", 20);
            p.AddText("The second page carries its own text.", 72, 112, "Helvetica", 12);
        })
        .ToByteArray();
