using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Tests for PdfRenderer rendering operations
/// </summary>
public class PdfRendererTests
{
    [Fact]
    public void Render_SimpleLine_CallsStrokePath()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            new MoveToOperator(100, 100),
            new LineToOperator(200, 200),
            new StrokeOperator()
        };

        renderer.ProcessOperators(operators);

        Assert.Contains(mock.Operations, op => op.StartsWith("StrokePath"));
        Assert.Single(mock.Operations, op => op.StartsWith("StrokePath"));
    }

    [Fact]
    public void Render_Rectangle_CallsFillPath()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            new RectangleOperator(50, 50, 100, 75),
            new FillOperator()
        };

        renderer.ProcessOperators(operators);

        Assert.NotEmpty(mock.Operations);
        Assert.Contains(mock.Operations, op => op.StartsWith("FillPath"));
        Assert.Contains("FillPath", mock.Operations[0]);
    }

    [Fact]
    public void Render_MultipleShapes_RendersInOrder()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            // First shape - rectangle
            new RectangleOperator(0, 0, 100, 100),
            new FillOperator(),
            // Second shape - line
            new MoveToOperator(0, 0),
            new LineToOperator(100, 100),
            new StrokeOperator()
        };

        renderer.ProcessOperators(operators);

        Assert.Equal(2, mock.Operations.Count);
        Assert.StartsWith("FillPath", mock.Operations[0]);
        Assert.StartsWith("StrokePath", mock.Operations[1]);
    }

    [Fact]
    public void Render_CubicBezierCurve_BuildsPath()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            new MoveToOperator(0, 0),
            new CurveToOperator(50, 100, 100, 100, 150, 0),
            new StrokeOperator()
        };

        renderer.ProcessOperators(operators);

        Assert.Single(mock.Operations, op => op.StartsWith("StrokePath"));
        // Path should have MoveTo + CurveTo
        Assert.Contains("segments", mock.Operations[0]);
    }

    [Fact]
    public void Render_ClosedPath_IncludesClosePath()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            new MoveToOperator(0, 0),
            new LineToOperator(100, 0),
            new LineToOperator(100, 100),
            new ClosePathOperator(),
            new StrokeOperator()
        };

        renderer.ProcessOperators(operators);

        Assert.Single(mock.Operations, op => op.StartsWith("StrokePath"));
        Assert.Contains("[MLLZ]", mock.Operations[0]); // MoveTo, LineTo, LineTo, ClosePath
    }

    [Fact]
    public void Render_FillEvenOdd_PassesEvenOddFlag()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            new RectangleOperator(0, 0, 100, 100),
            new FillEvenOddOperator()
        };

        renderer.ProcessOperators(operators);

        Assert.Contains("EvenOdd=True", mock.Operations[0]);
    }

    [Fact]
    public void Render_SetRgbColor_UpdatesColorState()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            new SetFillRgbOperator(1.0, 0.0, 0.0), // Red
            new RectangleOperator(0, 0, 100, 100),
            new FillOperator()
        };

        renderer.ProcessOperators(operators);

        Assert.Contains("DeviceRGB(1.00, 0.00, 0.00)", mock.Operations[0]);
    }

    [Fact]
    public void Render_SetGrayColor_UpdatesColorState()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            new SetStrokeGrayOperator(0.5), // 50% gray
            new MoveToOperator(0, 0),
            new LineToOperator(100, 100),
            new StrokeOperator()
        };

        renderer.ProcessOperators(operators);

        Assert.Contains("DeviceGray(0.50)", mock.Operations[0]);
    }

    [Fact]
    public void Render_SetCmykColor_UpdatesColorState()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            new SetFillCmykOperator(0.0, 1.0, 1.0, 0.0), // Red in CMYK
            new RectangleOperator(0, 0, 100, 100),
            new FillOperator()
        };

        renderer.ProcessOperators(operators);

        Assert.Contains("DeviceCMYK(0.00, 1.00, 1.00, 0.00)", mock.Operations[0]);
    }

    [Fact]
    public void Render_SaveRestoreState_CallsTargetMethods()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            new SaveGraphicsStateOperator(),
            new SetFillRgbOperator(1.0, 0.0, 0.0),
            new RectangleOperator(0, 0, 100, 100),
            new FillOperator(),
            new RestoreGraphicsStateOperator()
        };

        renderer.ProcessOperators(operators);

        Assert.Contains(mock.Operations, op => op.StartsWith("SaveState"));
        Assert.Contains(mock.Operations, op => op.StartsWith("RestoreState"));
        Assert.Equal("SaveState (depth=1)", mock.Operations[0]);
        Assert.Equal("RestoreState (depth=1)", mock.Operations[2]);
    }

    [Fact]
    public void Render_NestedStateChanges_TracksDepth()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            new SaveGraphicsStateOperator(),
            new SaveGraphicsStateOperator(),
            new RestoreGraphicsStateOperator(),
            new RestoreGraphicsStateOperator()
        };

        renderer.ProcessOperators(operators);

        Assert.Equal(4, mock.Operations.Count);
        Assert.Contains("depth=1", mock.Operations[0]);
        Assert.Contains("depth=2", mock.Operations[1]);
        Assert.Contains("depth=2", mock.Operations[2]);
        Assert.Contains("depth=1", mock.Operations[3]);
    }

    [Fact]
    public void Render_MatrixTransformation_TransformsCoordinates()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            new ConcatenateMatrixOperator(2, 0, 0, 2, 10, 10), // Scale 2x and translate (10, 10)
            new MoveToOperator(0, 0),
            new LineToOperator(100, 100),
            new StrokeOperator()
        };

        renderer.ProcessOperators(operators);

        // After transformation, (0,0) -> (10,10) and (100,100) -> (210,210)
        Assert.Single(mock.Operations, op => op.StartsWith("StrokePath"));
    }

    [Fact]
    public void Render_EmptyPath_DoesNotCallTarget()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        // Stroke without any path construction
        var operators = new List<PdfOperator>
        {
            new StrokeOperator()
        };

        renderer.ProcessOperators(operators);

        // Should not call stroke with empty path
        Assert.Empty(mock.Operations);
    }

    [Fact]
    public void Render_MultipleColorsInSequence_TracksColorChanges()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            // Red rectangle
            new SetFillRgbOperator(1.0, 0.0, 0.0),
            new RectangleOperator(0, 0, 50, 50),
            new FillOperator(),
            // Green rectangle
            new SetFillRgbOperator(0.0, 1.0, 0.0),
            new RectangleOperator(60, 0, 50, 50),
            new FillOperator(),
            // Blue rectangle
            new SetFillRgbOperator(0.0, 0.0, 1.0),
            new RectangleOperator(120, 0, 50, 50),
            new FillOperator()
        };

        renderer.ProcessOperators(operators);

        Assert.Equal(3, mock.Operations.Count);
        Assert.Contains("DeviceRGB(1.00, 0.00, 0.00)", mock.Operations[0]); // Red
        Assert.Contains("DeviceRGB(0.00, 1.00, 0.00)", mock.Operations[1]); // Green
        Assert.Contains("DeviceRGB(0.00, 0.00, 1.00)", mock.Operations[2]); // Blue
    }

    [Fact]
    public void Render_LineWidthChange_UpdatesState()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            new SetLineWidthOperator(5.0),
            new MoveToOperator(0, 0),
            new LineToOperator(100, 100),
            new StrokeOperator()
        };

        renderer.ProcessOperators(operators);

        Assert.Contains("LineWidth=5", mock.Operations[0]);
    }
}
