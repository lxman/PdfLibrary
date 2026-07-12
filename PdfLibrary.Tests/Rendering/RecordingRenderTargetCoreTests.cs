using PdfLibrary.Content;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

public class RecordingRenderTargetCoreTests
{
    [Fact]
    public void FillPath_records_one_command_with_a_frozen_state_snapshot()
    {
        var rec = new RecordingRenderTarget(document: null);
        var state = new PdfGraphicsState { ResolvedFillColorSpace = "DeviceRGB", ResolvedFillColor = [1.0, 0.0, 0.0] };
        var path = new PathBuilder();
        path.MoveTo(0, 0);
        path.LineTo(10, 0);
        path.LineTo(10, 10);
        path.ClosePath();

        rec.FillPath(path, state, evenOdd: false);
        // Mutating the ORIGINAL state after recording must not affect the recorded snapshot.
        state.ResolvedFillColor = [0.0, 1.0, 0.0];

        PageDrawList list = rec.TakeSnapshot();
        var fill = Assert.IsType<FillCommand>(Assert.Single(list.Commands));
        Assert.Equal(new[] { 1.0, 0.0, 0.0 }, fill.State.ResolvedFillColor);
    }
}
