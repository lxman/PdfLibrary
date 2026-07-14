using PdfLibrary.Content;
using PdfLibrary.Content.Operators;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Render-integration check for Soft-Proof SP-1 Task 2: driving a fill through <see cref="PdfRenderer"/>
/// populates <see cref="PdfGraphicsState.ResolvedFillColorantOrigin"/> from the named Separation colour
/// space, and a device-space (DeviceCMYK) fill leaves it null. Mirrors the operator-list render harness
/// used by <c>PdfRendererTests</c> (new PdfRenderer(mock, resources) + ProcessOperators) and the
/// dictionary-building helpers in <c>OverprintPlatesTests</c>.
/// </summary>
public class ColorantOriginRenderTests
{
    // Type 2 exponential tint transform; values are irrelevant to colorant-origin resolution (which only
    // reads the colorant name + alternate space), but a well-formed function keeps the array realistic.
    private static PdfDictionary Type2(double[] c0, double[] c1)
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(2));
        d.Add(new PdfName("Domain"), Reals(0, 1));
        d.Add(new PdfName("C0"), Reals(c0));
        d.Add(new PdfName("C1"), Reals(c1));
        d.Add(new PdfName("N"), new PdfReal(1));
        return d;
    }

    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    // Builds a /Resources dictionary whose /ColorSpace sub-dictionary maps "CS0" to
    // [/Separation /PANTONE 185 C /DeviceCMYK <tint fn>].
    private static PdfResources SeparationResources()
    {
        var sep = new PdfArray(
            new PdfName("Separation"), new PdfName("PANTONE 185 C"), new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [0, 1, 1, 0]));

        var colorSpaces = new PdfDictionary();
        colorSpaces.Add(new PdfName("CS0"), sep);

        var resourcesDict = new PdfDictionary();
        resourcesDict.Add(new PdfName("ColorSpace"), colorSpaces);

        return new PdfResources(resourcesDict);
    }

    [Fact]
    public void SeparationFill_PopulatesResolvedFillColorantOrigin()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock, SeparationResources());

        var operators = new List<PdfOperator>
        {
            new SetFillColorSpaceOperator(new PdfName("CS0")),
            new SetFillColorExtendedOperator([new PdfReal(0.5)]),
            new RectangleOperator(0, 0, 10, 10),
            new FillOperator(),
        };

        renderer.ProcessOperators(operators);

        Assert.NotNull(mock.LastFillState);
        ColorantOrigin? origin = mock.LastFillState!.ResolvedFillColorantOrigin;
        Assert.NotNull(origin);
        Assert.Equal(["PANTONE 185 C"], origin!.Names);
        Assert.Equal("DeviceCMYK", origin.AlternateSpace);
    }

    [Fact]
    public void DeviceCmykFill_LeavesResolvedFillColorantOriginNull()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock);

        var operators = new List<PdfOperator>
        {
            new SetFillCmykOperator(0, 0, 0, 1),
            new RectangleOperator(0, 0, 10, 10),
            new FillOperator(),
        };

        renderer.ProcessOperators(operators);

        Assert.NotNull(mock.LastFillState);
        Assert.Null(mock.LastFillState!.ResolvedFillColorantOrigin);
    }

    // Stroke-side counterpart of SeparationFill_PopulatesResolvedFillColorantOrigin: PdfRenderer.OnColorChanged
    // sets ResolvedStrokeColorantOrigin right after ResolvedFillColorantOrigin, but only the fill half had
    // render-integration coverage. Drives a /Separation STROKE (CS/SCN on the stroke operators + "S") and
    // asserts the stroke-side origin resolves the same way the fill-side one does.
    [Fact]
    public void SeparationStroke_PopulatesResolvedStrokeColorantOrigin()
    {
        var mock = new MockRenderTarget();
        var renderer = new PdfRenderer(mock, SeparationResources());

        var operators = new List<PdfOperator>
        {
            new SetStrokeColorSpaceOperator(new PdfName("CS0")),
            new SetStrokeColorExtendedOperator([new PdfReal(0.5)]),
            new RectangleOperator(0, 0, 10, 10),
            new StrokeOperator(),
        };

        renderer.ProcessOperators(operators);

        Assert.NotNull(mock.LastStrokeState);
        ColorantOrigin? origin = mock.LastStrokeState!.ResolvedStrokeColorantOrigin;
        Assert.NotNull(origin);
        Assert.Equal(["PANTONE 185 C"], origin!.Names);
        Assert.Equal("DeviceCMYK", origin.AlternateSpace);
    }
}
