using PdfLibrary.Editing;

namespace PdfLibrary.Tests.Editing;

public class PdfSaveOptionsTests
{
    // Parity with PdfOptimizationOptions.Default.
    [Fact]
    public void Default_ReturnsOptionsWithStandardDefaults()
    {
        PdfSaveOptions options = PdfSaveOptions.Default;

        Assert.True(options.RemoveOrphans);
        Assert.False(options.UseObjectStreams);
    }
}
