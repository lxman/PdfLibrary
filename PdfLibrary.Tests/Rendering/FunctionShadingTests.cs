using System.Numerics;
using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Function-based (type 1) shadings, ISO 32000-2 §8.7.4.5.3. A type 1 shading evaluates a 2-in/n-out
/// function over a rectangular /Domain and maps that domain into the shading's target space with
/// /Matrix — it shares nothing with the axial/radial ramp or the mesh stream decoders. Previously
/// <c>ShadingBuilder</c> returned null for it, so such a shading painted NOTHING.
///
/// <para>
/// The implementation tessellates the domain into a Gouraud triangle grid, which is why these tests
/// assert on <see cref="ShadingDescriptor.MeshTriangles"/>: the existing triangle paint path (Skia's
/// <c>DrawVertices</c>, and the CMYK compositor's mesh arm) then renders it with no backend change.
/// </para>
///
/// <para>
/// Colour fixtures use a type 4 (PostScript calculator) function because it is the only function type
/// that takes two inputs and can be written by hand: with the program <c>{ 0 }</c> the stack ends as
/// <c>x y 0</c>, and the top three values are the outputs — so R = x, G = y, B = 0 across the domain.
/// </para>
/// </summary>
public class FunctionShadingTests
{
    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    /// <summary>A type 4 function stream with the given program, /Domain and /Range.</summary>
    private static PdfStream PostScript(string program, PdfArray domain, PdfArray range)
    {
        var dict = new PdfDictionary();
        dict.Add(new PdfName("FunctionType"), new PdfInteger(4));
        dict.Add(new PdfName("Domain"), domain);
        dict.Add(new PdfName("Range"), range);
        return new PdfStream(dict, Encoding.Latin1.GetBytes(program));
    }

    /// <summary>R = x, G = y, B = 0 over the unit square, in DeviceRGB.</summary>
    private static PdfStream XyRgbFunction() =>
        PostScript("{ 0 }", Reals(0, 1, 0, 1), Reals(0, 1, 0, 1, 0, 1));

    private static PdfDictionary ShadingDict(PdfObject? function, double[]? domain = null,
        double[]? matrix = null, string colorSpace = "DeviceRGB")
    {
        var dict = new PdfDictionary();
        dict.Add(new PdfName("ShadingType"), new PdfInteger(1));
        dict.Add(new PdfName("ColorSpace"), new PdfName(colorSpace));
        if (function is not null) dict.Add(new PdfName("Function"), function);
        if (domain is not null) dict.Add(new PdfName("Domain"), Reals(domain));
        if (matrix is not null) dict.Add(new PdfName("Matrix"), Reals(matrix));
        return dict;
    }

    private static bool HasVertex(MeshVertex[] tris, float x, float y) =>
        Array.Exists(tris, v => MathF.Abs(v.X - x) < 1e-3f && MathF.Abs(v.Y - y) < 1e-3f);

    private static MeshVertex VertexAt(MeshVertex[] tris, float x, float y)
    {
        int i = Array.FindIndex(tris, v => MathF.Abs(v.X - x) < 1e-3f && MathF.Abs(v.Y - y) < 1e-3f);
        Assert.True(i >= 0, $"no vertex at ({x}, {y})");
        return tris[i];
    }

    [Fact]
    public void Type1_UnitDomain_TessellatesIntoTrianglesSpanningTheDomain()
    {
        ShadingDescriptor? d = ShadingBuilder.Build(ShadingDict(XyRgbFunction()), null);

        Assert.NotNull(d);
        Assert.Equal(1, d!.ShadingType);
        Assert.NotEmpty(d.MeshTriangles);
        Assert.Equal(0, d.MeshTriangles.Length % 3);            // whole triangles
        Assert.All(d.MeshTriangles, v =>
        {
            Assert.InRange(v.X, 0f, 1f);
            Assert.InRange(v.Y, 0f, 1f);
        });
        // The default /Domain is the unit square (Table 78) and every corner is a grid vertex.
        Assert.True(HasVertex(d.MeshTriangles, 0f, 0f));
        Assert.True(HasVertex(d.MeshTriangles, 1f, 1f));
    }

    [Fact]
    public void Type1_Matrix_MapsDomainIntoTargetSpace()
    {
        // [100 0 0 50 10 20]: scale x by 100, y by 50, then translate — the domain's (0,0)..(1,1)
        // corners land at (10,20)..(110,70) in the shading's target space.
        ShadingDescriptor? d = ShadingBuilder.Build(
            ShadingDict(XyRgbFunction(), matrix: [100, 0, 0, 50, 10, 20]), null);

        Assert.NotNull(d);
        Assert.True(HasVertex(d!.MeshTriangles, 10f, 20f));
        Assert.True(HasVertex(d.MeshTriangles, 110f, 70f));
        Assert.All(d.MeshTriangles, v =>
        {
            Assert.InRange(v.X, 10f, 110f);
            Assert.InRange(v.Y, 20f, 70f);
        });
    }

    [Fact]
    public void Type1_ColourAtVertex_IsTheFunctionEvaluatedAtThatDomainPoint()
    {
        ShadingDescriptor? d = ShadingBuilder.Build(ShadingDict(XyRgbFunction()), null);

        Assert.NotNull(d);
        // R = x, G = y, B = 0.
        Assert.Equal(0xFF000000, VertexAt(d!.MeshTriangles, 0f, 0f).Rgb);
        Assert.Equal(0xFFFF0000, VertexAt(d.MeshTriangles, 1f, 0f).Rgb);
        Assert.Equal(0xFF00FF00, VertexAt(d.MeshTriangles, 0f, 1f).Rgb);
        Assert.Equal(0xFFFFFF00, VertexAt(d.MeshTriangles, 1f, 1f).Rgb);
    }

    [Fact]
    public void Type1_NonUnitDomain_EvaluatesTheFunctionOverThatDomain()
    {
        // Domain x∈[0,2], y∈[0,1]; the function clamps its own /Domain at 1, so the right-hand edge
        // saturates to R=255 — the point is that the SHADING's domain drives the sample positions.
        PdfStream fn = PostScript("{ 0 }", Reals(0, 2, 0, 1), Reals(0, 1, 0, 1, 0, 1));
        ShadingDescriptor? d = ShadingBuilder.Build(ShadingDict(fn, domain: [0, 2, 0, 1]), null);

        Assert.NotNull(d);
        Assert.True(HasVertex(d!.MeshTriangles, 2f, 1f));
        // R = x clamped to the function's /Range [0,1] ⇒ 255 at x = 2.
        Assert.Equal(0xFFFFFF00, VertexAt(d.MeshTriangles, 2f, 1f).Rgb);
        Assert.Equal(0xFF000000, VertexAt(d.MeshTriangles, 0f, 0f).Rgb);
    }

    [Fact]
    public void Type1_ArrayOfSingleOutputFunctions_MapsOneFunctionPerComponent()
    {
        // n 2-in/1-out functions (Table 78's alternative form): R = x, G = y, B = 1.
        PdfArray one = Reals(0, 1, 0, 1);
        PdfArray unit = Reals(0, 1);
        var fns = new PdfArray([
            PostScript("{ pop }", one, unit),
            PostScript("{ exch pop }", one, unit),
            PostScript("{ pop pop 1 }", one, unit)
        ]);

        ShadingDescriptor? d = ShadingBuilder.Build(ShadingDict(fns), null);

        Assert.NotNull(d);
        Assert.Equal(0xFF0000FF, VertexAt(d!.MeshTriangles, 0f, 0f).Rgb);
        Assert.Equal(0xFFFFFFFF, VertexAt(d.MeshTriangles, 1f, 1f).Rgb);
    }

    [Fact]
    public void Type1_CmykColourSpace_CarriesNativeInk()
    {
        // { 0 0 } ⇒ stack x y 0 0, top four = (x, y, 0, 0) as C, M, Y, K.
        PdfStream fn = PostScript("{ 0 0 }", Reals(0, 1, 0, 1), Reals(0, 1, 0, 1, 0, 1, 0, 1));
        ShadingDescriptor? d = ShadingBuilder.Build(
            ShadingDict(fn, colorSpace: "DeviceCMYK"), null);

        Assert.NotNull(d);
        Assert.True(d!.MeshHasCmyk);
        Assert.Equal(0xFFFF0000u, VertexAt(d.MeshTriangles, 1f, 1f).Cmyk);   // C=255, M=255, Y=K=0
    }

    [Fact]
    public void Type1_PatternMatrix_IsCarriedThroughUntouched()
    {
        Matrix3x2 pattern = Matrix3x2.CreateTranslation(7f, 9f);
        ShadingDescriptor? d = ShadingBuilder.Build(ShadingDict(XyRgbFunction()), null, pattern);

        Assert.NotNull(d);
        Assert.Equal(pattern, d!.PatternMatrix);
        // /Matrix is absent, so the domain is NOT pre-transformed by the pattern matrix — the
        // consumer applies that itself, exactly as for types 2/3/6/7.
        Assert.True(HasVertex(d.MeshTriangles, 0f, 0f));
    }

    [Fact]
    public void Type1_MissingFunction_ReturnsNull()
    {
        Assert.Null(ShadingBuilder.Build(ShadingDict(null), null));
    }

    [Fact]
    public void Type1_UnevaluableFunction_ReturnsNull()
    {
        // FunctionType 5 does not exist; PdfFunction.Create declines it.
        var bad = new PdfDictionary();
        bad.Add(new PdfName("FunctionType"), new PdfInteger(5));
        Assert.Null(ShadingBuilder.Build(ShadingDict(bad), null));
    }

    [Theory]
    [InlineData(new double[] { 1, 1, 0, 1 })]        // zero-width domain
    [InlineData(new double[] { 0, 1, 2, 2 })]        // zero-height domain
    [InlineData(new double[] { 1, 0, 0, 1 })]        // reversed x
    [InlineData(new double[] { 0, 1, 0 })]           // too few entries
    public void Type1_DegenerateDomain_ReturnsNull(double[] domain)
    {
        Assert.Null(ShadingBuilder.Build(ShadingDict(XyRgbFunction(), domain: domain), null));
    }
}
