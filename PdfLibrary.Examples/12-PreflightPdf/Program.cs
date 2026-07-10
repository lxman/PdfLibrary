using PdfLibrary.Builder;
using PdfLibrary.Conformance;

// ==================== Conformance preflight example ====================
// Validates a PDF against an ISO standard — PDF/A (archival), PDF/X-4 (print), or
// PDF/UA-1 (accessibility) — WITHOUT modifying the document, and prints the findings.
//
// Usage:
//   dotnet run                        # preflights a small sample PDF built in memory
//   dotnet run -- <file.pdf>          # preflights a file against PDF/A-2b
//   dotnet run -- <file.pdf> PdfUA1   # ... against a named profile
//
// Valid profiles: PdfA2b, PdfA2u, PdfA3b (archival), PdfX4 (print), PdfUA1 (accessibility).

Console.WriteLine("PdfLibrary — Conformance Preflight Example\n");

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

    // A plain builder document: deliberately NOT PDF/A-conformant (no XMP metadata, no
    // output intent, a non-embedded standard-14 font) so the preflight has real findings.
    pdfBytes = PdfDocumentBuilder.Create()
        .AddPage(p => p.AddText("Hello, preflight!", 72, 700, "Helvetica", 24))
        .ToByteArray();
    source = "an in-memory sample document";
}

// ---- 2. Choose the profile (second argument, or default to PDF/A-2b) ----
ConformanceProfile[] valid =
[
    ConformanceProfile.PdfA2b, ConformanceProfile.PdfA2u, ConformanceProfile.PdfA3b,
    ConformanceProfile.PdfX4, ConformanceProfile.PdfUA1,
];

ConformanceProfile profile = ConformanceProfile.PdfA2b;
if (args.Length > 1 && (!Enum.TryParse(args[1], ignoreCase: true, out profile) || !valid.Contains(profile)))
{
    Console.WriteLine($"Unknown profile '{args[1]}'. Valid profiles: {string.Join(", ", valid)}.");
    return;
}

// ---- 3. Run the read-only preflight ----
Console.WriteLine($"Preflighting {source}");
Console.WriteLine($"Profile:     {profile}\n");

// Check also has PdfDocument and file-path overloads; the byte[] one is shown here.
PreflightResult result = Preflighter.Check(pdfBytes, profile);

// ---- 4. Report the findings ----
if (result.Conforms)
{
    Console.WriteLine("Result: CONFORMS — no violations among the checked rules.");
}
else
{
    int errors = result.Errors.Count();
    int warnings = result.Warnings.Count();
    Console.WriteLine($"Result: DOES NOT CONFORM — {errors} error(s), {warnings} warning(s).\n");

    foreach (Finding f in result.Findings.OrderBy(f => f.Severity)) // Error, then Warning, then Info
    {
        string where = f.PageIndex is { } page ? $" (page {page + 1})"
            : f.ObjectNumber is { } obj ? $" (object {obj})"
            : string.Empty;
        Console.WriteLine($"  [{f.Severity}] {f.Clause}{where}");
        Console.WriteLine($"      {f.Message}");
    }
}

Console.WriteLine();
Console.WriteLine("A \"conforms\" result means no violations among the CHECKED rules — a deliberately");
Console.WriteLine("partial, machine-decidable subset of each standard, not a certification.");
