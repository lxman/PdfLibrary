using System.Collections.Generic;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Decode/topology checks for Gouraud triangle meshes: type 4 (free-form, ISO 32000-2 §8.7.4.5.5) and
/// type 5 (lattice-form, §8.7.4.5.6). Both were previously unimplemented — <c>ShadingBuilder</c>
/// returned null, so such a shading painted NOTHING.
///
/// <para>
/// Fixtures are hand-built streams rather than corpus files: neither veraPDF nor GWG contains a type
/// 4/5 shading (they are prepress/conformance suites, and Gouraud meshes come from technical vector
/// output), and the sample files that do exist carry licence terms unsuitable for vendoring. Building
/// the bytes here also pins the exact bit layout, which is the part most easily got wrong.
/// </para>
///
/// <para>
/// Colours use DeviceRGB so the expected packed value is simply <c>0xFFRRGGBB</c>. Coordinates decode
/// to [0,100] from 8-bit raws, so raw 0 → 0.0 and raw 255 → 100.0.
/// </para>
/// </summary>
public class GouraudMeshShadingTests
{
    private const uint Red = 0xFFFF0000, Green = 0xFF00FF00, Blue = 0xFF0000FF, White = 0xFFFFFFFF;

    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    private static PdfDictionary MeshDict(int shadingType, int bitsPerComponent = 8, int? verticesPerRow = null)
    {
        var dict = new PdfDictionary();
        dict.Add(new PdfName("ShadingType"), new PdfInteger(shadingType));
        dict.Add(new PdfName("ColorSpace"), new PdfName("DeviceRGB"));
        dict.Add(new PdfName("BitsPerCoordinate"), new PdfInteger(8));
        dict.Add(new PdfName("BitsPerComponent"), new PdfInteger(bitsPerComponent));
        dict.Add(new PdfName("BitsPerFlag"), new PdfInteger(8));
        dict.Add(new PdfName("Decode"), Reals(0, 100, 0, 100, 0, 1, 0, 1, 0, 1));
        if (verticesPerRow is { } k) dict.Add(new PdfName("VerticesPerRow"), new PdfInteger(k));
        return dict;
    }

    /// <summary>One type-4 vertex: flag, x, y, r, g, b — 6 bytes, already a whole number of bytes.</summary>
    private static void Vertex4(List<byte> d, byte flag, byte x, byte y, byte r, byte g, byte b)
    {
        d.Add(flag); d.Add(x); d.Add(y); d.Add(r); d.Add(g); d.Add(b);
    }

    /// <summary>One type-5 vertex: x, y, r, g, b — no edge flag (§8.7.4.5.6).</summary>
    private static void Vertex5(List<byte> d, byte x, byte y, byte r, byte g, byte b)
    {
        d.Add(x); d.Add(y); d.Add(r); d.Add(g); d.Add(b);
    }

    private static MeshVertex[] Build(int shadingType, List<byte> data, PdfDictionary dict)
    {
        var stream = new PdfStream(dict, data.ToArray());
        ShadingDescriptor? desc = MeshShadingReader.Build(stream, dict, shadingType, null, null);
        Assert.NotNull(desc);
        Assert.NotNull(desc!.MeshTriangles);
        return desc.MeshTriangles!;
    }

    [Fact]
    public void Type4_SingleFlagZeroTriangle_EmitsOneTriangleWithDecodedVerticesAndColours()
    {
        var d = new List<byte>();
        Vertex4(d, 0, 0, 0, 255, 0, 0);       // va (0,0) red
        Vertex4(d, 0, 255, 0, 0, 255, 0);     // vb (100,0) green
        Vertex4(d, 0, 0, 255, 0, 0, 255);     // vc (0,100) blue

        MeshVertex[] t = Build(4, d, MeshDict(4));

        Assert.Equal(3, t.Length);
        Assert.Equal((0f, 0f, Red), (t[0].X, t[0].Y, t[0].Rgb));
        Assert.Equal((100f, 0f, Green), (t[1].X, t[1].Y, t[1].Rgb));
        Assert.Equal((0f, 100f, Blue), (t[2].X, t[2].Y, t[2].Rgb));
    }

    [Fact]
    public void Type4_EdgeFlag1_FormsTriangleFromPreviousBAndC()
    {
        var d = new List<byte>();
        Vertex4(d, 0, 0, 0, 255, 0, 0);       // va
        Vertex4(d, 0, 255, 0, 0, 255, 0);     // vb
        Vertex4(d, 0, 0, 255, 0, 0, 255);     // vc
        Vertex4(d, 1, 255, 255, 255, 255, 255); // vd, flag 1 -> (vb, vc, vd)

        MeshVertex[] t = Build(4, d, MeshDict(4));

        Assert.Equal(6, t.Length);
        // Second triangle is (vb, vc, vd) per §8.7.4.5.5 — NOT (va, vb, vd).
        Assert.Equal((100f, 0f, Green), (t[3].X, t[3].Y, t[3].Rgb));
        Assert.Equal((0f, 100f, Blue), (t[4].X, t[4].Y, t[4].Rgb));
        Assert.Equal((100f, 100f, White), (t[5].X, t[5].Y, t[5].Rgb));
    }

    [Fact]
    public void Type4_EdgeFlag2_FormsTriangleFromPreviousAAndC()
    {
        var d = new List<byte>();
        Vertex4(d, 0, 0, 0, 255, 0, 0);       // va
        Vertex4(d, 0, 255, 0, 0, 255, 0);     // vb
        Vertex4(d, 0, 0, 255, 0, 0, 255);     // vc
        Vertex4(d, 2, 255, 255, 255, 255, 255); // vd, flag 2 -> (va, vc, vd)

        MeshVertex[] t = Build(4, d, MeshDict(4));

        Assert.Equal(6, t.Length);
        // Flag 2 keeps va, drops vb — the distinction this test exists to pin.
        Assert.Equal((0f, 0f, Red), (t[3].X, t[3].Y, t[3].Rgb));
        Assert.Equal((0f, 100f, Blue), (t[4].X, t[4].Y, t[4].Rgb));
        Assert.Equal((100f, 100f, White), (t[5].X, t[5].Y, t[5].Rgb));
    }

    [Fact]
    public void Type4_VertexDataIsBytePadded_SecondVertexStillDecodes()
    {
        // 4-bit components: flag 8 + coords 16 + colour 12 = 36 bits, which is NOT divisible by 8.
        // §8.7.4.5.5: "the last data byte for each vertex is padded at the end with extra bits, which
        // shall be ignored." Reading straight through without re-aligning would slide every vertex
        // after the first by 4 bits and decode garbage.
        var d = new List<byte>();
        // vertex 1: flag 0, x 0x00, y 0x00, rgb = F,0,0 packed as 0xF0 0x0_ + 4 pad bits
        d.Add(0); d.Add(0x00); d.Add(0x00); d.Add(0xF0); d.Add(0x00);
        // vertex 2: flag 0, x 0xFF, y 0x00, rgb = 0,F,0
        d.Add(0); d.Add(0xFF); d.Add(0x00); d.Add(0x0F); d.Add(0x00);
        // vertex 3: flag 0, x 0x00, y 0xFF, rgb = 0,0,F
        d.Add(0); d.Add(0x00); d.Add(0xFF); d.Add(0x00); d.Add(0xF0);

        MeshVertex[] t = Build(4, d, MeshDict(4, bitsPerComponent: 4));

        Assert.Equal(3, t.Length);
        Assert.Equal((0f, 0f, Red), (t[0].X, t[0].Y, t[0].Rgb));
        Assert.Equal((100f, 0f, Green), (t[1].X, t[1].Y, t[1].Rgb));
        Assert.Equal((0f, 100f, Blue), (t[2].X, t[2].Y, t[2].Rgb));
    }

    [Fact]
    public void Type5_TwoByTwoLattice_EmitsTheTwoSpecifiedTriplets()
    {
        // Row 0: V00 (0,0) red,   V01 (100,0) green
        // Row 1: V10 (0,100) blue, V11 (100,100) white
        var d = new List<byte>();
        Vertex5(d, 0, 0, 255, 0, 0);
        Vertex5(d, 255, 0, 0, 255, 0);
        Vertex5(d, 0, 255, 0, 0, 255);
        Vertex5(d, 255, 255, 255, 255, 255);

        MeshVertex[] t = Build(5, d, MeshDict(5, verticesPerRow: 2));

        // §8.7.4.5.6: (Vi,j  Vi,j+1  Vi+1,j) and (Vi,j+1  Vi+1,j  Vi+1,j+1)
        Assert.Equal(6, t.Length);
        Assert.Equal(Red, t[0].Rgb);
        Assert.Equal(Green, t[1].Rgb);
        Assert.Equal(Blue, t[2].Rgb);
        Assert.Equal(Green, t[3].Rgb);
        Assert.Equal(Blue, t[4].Rgb);
        Assert.Equal(White, t[5].Rgb);
    }

    [Fact]
    public void Type5_IgnoresEdgeFlagsAndUsesVerticesPerRow()
    {
        // Three columns, two rows -> 2 quads -> 4 triangles -> 12 vertices. Confirms the row stride
        // comes from /VerticesPerRow and that no per-vertex flag byte is consumed (a stray flag read
        // would desynchronise everything after the first vertex).
        var d = new List<byte>();
        for (byte row = 0; row < 2; row++)
            for (byte col = 0; col < 3; col++)
                Vertex5(d, (byte)(col * 127), (byte)(row * 255), 255, 0, 0);

        MeshVertex[] t = Build(5, d, MeshDict(5, verticesPerRow: 3));

        Assert.Equal(12, t.Length);
    }

    [Fact]
    public void Type5_WithoutVerticesPerRow_DeclinesRatherThanGuessing()
    {
        var d = new List<byte>();
        Vertex5(d, 0, 0, 255, 0, 0);
        Vertex5(d, 255, 0, 0, 255, 0);

        var dict = MeshDict(5);                     // no /VerticesPerRow — required by Table 82
        var stream = new PdfStream(dict, d.ToArray());
        Assert.Null(MeshShadingReader.Build(stream, dict, 5, null, null));
    }

    [Fact]
    public void Type4_LeadingContinuationFlagWithNoPriorTriangle_DeclinesInsteadOfThrowing()
    {
        var d = new List<byte>();
        Vertex4(d, 1, 0, 0, 255, 0, 0);             // flag 1 with nothing to continue from

        var dict = MeshDict(4);
        var stream = new PdfStream(dict, d.ToArray());
        Assert.Null(MeshShadingReader.Build(stream, dict, 4, null, null));
    }
}
