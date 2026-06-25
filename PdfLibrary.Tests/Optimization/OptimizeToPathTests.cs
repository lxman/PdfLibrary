using PdfLibrary.Builder;
using PdfLibrary.Optimization;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Optimization;

public class OptimizeToPathTests
{
    // Parity with PdfDocument.Save(string): Optimize should accept an output file path,
    // not only a Stream.
    [Fact]
    public void Optimize_ToFilePath_WritesReloadablePdf()
    {
        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("optimize-to-path", 100, 700))
            .ToByteArray();
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));

        string outPath = Path.Combine(Path.GetTempPath(), $"opt-{Guid.NewGuid():N}.pdf");
        try
        {
            PdfOptimizer.Optimize(doc, outPath);

            Assert.True(File.Exists(outPath));
            using PdfDocument reloaded = PdfDocument.Load(outPath);
            Assert.Equal(1, reloaded.PageCount);
        }
        finally
        {
            if (File.Exists(outPath))
                File.Delete(outPath);
        }
    }
}
