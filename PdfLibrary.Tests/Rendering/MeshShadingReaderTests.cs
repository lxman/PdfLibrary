using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Decode/tessellation checks for type 7 (tensor-product) patch-mesh shadings. Uses a single planar
/// flag-0 patch on a regular control-point grid, so the bicubic surface reduces to the affine map
/// S(u,v) = (100u, 100v) and every tessellation vertex has a predictable position — which pins down
/// the stream's control-point ordering (ISO 32000 Table 86) and the corner-colour bilinear map.
/// </summary>
public class MeshShadingReaderTests
{
    // Control-point (col,row) in the order a full (flag 0) tensor patch lists them in the stream.
    private static readonly (int Col, int Row)[] FullOrder =
    [
        (0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2),
        (3, 1), (3, 0), (2, 0), (1, 0), (1, 1), (1, 2), (2, 2), (2, 1)
    ];

    // A single flag-0 tensor patch: 8-bit flag/coords/components, DeviceCMYK, Decode maps coords to
    // [0,100] and each component to [0,1]. Control points sit on a regular 4×4 grid (raw col*85, row*85),
    // giving corners p00=(0,0) p30=(100,0) p03=(0,100) p33=(100,100). Corner inks (stream order
    // c00,c03,c33,c30): cyan, yellow, black, magenta.
    private static PdfStream OnePatchTensor()
    {
        var data = new List<byte> { 0 }; // edge flag 0
        foreach ((int col, int row) in FullOrder)
        {
            data.Add((byte)(col * 85));   // rawX → col*85/255*100
            data.Add((byte)(row * 85));   // rawY
        }
        data.AddRange(new byte[] { 255, 0, 0, 0 });  // c00 cyan
        data.AddRange(new byte[] { 0, 0, 255, 0 });  // c03 yellow
        data.AddRange(new byte[] { 0, 0, 0, 255 });  // c33 black
        data.AddRange(new byte[] { 0, 255, 0, 0 });  // c30 magenta

        var dict = new PdfDictionary();
        dict.Add(new PdfName("ShadingType"), new PdfInteger(7));
        dict.Add(new PdfName("ColorSpace"), new PdfName("DeviceCMYK"));
        dict.Add(new PdfName("BitsPerCoordinate"), new PdfInteger(8));
        dict.Add(new PdfName("BitsPerComponent"), new PdfInteger(8));
        dict.Add(new PdfName("BitsPerFlag"), new PdfInteger(8));
        dict.Add(new PdfName("Decode"), Reals(0, 100, 0, 100, 0, 1, 0, 1, 0, 1, 0, 1));
        return new PdfStream(dict, data.ToArray());
    }

    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    private static bool Near(float a, float b, float tol = 0.6f) => System.MathF.Abs(a - b) <= tol;

    // Finds the tessellation vertex closest to (x,y) and returns its packed native CMYK.
    private static uint CmykAt(MeshVertex[] tris, float x, float y)
    {
        MeshVertex best = tris[0];
        float bestD = float.MaxValue;
        foreach (MeshVertex v in tris)
        {
            float d = (v.X - x) * (v.X - x) + (v.Y - y) * (v.Y - y);
            if (d < bestD) { bestD = d; best = v; }
        }
        return best.Cmyk;
    }

    [Fact]
    public void Type7_flat_patch_tessellates_with_predictable_geometry()
    {
        ShadingDescriptor? d = ShadingBuilder.Build(OnePatchTensor(), null);

        Assert.NotNull(d);
        Assert.Equal(7, d!.ShadingType);
        Assert.True(d.MeshHasCmyk);
        // N=8 subdivisions → 2 triangles × 64 cells × 3 verts = 384 vertices.
        Assert.Equal(384, d.MeshTriangles.Length);

        // Bounding box of the affine patch is exactly [0,100]×[0,100].
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (MeshVertex v in d.MeshTriangles)
        {
            minX = System.MathF.Min(minX, v.X); maxX = System.MathF.Max(maxX, v.X);
            minY = System.MathF.Min(minY, v.Y); maxY = System.MathF.Max(maxY, v.Y);
        }
        Assert.True(Near(minX, 0) && Near(minY, 0), $"min ({minX},{minY})");
        Assert.True(Near(maxX, 100) && Near(maxY, 100), $"max ({maxX},{maxY})");
    }

    [Fact]
    public void Type7_corner_colours_map_to_the_right_corners()
    {
        ShadingDescriptor? d = ShadingBuilder.Build(OnePatchTensor(), null);
        Assert.NotNull(d);
        MeshVertex[] tris = d!.MeshTriangles;

        // c00 cyan at (0,0); c03 yellow at (0,100); c33 black at (100,100); c30 magenta at (100,0).
        Assert.True((byte)(CmykAt(tris, 0, 0) >> 24) > 250, "corner (0,0) should be cyan (C≈255)");
        Assert.True((byte)(CmykAt(tris, 0, 100) >> 8) > 250, "corner (0,100) should be yellow (Y≈255)");
        Assert.True((byte)CmykAt(tris, 100, 100) > 250, "corner (100,100) should be black (K≈255)");
        Assert.True((byte)(CmykAt(tris, 100, 0) >> 16) > 250, "corner (100,0) should be magenta (M≈255)");
    }
}
