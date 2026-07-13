using PdfLibrary.Content;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Content;

public class PdfGraphicsStateColorantOriginTests
{
    [Fact]
    public void Clone_PreservesColorantOrigins()
    {
        var state = new PdfGraphicsState
        {
            ResolvedFillColorantOrigin = new ColorantOrigin(new[] { "PANTONE 185 C" }, new[] { 0.5 }, "DeviceCMYK"),
            ResolvedStrokeColorantOrigin = new ColorantOrigin(new[] { "Spot1" }, new[] { 1.0 }, "DeviceCMYK"),
        };

        PdfGraphicsState clone = state.Clone();

        Assert.Same(state.ResolvedFillColorantOrigin, clone.ResolvedFillColorantOrigin);
        Assert.Same(state.ResolvedStrokeColorantOrigin, clone.ResolvedStrokeColorantOrigin);
    }

    [Fact]
    public void DefaultState_HasNullColorantOrigins()
    {
        var state = new PdfGraphicsState();
        Assert.Null(state.ResolvedFillColorantOrigin);
        Assert.Null(state.ResolvedStrokeColorantOrigin);
    }
}
