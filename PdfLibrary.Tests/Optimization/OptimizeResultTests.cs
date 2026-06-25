using PdfLibrary.Builder;
using PdfLibrary.Optimization;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Optimization;

public class OptimizeResultTests
{
    [Fact]
    public void Optimize_ReturnsResult_WithObjectAndSizeStats()
    {
        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("optimize-stats", 100, 700))
            .ToByteArray();
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));

        using var output = new MemoryStream();
        PdfOptimizationResult result = PdfOptimizer.Optimize(doc, output);

        Assert.True(result.ObjectsBefore > 0, "ObjectsBefore should be positive");
        Assert.True(result.ObjectsAfter > 0, "ObjectsAfter should be positive");
        Assert.True(result.ObjectsRemoved >= 0, "ObjectsRemoved should be non-negative");
        Assert.True(result.ObjectsAfter <= result.ObjectsBefore, "ObjectsAfter cannot exceed ObjectsBefore");
        Assert.Equal(output.Length, result.OutputBytes);
    }
}
