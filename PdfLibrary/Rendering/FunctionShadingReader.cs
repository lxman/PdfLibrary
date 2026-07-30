using System.Numerics;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Functions;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// Builds a function-based (type 1) shading — ISO 32000-2 §8.7.4.5.3. Unlike every other shading type
/// this one has no geometry of its own: it evaluates a 2-in/n-out function (or n 2-in/1-out functions)
/// over a rectangular /Domain in its own parameter space, and /Matrix maps that domain into the
/// shading's target coordinate space.
///
/// <para>
/// The domain is tessellated into a Gouraud triangle grid — colour sampled exactly at each grid vertex,
/// linearly interpolated between — so the result rides the same
/// <see cref="ShadingDescriptor.MeshTriangles"/> path the mesh types already use and needs no backend
/// change. Sampling density is fixed at <see cref="Subdivisions"/> per axis; a function-based shading
/// carries no resolution hint, and the grid is built once per shading, not per pixel.
/// </para>
///
/// <para>
/// Points outside /Domain are simply not tessellated, which is what the spec asks for ("the shading
/// shall not be painted" there) — no clipping is needed at paint time.
/// </para>
/// </summary>
internal static class FunctionShadingReader
{
    // Per-axis grid resolution: (N+1)² function evaluations and 2N² triangles per shading.
    private const int Subdivisions = 32;

    public static ShadingDescriptor? Build(PdfDictionary dict, PdfDocument? document, Matrix3x2? patternMatrix)
    {
        if (!dict.TryGetValue(new PdfName("Function"), out PdfObject funcObj)) return null;   // required
        List<PdfFunction> functions = ShadingBuilder.ResolveFunctions(funcObj, document);
        if (functions.Count == 0) return null;                 // a function type we can't evaluate

        double[] domain = ShadingBuilder.GetNumbers(dict, "Domain", document) ?? [0.0, 1.0, 0.0, 1.0];
        if (domain.Length < 4) return null;
        double x0 = domain[0], x1 = domain[1], y0 = domain[2], y1 = domain[3];
        // Table 78 gives /Domain as [x0 x1 y0 y1] with x0 < x1 and y0 < y1. A degenerate or reversed
        // rectangle spans no area, so there is nothing to tessellate — decline rather than emit
        // zero-area triangles or silently flip the parameterisation.
        if (x1 <= x0 || y1 <= y0) return null;

        Matrix3x2 matrix = Matrix3x2.Identity;
        if (ShadingBuilder.GetNumbers(dict, "Matrix", document) is { Length: >= 6 } m)
            matrix = new Matrix3x2((float)m[0], (float)m[1], (float)m[2], (float)m[3], (float)m[4], (float)m[5]);

        // Colour setup mirrors the mesh reader's: sRGB always, native CMYK when the space resolves to
        // it, and the SP-7 spot split when a DeviceCMYK-alternate Separation/DeviceN names a spot.
        PdfObject? csObj = dict.TryGetValue(new PdfName("ColorSpace"), out PdfObject cs) ? cs : null;
        Func<double[], uint> toRgb = ShadingBuilder.BuildColorMapper(csObj, document);
        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(csObj, document);

        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(csObj, null, document);
        // Placement first: a listed process name such as /PrCyan is NOT a spot, and the name-derived
        // split would put its ink on a spot plane while the cyan unit sat dry.
        ColorantPlacement? placement = origin?.Placement;
        List<string> spotNames = placement is not null
            ? [.. placement.SpotNames]
            : origin is not null ? ShadingSpotSplit.SpotNames(origin.Names) : [];
        bool splitSpots = toCmyk is not null && origin is not null && spotNames.Count > 0;
        int spotN = spotNames.Count;
        bool hasProcess = placement is not null
            ? placement.Slots.Any(s => s.Kind == ColorantSlotKind.Plate)
            : origin is not null && origin.Names.Any(n => PageColorant.Classify(n) == ColorantKind.Process);

        const int n = Subdivisions;
        var grid = new MeshVertex[(n + 1) * (n + 1)];
        uint[]? gridProc = splitSpots ? new uint[(n + 1) * (n + 1)] : null;
        byte[]? gridTints = splitSpots ? new byte[(n + 1) * (n + 1) * spotN] : null;

        for (var gy = 0; gy <= n; gy++)
        for (var gx = 0; gx <= n; gx++)
        {
            double x = x0 + (x1 - x0) * gx / n;
            double y = y0 + (y1 - y0) * gy / n;
            double[] comps = ShadingBuilder.EvaluateColor(functions, [x, y]);

            Vector2 pos = Vector2.Transform(new Vector2((float)x, (float)y), matrix);
            int gi = gy * (n + 1) + gx;
            grid[gi] = new MeshVertex(pos.X, pos.Y, toRgb(comps), toCmyk?.Invoke(comps) ?? 0u);
            if (!splitSpots) continue;

            var tints = new byte[spotN];
            gridProc![gi] = placement is not null
                ? ShadingSpotSplit.SplitByPlacement(comps, placement, tints, 0)
                : ShadingSpotSplit.Split(comps, origin!.Names, tints, 0);
            tints.CopyTo(gridTints.AsSpan(gi * spotN));
        }

        var triangles = new List<MeshVertex>(n * n * 6);
        var vertProc = splitSpots ? new List<uint>(n * n * 6) : null;
        var vertTints = splitSpots ? new List<byte>(n * n * 6 * spotN) : null;

        for (var gy = 0; gy < n; gy++)
        for (var gx = 0; gx < n; gx++)
        {
            int ia = gy * (n + 1) + gx, ib = gy * (n + 1) + gx + 1;
            int ic = (gy + 1) * (n + 1) + gx + 1, id = (gy + 1) * (n + 1) + gx;
            Emit(ia); Emit(ib); Emit(ic);
            Emit(ia); Emit(ic); Emit(id);
        }

        void Emit(int gi)
        {
            triangles.Add(grid[gi]);
            if (!splitSpots) return;
            vertProc!.Add(gridProc![gi]);
            for (var s = 0; s < spotN; s++) vertTints!.Add(gridTints![gi * spotN + s]);
        }

        return new ShadingDescriptor
        {
            ShadingType = 1,
            MeshTriangles = triangles.ToArray(),
            MeshHasCmyk = toCmyk is not null,
            PatternMatrix = patternMatrix,
            ColorantOrigin = origin,
            MeshSpotInk = vertTints is { Count: > 0 }
                ? new MeshSpotInk(spotNames, vertTints.ToArray(), hasProcess ? vertProc!.ToArray() : null)
                : null,
        };
    }
}
