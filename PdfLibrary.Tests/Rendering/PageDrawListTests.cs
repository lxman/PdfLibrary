using PdfLibrary.Content;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

public class PageDrawListTests
{
    [Fact]
    public void FillCommand_carries_segments_state_and_rule()
    {
        var state = new PdfGraphicsState { ResolvedFillColorSpace = "DeviceRGB", ResolvedFillColor = [1.0, 0.0, 0.0] };
        var segs = new PathSegment[] { new MoveToSegment(0, 0), new LineToSegment(10, 0), new ClosePathSegment() };

        var cmd = new FillCommand(segs, EvenOdd: false, state);

        Assert.Equal(3, cmd.Segments.Count);
        Assert.False(cmd.EvenOdd);
        Assert.Same(state, cmd.State);
    }

    [Fact]
    public void PageDrawList_holds_begin_and_commands()
    {
        var list = new PageDrawList(
            new BeginPageArgs(1, 200, 100, 1.0, 0, 0, 0),
            new DrawCommand[] { new SaveCommand(), new RestoreCommand() });

        Assert.Equal(200, list.Begin.Width);
        Assert.Equal(2, list.Commands.Count);
    }
}
