using System.IO;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Shared "known-conformant" fixture for the preflight tests. PdfLibrary's builder cannot yet emit a
/// font-embedding-conformant PDF/A (it always writes an unused, non-embedded Helvetica and no XMP —
/// that is write-side / conversion-phase work), so a genuinely conformant document is provided as a
/// vendored file: a 3.3 KB PDF/A-2b file from the veraPDF corpus (CC BY 4.0; see
/// Resources/conformant-pdfa2b.LICENSE.txt). It passes the full preflight rule set with zero errors.
/// </summary>
internal static class ConformanceFixtures
{
    private static string ConformantPath =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "conformant-pdfa2b.pdf");

    /// <summary>Bytes of a known-conformant PDF/A-2b document.</summary>
    public static byte[] CleanConformantBytes() => File.ReadAllBytes(ConformantPath);

    /// <summary>A loaded, known-conformant PDF/A-2b document.</summary>
    public static PdfDocument CleanConformantDoc() => PdfDocument.Load(new MemoryStream(CleanConformantBytes()));
}
