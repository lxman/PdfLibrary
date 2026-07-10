using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

// ==================== Merge example ====================
// Combines several PDFs into one with PdfDocumentEditor.Merge, which copies every page
// (and its resources) from each source, in order, into a brand-new document.
//
// Usage:
//   dotnet run                       # merges three samples built in memory
//   dotnet run -- a.pdf b.pdf ...    # merges the given files, in order

Console.WriteLine("PdfLibrary — Merge Example\n");

string outputDir = Path.Combine(Path.GetTempPath(), "pdflibrary-examples");
Directory.CreateDirectory(outputDir);
string outputPath = Path.Combine(outputDir, "merged.pdf");

// ---- 1. Gather the source documents ----
var sources = new List<PdfDocument>();
var labels = new List<string>();
try
{
    string[] files = args.Where(File.Exists).Select(Path.GetFullPath).ToArray();

    // A loaded source keeps its file open until disposed, and here the sources are still
    // open during Save (disposed in the finally). Saving over a still-open input fails on
    // some platforms, so refuse it — merge to a path that isn't one of the inputs.
    if (files.Any(f => string.Equals(f, Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine($"One of the inputs is the merge target itself:\n  {outputPath}");
        Console.WriteLine("Pass inputs other than the output path (or delete it first).");
        return;
    }

    if (files.Length > 0)
    {
        foreach (string file in files)
        {
            sources.Add(PdfDocument.Load(file));
            labels.Add(file);
        }
    }
    else
    {
        if (args.Length > 0)
            Console.WriteLine("(None of the given files exist — merging three built-in samples instead.)\n");

        sources.Add(PdfDocument.Load(new MemoryStream(BuildSample("Document A", 2)))); labels.Add("sample A");
        sources.Add(PdfDocument.Load(new MemoryStream(BuildSample("Document B", 1)))); labels.Add("sample B");
        sources.Add(PdfDocument.Load(new MemoryStream(BuildSample("Document C", 3)))); labels.Add("sample C");
    }

    Console.WriteLine("Merging, in order:");
    foreach ((string label, PdfDocument doc) in labels.Zip(sources))
        Console.WriteLine($"  • {label}  ({doc.PageCount} page(s))");
    Console.WriteLine();

    // ---- 2. Merge into a new document and save it ----
    using PdfDocument merged = PdfDocumentEditor.Merge(sources);
    merged.Save(outputPath);

    int expected = sources.Sum(d => d.PageCount);
    Console.WriteLine($"Merged {sources.Count} documents into: {outputPath}");
    Console.WriteLine($"  Pages: {merged.PageCount} (expected {expected})");
}
finally
{
    foreach (PdfDocument doc in sources)
        doc.Dispose();
}

Console.WriteLine("\nDone!");
return;

// ---- helpers ----

static byte[] BuildSample(string title, int pages)
{
    PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
        .WithMetadata(m => m.SetTitle(title));

    for (int i = 1; i <= pages; i++)
    {
        int pageNumber = i;
        builder.AddPage(p =>
        {
            p.FromTopLeft();
            p.AddText(title, 72, 80, "Helvetica-Bold", 28);
            p.AddText($"Page {pageNumber} of {pages}", 72, 124, "Helvetica", 14);
        });
    }

    return builder.ToByteArray();
}
