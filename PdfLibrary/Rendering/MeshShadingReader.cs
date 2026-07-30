using System.Linq;
using System.Numerics;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Functions;
using PdfLibrary.Structure;

namespace PdfLibrary.Rendering;

/// <summary>
/// Decodes a type 6 (Coons) or type 7 (tensor-product) patch-mesh shading stream and tessellates it
/// into a Gouraud-shaded triangle soup (<see cref="ShadingDescriptor.MeshTriangles"/>). The bicubic
/// tensor surface of each patch is sampled on an N×N grid; the four corner colours are bilinearly
/// interpolated across the unit square (ISO 32000 §8.7.4.5.7–8). A type 6 patch is promoted to a
/// tensor patch by deriving its four internal control points from the boundary (spec formulas), so
/// both types share one evaluation path.
/// </summary>
internal static class MeshShadingReader
{
    // Per-patch grid resolution. Colour is only bilinear (four corners), so N governs geometric
    // fidelity of the Bézier boundaries; small patches stay smooth at modest N.
    private const int Subdivisions = 8;

    // The 16 control points in the order the stream lists them for a full (flag 0) tensor patch
    // (Table 86), expressed as (column, row) into the 4×4 tensor grid p[col,row]. The first 12 are the
    // boundary (also the Coons type-6 order, Table 85); the last four are the internal points.
    private static readonly (int Col, int Row)[] FullOrder =
    [
        (0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2),
        (3, 1), (3, 0), (2, 0), (1, 0), (1, 1), (1, 2), (2, 2), (2, 1)
    ];

    private sealed class Patch
    {
        public readonly Vector2[] Cp = new Vector2[16];        // indexed by Idx(col,row)
        // c00, c03, c33, c30. Proc = process-only packed CMYK; Tints = per-spot tint bytes (empty when no
        // spot). Inherited corners (flag 1/2/3) copy the whole tuple, so the split rides inheritance for free.
        public readonly (uint Rgb, uint Cmyk, uint Proc, byte[] Tints)[] Corner = new (uint, uint, uint, byte[])[4];
    }

    private static int Idx(int col, int row) => col * 4 + row;

    public static ShadingDescriptor? Build(PdfStream stream, PdfDictionary dict, int shadingType,
        PdfDocument? document, Matrix3x2? patternMatrix)
    {
        int bpc = GetInt(dict, "BitsPerCoordinate", document);
        int bpComp = GetInt(dict, "BitsPerComponent", document);
        int bpf = GetInt(dict, "BitsPerFlag", document);
        if (bpc is <= 0 or > 32 || bpComp is <= 0 or > 16 || bpf is <= 0 or > 8) return null;

        double[]? decode = GetNumbers(dict, "Decode", document);
        if (decode is null || decode.Length < 6) return null;   // x, y, and ≥1 colour pair

        PdfObject? csObj = dict.TryGetValue(new PdfName("ColorSpace"), out PdfObject cs) ? cs : null;
        Func<double[], uint> toRgb = ShadingBuilder.BuildColorMapper(csObj, document);
        Func<double[], uint>? toCmyk = ShadingBuilder.BuildCmykMapper(csObj, document);

        // SP-7-mesh: preserve a spot mesh's per-vertex spot tints + process-only CMYK (only for a
        // DeviceCMYK-alternate Separation/DeviceN, i.e. toCmyk non-null and a spot colorant present).
        ColorantOrigin? origin = ColorSpaceResolver.OriginForColorSpaceObject(csObj, null, document);
        // Placement first: a listed process name such as /PrCyan is NOT a spot, and the name-derived
        // split would put its ink on a spot plane while the cyan unit sat dry. Falls back whole when
        // the space has no placement (plain DeviceN, one-channel process space, /All, unplaceable).
        ColorantPlacement? placement = origin?.Placement;
        List<string> spotNames = placement is not null
            ? [.. placement.SpotNames]
            : origin is not null ? ShadingSpotSplit.SpotNames(origin.Names) : [];
        bool splitSpots = toCmyk is not null && origin is not null && spotNames.Count > 0;
        int spotN = spotNames.Count;
        bool hasProcess = placement is not null
            ? placement.Slots.Any(s => s.Kind == ColorantSlotKind.Plate)
            : origin is not null && origin.Names.Any(n => PageColorant.Classify(n) == ColorantKind.Process);
        var vertProc = splitSpots ? new List<uint>() : null;
        var vertTints = splitSpots ? new List<byte>() : null;

        // With a /Function each corner stores a single parametric value t; otherwise it stores the
        // colour space's n components directly. The Decode array sizes the latter.
        bool hasFunction = dict.TryGetValue(new PdfName("Function"), out PdfObject funcObj);
        List<PdfFunction> functions = hasFunction ? ShadingBuilder.ResolveFunctions(funcObj, document) : [];
        if (hasFunction && functions.Count == 0) return null;
        int colorValues = hasFunction ? 1 : (decode.Length - 4) / 2;
        if (colorValues < 1) return null;

        byte[] data = stream.GetDecodedData(document?.Decryptor);
        if (data.Length == 0) return null;
        var reader = new BitReader(data);

        double coordMax = Math.Pow(2, bpc) - 1;
        double compMax = Math.Pow(2, bpComp) - 1;

        var triangles = new List<MeshVertex>();

        // Shared by the patch (6/7) and Gouraud (4/5) paths: both decode a point and a colour the same
        // way, so these live here rather than being written twice.
        Vector2 ReadPoint()
        {
            double px = decode[0] + reader.Read(bpc) * (decode[1] - decode[0]) / coordMax;
            double py = decode[2] + reader.Read(bpc) * (decode[3] - decode[2]) / coordMax;
            return new Vector2((float)px, (float)py);
        }

        (uint Rgb, uint Cmyk, uint Proc, byte[] Tints) ReadColour()
        {
            var comps = new double[colorValues];
            for (var c = 0; c < colorValues; c++)
            {
                double lo = decode[4 + 2 * c], hi = decode[5 + 2 * c];
                comps[c] = lo + reader.Read(bpComp) * (hi - lo) / compMax;
            }
            double[] csComps = hasFunction ? ShadingBuilder.EvaluateColor(functions, comps[0]) : comps;
            uint proc = 0;
            byte[] tints = [];
            if (splitSpots)
            {
                tints = new byte[spotN];
                proc = placement is not null
                    ? ShadingSpotSplit.SplitByPlacement(csComps, placement, tints, 0)
                    : ShadingSpotSplit.Split(csComps, origin!.Names, tints, 0);
            }
            return (toRgb(csComps), toCmyk?.Invoke(csComps) ?? 0u, proc, tints);
        }

        // Types 4 and 5 are triangle meshes, not patch meshes: the vertices ARE the triangles, so they
        // bypass the bicubic tessellation entirely and feed Finish() directly.
        if (shadingType is 4 or 5)
        {
            bool ok = shadingType == 4
                ? ReadFreeForm(reader, bpf, ReadPoint, ReadColour, triangles, vertProc, vertTints, spotN)
                : ReadLattice(GetInt(dict, "VerticesPerRow", document), reader,
                    ReadPoint, ReadColour, triangles, vertProc, vertTints, spotN);

            return ok && triangles.Count > 0
                ? Finish(triangles, toCmyk, patternMatrix, shadingType, origin, spotNames, vertProc, vertTints, hasProcess)
                : null;
        }

        Patch? prev = null;

        while (reader.HasData)
        {
            long flagRaw = reader.Read(bpf);
            if (reader.Eof) break;
            int flag = (int)(flagRaw & 0x3);
            if (flag != 0 && prev is null) return triangles.Count > 0 ? Finish(triangles, toCmyk, patternMatrix, shadingType, origin, spotNames, vertProc, vertTints, hasProcess) : null;

            var patch = new Patch();

            // --- control points ---
            // flag 0: all points in FullOrder. flag 1/2/3: the shared boundary p00..p03 is inherited
            // from the previous patch's edge, and the remaining points are read (FullOrder[4..]).
            int startPoint = flag == 0 ? 0 : 4;
            int lastPoint = shadingType == 7 ? 16 : 12;   // type 6 reads/derives only the 12 boundary points

            if (flag != 0)
                InheritBoundary(prev!, patch, flag);

            for (int k = startPoint; k < lastPoint; k++)
            {
                (int col, int row) = FullOrder[k];
                double x = decode[0] + reader.Read(bpc) * (decode[1] - decode[0]) / coordMax;
                double y = decode[2] + reader.Read(bpc) * (decode[3] - decode[2]) / coordMax;
                patch.Cp[Idx(col, row)] = new Vector2((float)x, (float)y);
            }
            if (reader.Eof) break;

            // A Coons (type 6) patch supplies only the boundary; derive the four internal points.
            if (shadingType == 6) DeriveInternalPoints(patch);

            // --- corner colours ---
            // flag 0: four corners c00,c03,c33,c30. flag 1/2/3: c00,c03 inherited (done in InheritBoundary),
            // c33,c30 read here.
            int firstCorner = flag == 0 ? 0 : 2;
            for (int corner = firstCorner; corner < 4; corner++)
            {
                var comps = new double[colorValues];
                for (int c = 0; c < colorValues; c++)
                {
                    double lo = decode[4 + 2 * c], hi = decode[5 + 2 * c];
                    comps[c] = lo + reader.Read(bpComp) * (hi - lo) / compMax;
                }
                double[] colorSpaceComps = hasFunction ? ShadingBuilder.EvaluateColor(functions, comps[0]) : comps;
                uint proc = 0;
                byte[] tints = [];
                if (splitSpots)
                {
                    tints = new byte[spotN];
                    proc = placement is not null
                        ? ShadingSpotSplit.SplitByPlacement(colorSpaceComps, placement, tints, 0)
                        : ShadingSpotSplit.Split(colorSpaceComps, origin!.Names, tints, 0);
                }
                patch.Corner[corner] = (toRgb(colorSpaceComps), toCmyk?.Invoke(colorSpaceComps) ?? 0u, proc, tints);
            }
            if (reader.Eof) break;

            reader.Align();          // each patch record occupies a whole number of bytes
            Tessellate(patch, triangles, vertProc, vertTints, spotN);
            prev = patch;
        }

        return triangles.Count > 0 ? Finish(triangles, toCmyk, patternMatrix, shadingType, origin, spotNames, vertProc, vertTints, hasProcess) : null;
    }

    /// <summary>
    /// One decoded Gouraud vertex: position plus every colour representation the mesh carries.
    /// </summary>
    private readonly record struct Vtx(Vector2 Pos, uint Rgb, uint Cmyk, uint Proc, byte[] Tints);

    /// <summary>
    /// Type 4 (ISO 32000-2 §8.7.4.5.5). Each vertex carries an edge flag: 0 starts a new triangle
    /// (and is followed by two more vertices completing it), 1 continues from the previous triangle's
    /// (vb, vc), and 2 continues from (va, vc). Every vertex's data — flag included — occupies a whole
    /// number of bytes, so the reader re-aligns after each one; without that, a component width that
    /// is not a multiple of 8 slides every subsequent vertex and decodes garbage.
    /// </summary>
    private static bool ReadFreeForm(BitReader reader, int bpf,
        Func<Vector2> readPoint, Func<(uint Rgb, uint Cmyk, uint Proc, byte[] Tints)> readColour,
        List<MeshVertex> tris, List<uint>? outProc, List<byte>? outTints, int spotN)
    {
        Vtx va = default, vb = default, vc = default;
        var have = false;

        Vtx ReadVertex()
        {
            Vector2 p = readPoint();
            (uint rgb, uint cmyk, uint proc, byte[] tints) = readColour();
            reader.Align();                       // per-vertex byte padding
            return new Vtx(p, rgb, cmyk, proc, tints);
        }

        while (reader.HasData)
        {
            var flag = (int)(reader.Read(bpf) & 0x3);
            if (reader.Eof) break;

            if (flag == 0)
            {
                Vtx a = ReadVertex();
                if (reader.Eof) break;
                // The two vertices completing the triangle carry their own (ignored) flags.
                reader.Read(bpf);
                Vtx b = ReadVertex();
                if (reader.Eof) break;
                reader.Read(bpf);
                Vtx c = ReadVertex();
                if (reader.Eof) break;

                (va, vb, vc) = (a, b, c);
                have = true;
            }
            else
            {
                // A continuation flag with no preceding triangle is malformed: decline rather than
                // inventing geometry from default-initialised vertices.
                if (!have) return false;

                Vtx d = ReadVertex();
                if (reader.Eof) break;

                if (flag == 1) (va, vb, vc) = (vb, vc, d);   // side vbc — va is dropped
                else (vb, vc) = (vc, d);                     // flag 2, side vac — va is KEPT
            }

            Emit(tris, outProc, outTints, spotN, va, vb, vc);
        }

        return true;
    }

    /// <summary>
    /// Type 5 (ISO 32000-2 §8.7.4.5.6). No edge flags: vertices are read in row-major order,
    /// <c>/VerticesPerRow</c> per row, and each adjacent pair of rows contributes the triplets
    /// (Vi,j  Vi,j+1  Vi+1,j) and (Vi,j+1  Vi+1,j  Vi+1,j+1). §8.7.4.5.6 defers to §8.7.4.5.5 for the
    /// vertex format, so the per-vertex byte padding applies here too.
    /// </summary>
    private static bool ReadLattice(int verticesPerRow, BitReader reader,
        Func<Vector2> readPoint, Func<(uint Rgb, uint Cmyk, uint Proc, byte[] Tints)> readColour,
        List<MeshVertex> tris, List<uint>? outProc, List<byte>? outTints, int spotN)
    {
        // Required, and >= 2 (Table 82). Without it the row stride is unknowable — decline rather
        // than guess a lattice shape.
        if (verticesPerRow < 2) return false;

        var verts = new List<Vtx>();
        while (reader.HasData)
        {
            Vector2 p = readPoint();
            (uint rgb, uint cmyk, uint proc, byte[] tints) = readColour();
            if (reader.Eof) break;
            reader.Align();
            verts.Add(new Vtx(p, rgb, cmyk, proc, tints));
        }

        int rows = verts.Count / verticesPerRow;
        if (rows < 2) return false;                 // a single row spans no triangles

        for (var i = 0; i < rows - 1; i++)
        for (var j = 0; j < verticesPerRow - 1; j++)
        {
            Vtx v00 = verts[i * verticesPerRow + j];
            Vtx v01 = verts[i * verticesPerRow + j + 1];
            Vtx v10 = verts[(i + 1) * verticesPerRow + j];
            Vtx v11 = verts[(i + 1) * verticesPerRow + j + 1];
            Emit(tris, outProc, outTints, spotN, v00, v01, v10);
            Emit(tris, outProc, outTints, spotN, v01, v10, v11);
        }

        return true;
    }

    /// <summary>Appends one triangle, keeping the parallel spot arrays in step with the vertex list.</summary>
    private static void Emit(List<MeshVertex> tris, List<uint>? outProc, List<byte>? outTints, int spotN,
        Vtx a, Vtx b, Vtx c)
    {
        Add(a); Add(b); Add(c);

        void Add(Vtx v)
        {
            tris.Add(new MeshVertex(v.Pos.X, v.Pos.Y, v.Rgb, v.Cmyk));
            if (outProc is null) return;
            outProc.Add(v.Proc);
            for (var s = 0; s < spotN; s++)
                outTints!.Add(s < v.Tints.Length ? v.Tints[s] : (byte)0);
        }
    }

    private static ShadingDescriptor Finish(List<MeshVertex> triangles, Func<double[], uint>? toCmyk,
        Matrix3x2? patternMatrix, int shadingType, ColorantOrigin? origin, List<string> spotNames,
        List<uint>? vertProc, List<byte>? vertTints, bool hasProcess) => new()
    {
        ShadingType = shadingType,
        MeshTriangles = triangles.ToArray(),
        MeshHasCmyk = toCmyk is not null,
        PatternMatrix = patternMatrix,
        ColorantOrigin = origin,
        // vertTints is non-empty only when splitSpots was true ⇒ spotNames is the shading's spot list.
        MeshSpotInk = vertTints is { Count: > 0 }
            ? new MeshSpotInk(spotNames, vertTints.ToArray(), hasProcess ? vertProc!.ToArray() : null)
            : null,
    };

    // Copies the new patch's shared edge (p00,p01,p02,p03 = column 0) and the two shared corner colours
    // (c00,c03) from the appropriate edge of the previous patch, per Table 86 / Table 85 edge flags.
    private static void InheritBoundary(Patch prev, Patch patch, int flag)
    {
        // Source (col,row) for the new column-0 points, and the two source corner-colour indices.
        (int, int)[] src;
        (int c00, int c03) col;
        switch (flag)
        {
            case 1:  // shared edge = previous top edge (row 3)
                src = [(0, 3), (1, 3), (2, 3), (3, 3)];
                col = (1, 2);   // c00_new = prev.c03, c03_new = prev.c33
                break;
            case 2:  // shared edge = previous right edge (col 3), reversed
                src = [(3, 3), (3, 2), (3, 1), (3, 0)];
                col = (2, 3);   // c00_new = prev.c33, c03_new = prev.c30
                break;
            default: // flag 3: shared edge = previous bottom edge (row 0), reversed
                src = [(3, 0), (2, 0), (1, 0), (0, 0)];
                col = (3, 0);   // c00_new = prev.c30, c03_new = prev.c00
                break;
        }
        for (int r = 0; r < 4; r++)
            patch.Cp[Idx(0, r)] = prev.Cp[Idx(src[r].Item1, src[r].Item2)];
        patch.Corner[0] = prev.Corner[col.c00];
        patch.Corner[1] = prev.Corner[col.c03];
    }

    // Derives the four internal tensor control points of a Coons (type 6) patch from its boundary,
    // making it a full tensor-product patch (ISO 32000 §8.7.4.5.8 internal-point equations).
    private static void DeriveInternalPoints(Patch p)
    {
        Vector2 P(int c, int r) => p.Cp[Idx(c, r)];
        p.Cp[Idx(1, 1)] = (1f / 9f) * (-4 * P(0, 0) + 6 * (P(0, 1) + P(1, 0)) - 2 * (P(0, 3) + P(3, 0)) + 3 * (P(3, 1) + P(1, 3)) - P(3, 3));
        p.Cp[Idx(1, 2)] = (1f / 9f) * (-4 * P(0, 3) + 6 * (P(0, 2) + P(1, 3)) - 2 * (P(0, 0) + P(3, 3)) + 3 * (P(3, 2) + P(1, 0)) - P(3, 0));
        p.Cp[Idx(2, 1)] = (1f / 9f) * (-4 * P(3, 0) + 6 * (P(3, 1) + P(2, 0)) - 2 * (P(3, 3) + P(0, 0)) + 3 * (P(0, 1) + P(2, 3)) - P(0, 3));
        p.Cp[Idx(2, 2)] = (1f / 9f) * (-4 * P(3, 3) + 6 * (P(3, 2) + P(2, 3)) - 2 * (P(3, 0) + P(0, 3)) + 3 * (P(0, 2) + P(2, 0)) - P(0, 0));
    }

    // Samples the patch surface on an (N+1)×(N+1) grid and emits two triangles per cell.
    private static void Tessellate(Patch p, List<MeshVertex> outTris,
        List<uint>? outProc, List<byte>? outTints, int spotN)
    {
        const int n = Subdivisions;
        var grid = new MeshVertex[(n + 1) * (n + 1)];
        bool split = outProc is not null;
        uint[]? gridProc = split ? new uint[(n + 1) * (n + 1)] : null;
        byte[]? gridTints = split ? new byte[(n + 1) * (n + 1) * spotN] : null;
        for (int gu = 0; gu <= n; gu++)
        for (int gv = 0; gv <= n; gv++)
        {
            float u = gu / (float)n, v = gv / (float)n;
            Vector2 pos = Surface(p, u, v);
            uint rgb = Bilerp(p.Corner[0].Rgb, p.Corner[3].Rgb, p.Corner[2].Rgb, p.Corner[1].Rgb, u, v);
            uint cmyk = Bilerp(p.Corner[0].Cmyk, p.Corner[3].Cmyk, p.Corner[2].Cmyk, p.Corner[1].Cmyk, u, v);
            int gi = gu * (n + 1) + gv;
            grid[gi] = new MeshVertex(pos.X, pos.Y, rgb, cmyk);
            if (split)
            {
                // Same Bilerp corner order + u/v as Cmyk above → structural parity.
                gridProc![gi] = Bilerp(p.Corner[0].Proc, p.Corner[3].Proc, p.Corner[2].Proc, p.Corner[1].Proc, u, v);
                for (var s = 0; s < spotN; s++)
                    gridTints![gi * spotN + s] = BilerpByte(
                        p.Corner[0].Tints[s], p.Corner[3].Tints[s], p.Corner[2].Tints[s], p.Corner[1].Tints[s], u, v);
            }
        }

        for (int gu = 0; gu < n; gu++)
        for (int gv = 0; gv < n; gv++)
        {
            int ia = gu * (n + 1) + gv, ib = (gu + 1) * (n + 1) + gv;
            int ic = (gu + 1) * (n + 1) + gv + 1, id = gu * (n + 1) + gv + 1;
            Emit(ia); Emit(ib); Emit(ic);
            Emit(ia); Emit(ic); Emit(id);
        }

        void Emit(int gi)
        {
            outTris.Add(grid[gi]);
            if (!split) return;
            outProc!.Add(gridProc![gi]);
            for (var s = 0; s < spotN; s++) outTints!.Add(gridTints![gi * spotN + s]);
        }
    }

    // Bilinear interpolation of a single byte across the unit square — identical clamp/round/order to Bilerp,
    // so a spot tint interpolates exactly as its packed-CMYK sibling (interpolation parity).
    private static byte BilerpByte(byte v0u0, byte v0u1, byte v1u1, byte v1u0, float u, float v)
    {
        float bottom = v0u0 * (1 - u) + v0u1 * u;
        float top = v1u0 * (1 - u) + v1u1 * u;
        float f = bottom * (1 - v) + top * v;
        return (byte)Math.Clamp((int)MathF.Round(f), 0, 255);
    }

    // S(u,v) = Σ_col Σ_row p[col,row] · B_col(u) · B_row(v) — bicubic Bernstein tensor product.
    private static Vector2 Surface(Patch p, float u, float v)
    {
        Span<float> bu = stackalloc float[4];
        Span<float> bv = stackalloc float[4];
        Bernstein(u, bu);
        Bernstein(v, bv);
        Vector2 s = Vector2.Zero;
        for (int col = 0; col < 4; col++)
        for (int row = 0; row < 4; row++)
            s += p.Cp[Idx(col, row)] * (bu[col] * bv[row]);
        return s;
    }

    private static void Bernstein(float t, Span<float> b)
    {
        float mt = 1f - t;
        b[0] = mt * mt * mt;
        b[1] = 3f * t * mt * mt;
        b[2] = 3f * t * t * mt;
        b[3] = t * t * t;
    }

    // Bilinear interpolation of a packed 4-byte colour across the unit square. Corners are given in the
    // patch order: v0u0 = (u=0,v=0), v0u1 = (u=1,v=0), v1u1 = (u=1,v=1), v1u0 = (u=0,v=1).
    private static uint Bilerp(uint v0u0, uint v0u1, uint v1u1, uint v1u0, float u, float v)
    {
        uint result = 0;
        for (int shift = 24; shift >= 0; shift -= 8)
        {
            float bottom = ((v0u0 >> shift) & 0xFF) * (1 - u) + ((v0u1 >> shift) & 0xFF) * u;
            float top = ((v1u0 >> shift) & 0xFF) * (1 - u) + ((v1u1 >> shift) & 0xFF) * u;
            float f = bottom * (1 - v) + top * v;
            var byteVal = (uint)Math.Clamp((int)MathF.Round(f), 0, 255);
            result |= byteVal << shift;
        }
        return result;
    }

    private static int GetInt(PdfDictionary dict, string key, PdfDocument? document)
    {
        if (!dict.TryGetValue(new PdfName(key), out PdfObject? obj)) return -1;
        if (obj is PdfIndirectReference r && document is not null) obj = document.ResolveReference(r);
        return obj is PdfInteger i ? i.Value : obj is PdfReal d ? (int)d.Value : -1;
    }

    private static double[]? GetNumbers(PdfDictionary dict, string key, PdfDocument? document)
    {
        if (!dict.TryGetValue(new PdfName(key), out PdfObject? obj)) return null;
        if (obj is PdfIndirectReference r && document is not null) obj = document.ResolveReference(r);
        if (obj is not PdfArray arr) return null;
        var nums = new double[arr.Count];
        for (var i = 0; i < arr.Count; i++) nums[i] = arr[i].ToDouble();
        return nums;
    }

    // Big-endian (MSB-first) bit reader over the decoded mesh stream. Reads run continuously; Align()
    // discards the remaining bits of the current byte so the next read starts on a byte boundary.
    private sealed class BitReader(byte[] data)
    {
        private int _bytePos;
        private int _bitPos;     // bits already consumed in the current byte (0..7)

        public bool Eof { get; private set; }
        public bool HasData => _bytePos < data.Length;

        public long Read(int bits)
        {
            long value = 0;
            for (int i = 0; i < bits; i++)
            {
                if (_bytePos >= data.Length) { Eof = true; return value; }
                int bit = (data[_bytePos] >> (7 - _bitPos)) & 1;
                value = (value << 1) | (uint)bit;
                if (++_bitPos == 8) { _bitPos = 0; _bytePos++; }
            }
            return value;
        }

        public void Align()
        {
            if (_bitPos == 0) return;
            _bitPos = 0;
            _bytePos++;
        }
    }
}
