using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

public class MeshSpotInkTests
{
    // A Type-2 exponential 0→1 tint transform on `outN` outputs (the Separation/DeviceN alternate).
    private static PdfDictionary Type2Fn(double[] c0, double[] c1)
    {
        var d = new PdfDictionary();
        d[new PdfName("FunctionType")] = new PdfInteger(2);
        d[new PdfName("Domain")] = new PdfArray(new PdfReal(0), new PdfReal(1));
        d[new PdfName("C0")] = new PdfArray(System.Array.ConvertAll(c0, v => (PdfObject)new PdfReal(v)));
        d[new PdfName("C1")] = new PdfArray(System.Array.ConvertAll(c1, v => (PdfObject)new PdfReal(v)));
        d[new PdfName("N")] = new PdfReal(1);
        return d;
    }

    private static PdfArray SeparationCmyk(string name) => new(
        new PdfName("Separation"), new PdfName(name), new PdfName("DeviceCMYK"),
        Type2Fn([0, 0, 0, 0], [0, 1, 0, 0]));

    private static PdfArray DeviceNCmyk(string[] names) => new(
        new PdfName("DeviceN"),
        new PdfArray(System.Array.ConvertAll(names, n => (PdfObject)new PdfName(n))),
        new PdfName("DeviceCMYK"),
        Type2Fn([0, 0, 0, 0], [0, 1, 0, 0]));

    // Build a single-patch type-6 Coons mesh stream. 8-bit coords/components/flag, one colour component per
    // corner (componentsPerCorner = number of colorants in the space). `cornerTints[4][componentsPerCorner]`
    // are the raw 0..255 component bytes for c00,c03,c33,c30. Geometry is a unit square (12 boundary points).
    private static (PdfDictionary dict, PdfStream stream) Coons(PdfObject colorSpace, int componentsPerCorner, byte[][] cornerTints)
    {
        var bytes = new System.Collections.Generic.List<byte>();
        bytes.Add(0);                                   // flag 0 (new patch)
        // 12 boundary points, values 0..255 mapped by Decode [0 1] → we just need distinct valid geometry.
        // Corner order in the stream boundary is p00..p03, p13..p33, p32..p30, p20..p10 (Table 85); exact
        // positions don't matter to the split/tint assertions — only that 12 points are present.
        byte[] coords =
        [
            0,0,  0,85,  0,170,  0,255,  85,255,  170,255,
            255,255,  255,170,  255,85,  255,0,  170,0,  85,0
        ];
        bytes.AddRange(coords);
        foreach (byte[] corner in cornerTints)
            for (var c = 0; c < componentsPerCorner; c++) bytes.Add(corner[c]);

        var stream = new PdfStream(new PdfDictionary(), bytes.ToArray());
        var dict = new PdfDictionary();
        dict[new PdfName("ShadingType")] = new PdfInteger(6);
        dict[new PdfName("BitsPerCoordinate")] = new PdfInteger(8);
        dict[new PdfName("BitsPerComponent")] = new PdfInteger(8);
        dict[new PdfName("BitsPerFlag")] = new PdfInteger(8);
        // Decode: x,y in [0,1]; then componentsPerCorner pairs each [0,1] (tint 0..1).
        var decode = new System.Collections.Generic.List<PdfObject> { new PdfReal(0), new PdfReal(1), new PdfReal(0), new PdfReal(1) };
        for (var c = 0; c < componentsPerCorner; c++) { decode.Add(new PdfReal(0)); decode.Add(new PdfReal(1)); }
        dict[new PdfName("Decode")] = new PdfArray(decode.ToArray());
        dict[new PdfName("ColorSpace")] = colorSpace;
        // NB: no /Function → each corner stores componentsPerCorner raw colour components directly.
        return (dict, stream);
    }

    // ShadingBuilder.Build takes the shading object; for a stream shading pass the PdfStream (its dict merged).
    private static ShadingDescriptor? BuildCoons(PdfObject colorSpace, int comps, byte[][] cornerTints)
    {
        (PdfDictionary dict, PdfStream stream) = Coons(colorSpace, comps, cornerTints);
        foreach (System.Collections.Generic.KeyValuePair<PdfName, PdfObject> kv in dict) stream.Dictionary[kv.Key] = kv.Value;
        return ShadingBuilder.Build(stream, null);
    }

    // Build a single-patch type-7 tensor-product mesh: flag(1B)=0, then 16 control points (32 coord bytes —
    // the full FullOrder; type 7 READS all 16 rather than deriving the 4 internal ones), then 4 corner
    // colours. This drives the type-7 branch (`lastPoint == 16`, `DeriveInternalPoints` skipped); the corner
    // split + parallel-Bilerp tessellation are the SAME code as type 6, so this confirms the split fires on
    // the tensor path too.
    private static ShadingDescriptor? BuildTensor(PdfObject colorSpace, int componentsPerCorner, byte[][] cornerTints)
    {
        var bytes = new System.Collections.Generic.List<byte> { 0 };   // flag 0 (new patch)
        byte[] coords =
        [
            0,0,  0,85,  0,170,  0,255,  85,255,  170,255,
            255,255,  255,170,  255,85,  255,0,  170,0,  85,0,   // 12 boundary points
            85,85,  85,170,  170,170,  170,85                    // 4 internal control points (type-7 only)
        ];
        bytes.AddRange(coords);
        foreach (byte[] corner in cornerTints)
            for (var c = 0; c < componentsPerCorner; c++) bytes.Add(corner[c]);

        var stream = new PdfStream(new PdfDictionary(), bytes.ToArray());
        stream.Dictionary[new PdfName("ShadingType")] = new PdfInteger(7);
        stream.Dictionary[new PdfName("BitsPerCoordinate")] = new PdfInteger(8);
        stream.Dictionary[new PdfName("BitsPerComponent")] = new PdfInteger(8);
        stream.Dictionary[new PdfName("BitsPerFlag")] = new PdfInteger(8);
        var decode = new System.Collections.Generic.List<PdfObject> { new PdfReal(0), new PdfReal(1), new PdfReal(0), new PdfReal(1) };
        for (var c = 0; c < componentsPerCorner; c++) { decode.Add(new PdfReal(0)); decode.Add(new PdfReal(1)); }
        stream.Dictionary[new PdfName("Decode")] = new PdfArray(decode.ToArray());
        stream.Dictionary[new PdfName("ColorSpace")] = colorSpace;
        return ShadingBuilder.Build(stream, null);
    }

    [Fact]
    public void PureSeparationMesh_PopulatesMeshSpotInk_ProcessNull()
    {
        // Corner tints 0,85,255,170 for the single GWG Green component.
        ShadingDescriptor? sh = BuildCoons(SeparationCmyk("GWG Green"), 1,
            [[0], [85], [255], [170]]);

        Assert.NotNull(sh);
        Assert.NotNull(sh!.MeshSpotInk);
        Assert.Equal(new[] { "GWG Green" }, sh.MeshSpotInk!.Names);
        Assert.Null(sh.MeshSpotInk.VertexProcessCmyk);                       // pure spot ⇒ process null
        Assert.Equal(sh.MeshTriangles.Length, sh.MeshSpotInk.VertexTints.Length); // N=1
        // The tint plane must span the corner range (0..255 present somewhere after tessellation).
        Assert.Contains(sh.MeshSpotInk.VertexTints, b => b > 200);
        Assert.Contains(sh.MeshSpotInk.VertexTints, b => b < 40);
        // Mirror unchanged: MeshVertex.Cmyk still the full flatten.
        Assert.True(sh.MeshTriangles.Length > 0);
    }

    [Fact]
    public void MeshInterpolationParity_TintTracksCmykMagenta()
    {
        // Separation alternate maps tint t → (0, t, 0, 0): the flattened MeshVertex.Cmyk's MAGENTA byte
        // equals the routed spot tint at every vertex. This pins that VertexTints is interpolated by the
        // SAME Bilerp weights as MeshVertex.Cmyk (the load-bearing parity property).
        // Corners are 0 and 255 (no rounding midpoint), and BilerpByte mirrors Bilerp's arithmetic exactly,
        // so the flatten M byte and the routed tint are BIT-IDENTICAL at every vertex — assert exact equality.
        ShadingDescriptor? sh = BuildCoons(SeparationCmyk("GWG Green"), 1, [[0], [255], [255], [0]]);
        Assert.NotNull(sh!.MeshSpotInk);
        int n = sh.MeshSpotInk!.Names.Count;   // 1
        for (var v = 0; v < sh.MeshTriangles.Length; v++)
        {
            byte magenta = (byte)((sh.MeshTriangles[v].Cmyk >> 16) & 0xFF);   // M channel of the flatten
            byte tint = sh.MeshSpotInk.VertexTints[v * n];
            Assert.Equal(magenta, tint);                                       // same Bilerp weights ⇒ exact
        }
    }

    [Fact]
    public void MixedDeviceNMesh_SplitsCyanToProcess_SpotToTints()
    {
        // DeviceN [GWG Green, Cyan]; corners carry (GWG Green tint, Cyan tint).
        ShadingDescriptor? sh = BuildCoons(DeviceNCmyk(["GWG Green", "Cyan"]), 2,
            [[0, 0], [255, 255], [255, 255], [0, 0]]);
        Assert.NotNull(sh!.MeshSpotInk);
        Assert.Equal(new[] { "GWG Green" }, sh.MeshSpotInk!.Names);           // only the spot name
        Assert.NotNull(sh.MeshSpotInk.VertexProcessCmyk);                     // has a process colorant
        // Some vertex has C≈full in the process plane and M/Y/K zero (Cyan → C plate; spot NOT folded).
        Assert.Contains(sh.MeshSpotInk.VertexProcessCmyk!, p => ((p >> 24) & 0xFF) > 200 && (p & 0x00FFFFFFu) == 0);
    }

    [Fact]
    public void PureSeparationTensorMesh_Type7_PopulatesMeshSpotInk()
    {
        // Type-7 tensor patch over /Separation — the corner split + tessellation path is shared with type 6,
        // so this confirms MeshSpotInk is emitted on the 16-control-point branch (DeriveInternalPoints skipped).
        ShadingDescriptor? sh = BuildTensor(SeparationCmyk("GWG Green"), 1, [[0], [255], [255], [0]]);

        Assert.NotNull(sh);
        Assert.Equal(7, sh!.ShadingType);                                    // drove the tensor branch
        Assert.NotNull(sh.MeshSpotInk);
        Assert.Equal(new[] { "GWG Green" }, sh.MeshSpotInk!.Names);
        Assert.Null(sh.MeshSpotInk.VertexProcessCmyk);                       // pure spot ⇒ process null
        Assert.Equal(sh.MeshTriangles.Length, sh.MeshSpotInk.VertexTints.Length);
        // Interpolation ran across the tensor patch: the tint plane spans the corner range.
        Assert.Contains(sh.MeshSpotInk.VertexTints, b => b > 200);
        Assert.Contains(sh.MeshSpotInk.VertexTints, b => b < 40);
    }

    [Fact]
    public void DeviceCmykMesh_NoMeshSpotInk()
    {
        ShadingDescriptor? sh = BuildCoons(new PdfName("DeviceCMYK"), 4,
            [[0, 0, 0, 0], [255, 0, 0, 0], [0, 255, 0, 0], [0, 0, 255, 0]]);
        Assert.NotNull(sh);
        Assert.Null(sh!.MeshSpotInk);
    }
}
