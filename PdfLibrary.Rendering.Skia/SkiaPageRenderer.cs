using System.Numerics;
using System.Runtime.InteropServices;
using PdfLibrary.Content;
using PdfLibrary.Rendering;
using SkiaSharp;

namespace PdfLibrary.Rendering.Skia;

/// <summary>
/// Immediate-mode Skia renderer that walks a thread-agnostic <see cref="PageDrawList"/> onto an
/// <see cref="SKCanvas"/>. Geometry/colour/stroke/clip/image logic mirrors the reference render path
/// exactly (paths arrive CTM-baked in PDF user space; the page transform is applied once as the base
/// matrix). Because the whole page is drawn in one pass, blend modes (Task 4) and soft masks (Task 6)
/// composite against the page's own pixels.
/// </summary>
public static class SkiaPageRenderer
{
    public static void Render(SKCanvas canvas, PageDrawList list)
    {
        BeginPageArgs b = list.Begin;
        Matrix3x2 page = PageTransform.Build(b.Width, b.Height, b.Scale, b.CropOffsetX, b.CropOffsetY, b.Rotation);
        int save = canvas.Save();
        // Concat (compose) the page transform onto the canvas's EXISTING matrix — never SetMatrix,
        // which would overwrite the position + DPI transform the windowed PageControl's leased canvas
        // carries (drawing the page at the layer origin / wrong scale). On a fresh test surface the
        // base matrix is identity, so Concat ≡ the old SetMatrix there.
        canvas.Concat(SkiaConversions.ToSkMatrix(page));

        // Isolated page group: if any command uses a non-Normal blend mode, replay the page into an
        // isolated (transparent-backdrop) layer, then composite it SrcOver onto the canvas. Without this,
        // white-preserving modes (Screen/Overlay/Lighten/ColorDodge...) compose against the opaque white
        // page-sheet the app draws behind the control, so Screen(white,src)=white erases the shape over
        // bare paper — the BlendModes.pdf p1 defect. Adobe/Chrome treat the page as an isolated group.
        // Guarded so ordinary (Normal-only) pages skip the extra full-page offscreen allocation.
        bool isolate = NeedsIsolation(list);
        if (isolate) canvas.SaveLayer();

        var maskStack = new Stack<(string Subtype, PageDrawList Mask)>();

        // Per-command handling as a local function so a GroupCommand can replay its content inline without
        // re-applying the page transform (recursing into Render would double it).
        void Handle(DrawCommand cmd)
        {
            switch (cmd)
            {
                case FillCommand f:        Fill(canvas, f.Segments, f.EvenOdd, f.State); break;
                case StrokeCommand s:      Stroke(canvas, s.Segments, s.State); break;
                case FillStrokeCommand fs: Fill(canvas, fs.Segments, fs.EvenOdd, fs.State); Stroke(canvas, fs.Segments, fs.State); break;
                case TilingFillCommand t:
                    if (t.Content is null) Fill(canvas, t.Segments, t.EvenOdd, t.State);  // no tile captured: flat-fill fallback
                    else TileFill(canvas, t, Handle);
                    break;
                case ClipCommand c:
                    using (SKPath clip = SkiaConversions.ToSkPath(c.Segments, c.EvenOdd))
                        canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
                    break;
                case SaveCommand:          canvas.Save(); break;
                case RestoreCommand:       canvas.Restore(); break;
                case ImageCommand i:       DrawImage(canvas, i); break;
                case ShadingCommand sh:            PaintShading(canvas, sh); break;
                case ShadingPatternFillCommand sp: FillWithShadingPattern(canvas, sp); break;
                case SoftMaskPushCommand pm:
                    maskStack.Push((pm.Subtype, pm.Mask));
                    canvas.SaveLayer();                       // content drawn next goes into an isolated layer
                    break;
                case SoftMaskPopCommand:
                    if (maskStack.Count > 0) ApplyMaskAndRestore(canvas, maskStack.Pop());
                    break;
                case GroupCommand grp:
                {
                    // Transparency group (§11.4): composite the content as an isolated UNIT under the group
                    // blend mode + constant alpha. Skia SaveLayer is always isolated, so non-isolated and
                    // knockout groups render as isolated on this RGB display path — the CMYK compositor is the
                    // spec-exact soft-proof; this is the documented display asymmetry. Any outer /SMask is
                    // already carried by the surrounding SoftMaskPush/Pop layer and applies on restore.
                    // Visible symptom: because Skia blends in RGB (not the fixture's DeviceCMYK TBCS) over a
                    // transparent isolated backdrop, Difference/Exclusion group blends leave the raw source
                    // colour showing (GWG162 bold magenta X); most other modes degrade only to a faint X.
                    // Not fixable by flattening (regresses the other modes) — pages that need this route to the
                    // CMYK raster in Auto/Always (OverprintDetector.NeedsGroupCompositing); Never mode shows this.
                    SKBlendMode gm = BlendModeMap.FromPdf(grp.Info.BlendMode);
                    byte ga = PdfColorToRgb.AlphaByte(grp.Info.ConstantAlpha);
                    // A layer is needed when the group's own composite is non-trivial (a blend or ca < 1) OR
                    // the group is ISOLATED and its content contains a blend — an isolated group's inner blends
                    // must see the group's transparent backdrop, not whatever is on the page, so it cannot be
                    // flattened inline. A non-isolated Normal/opaque group composites against the page anyway,
                    // so flattening it is exact.
                    bool needsLayer = gm != SKBlendMode.SrcOver || ga != 255
                                      || (grp.Info.Isolated && NeedsIsolation(grp.Content));
                    if (!needsLayer)
                    {
                        foreach (DrawCommand inner in grp.Content.Commands) Handle(inner);
                    }
                    else
                    {
                        using var paint = new SKPaint { BlendMode = gm, Color = new SKColor(0, 0, 0, ga) };
                        canvas.SaveLayer(paint);
                        foreach (DrawCommand inner in grp.Content.Commands) Handle(inner);
                        canvas.Restore();      // composite the (isolated) group layer under its blend / ca
                    }
                    break;
                }
            }
        }

        foreach (DrawCommand cmd in list.Commands) Handle(cmd);

        if (isolate) canvas.Restore();   // composite the isolated page group onto the canvas
        canvas.RestoreToCount(save);
    }

    /// <summary>True iff any draw command uses a blend mode other than Normal — i.e. one that maps to a
    /// non-SrcOver <see cref="SKBlendMode"/>. Such pages must render as an isolated group so blends
    /// compose against a transparent backdrop, not the opaque page-sheet the app paints behind the page.</summary>
    private static bool NeedsIsolation(PageDrawList list)
    {
        foreach (DrawCommand cmd in list.Commands)
        {
            string? bm = cmd switch
            {
                FillCommand f        => f.State.BlendMode,
                StrokeCommand s      => s.State.BlendMode,
                FillStrokeCommand fs => fs.State.BlendMode,
                TilingFillCommand t  => t.State.BlendMode,
                ShadingCommand sh    => sh.State.BlendMode,
                ShadingPatternFillCommand sp => sp.State.BlendMode,
                GroupCommand g       => g.Info.BlendMode,   // a group's own blend composes onto the page sheet
                _ => null,
            };
            if (bm is not null && BlendModeMap.FromPdf(bm) != SKBlendMode.SrcOver) return true;
            // Recurse into group content: a blend inside a trivial (flattened) group also lands on the page,
            // and nested group blends need the page isolated so white-preserving modes don't erase over paper.
            if (cmd is GroupCommand grp && NeedsIsolation(grp.Content)) return true;
        }
        return false;
    }

    /// <summary>Composite the just-drawn content layer through a soft mask. The mask content is
    /// rendered to its own layer; luminosity masks convert RGB luminance to alpha, then the mask is
    /// painted DstIn (keeps content where mask alpha is high) and both layers are restored.</summary>
    private static void ApplyMaskAndRestore(SKCanvas canvas, (string Subtype, PageDrawList Mask) m)
    {
        using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
        if (string.Equals(m.Subtype, "Luminosity", StringComparison.OrdinalIgnoreCase))
        {
            using var luma = SKColorFilter.CreateLumaColor();
            maskPaint.ColorFilter = luma;   // luminance -> alpha
        }
        canvas.SaveLayer(maskPaint);          // mask layer; composited DstIn onto the content layer
        Render(canvas, m.Mask);               // draw mask content (recursive; its own page transform)
        canvas.Restore();                     // applies DstIn (mask) onto content layer
        canvas.Restore();                     // pops the content layer from the push
    }

    /// <summary>
    /// Faithful tiling-pattern fill: clip to the fill path, map into pattern space via the pattern matrix,
    /// and replay the captured tile content on the XStep/YStep lattice covering the region (ISO 32000-1
    /// §8.7.3.1). A single tile whose steps exceed the region — cairo's shape-fill idiom — draws once. The
    /// pattern matrix is treated as pattern→user space; patterns whose parent is a transformed form space
    /// are approximate. Colours come from the tile content (PaintType 1); PaintType 2 recolouring is TODO.
    /// </summary>
    private static void TileFill(SKCanvas canvas, TilingFillCommand t, Action<DrawCommand> handle)
    {
        using SKPath clip = SkiaConversions.ToSkPath(t.Segments, t.EvenOdd);
        SKRect bounds = clip.Bounds;
        if (bounds.IsEmpty || t.Content is null) return;

        int save = canvas.Save();
        canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
        SKMatrix pm = SkiaConversions.ToSkMatrix(t.PatternMatrix);
        canvas.Concat(pm);                                       // pattern space -> user space

        // Which tiles cover the fill region? Map the user-space clip bounds back into pattern space.
        if (!pm.TryInvert(out SKMatrix inv)) inv = SKMatrix.CreateIdentity();
        SKRect pb = inv.MapRect(bounds);

        float xs = Math.Abs(t.XStep), ys = Math.Abs(t.YStep);
        bool singleTile = xs < 1e-3f || ys < 1e-3f || (xs >= pb.Width && ys >= pb.Height);

        void DrawTileOnce()
        {
            foreach (DrawCommand inner in t.Content!.Commands) handle(inner);
        }

        if (singleTile)
        {
            DrawTileOnce();
            canvas.RestoreToCount(save);
            return;
        }

        // A tile paints its content over [origin + bboxMin, origin + bboxMax] in pattern space. When the
        // BBox is larger than the step (overlapping tiles) or the visible mark sits away from the origin,
        // tiles whose ORIGIN is outside the region still reach into it, so iterate every tile whose BBox
        // overlaps the region — not just origins within it. Otherwise a ~BBox-tall band is dropped at an
        // edge (veraPDF 6-2-4-3-t02-pass-f). Zero BBox (unset) reduces to the plain origins-in-region range.
        float bMinX = Math.Min(t.BBoxMinX, t.BBoxMaxX), bMaxX = Math.Max(t.BBoxMinX, t.BBoxMaxX);
        float bMinY = Math.Min(t.BBoxMinY, t.BBoxMaxY), bMaxY = Math.Max(t.BBoxMinY, t.BBoxMaxY);
        int i0 = (int)Math.Floor((pb.Left - bMaxX) / xs), i1 = (int)Math.Ceiling((pb.Right - bMinX) / xs);
        int j0 = (int)Math.Floor((pb.Top - bMaxY) / ys),  j1 = (int)Math.Ceiling((pb.Bottom - bMinY) / ys);
        if ((long)(i1 - i0 + 1) * (j1 - j0 + 1) > 4096)          // runaway guard -> one tile
        {
            DrawTileOnce();
            canvas.RestoreToCount(save);
            return;
        }

        for (int j = j0; j <= j1; j++)
        for (int i = i0; i <= i1; i++)
        {
            int s2 = canvas.Save();
            canvas.Translate(i * xs, j * ys);
            DrawTileOnce();
            canvas.RestoreToCount(s2);
        }
        canvas.RestoreToCount(save);
    }

    private static void Fill(SKCanvas canvas, IReadOnlyList<PathSegment> segs, bool evenOdd, PdfGraphicsState state)
    {
        using SKPath path = SkiaConversions.ToSkPath(segs, evenOdd);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = SkiaConversions.ToSkColor(state.ResolvedFillColor, state.ResolvedFillColorSpace, state.FillAlpha),
            BlendMode = BlendModeMap.FromPdf(state.BlendMode),
        };
        canvas.DrawPath(path, paint);
    }

    private static void Stroke(SKCanvas canvas, IReadOnlyList<PathSegment> segs, PdfGraphicsState state)
    {
        // Match AvaloniaRenderTarget: width = LineWidth * sqrt(|det CTM|), floor 0.1.
        double ctmScale = Math.Sqrt(Math.Abs(state.Ctm.M11 * state.Ctm.M22 - state.Ctm.M12 * state.Ctm.M21));
        var width = (float)Math.Max(state.LineWidth * ctmScale, 0.1);
        using SKPath path = SkiaConversions.ToSkPath(segs, evenOdd: false);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            StrokeCap = Cap(state.LineCap),
            StrokeJoin = Join(state.LineJoin),
            StrokeMiter = (float)state.MiterLimit,
            Color = SkiaConversions.ToSkColor(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace, state.StrokeAlpha),
            BlendMode = BlendModeMap.FromPdf(state.BlendMode),
        };
        if (state.DashPattern is { Length: > 0 })
        {
            float[] intervals = state.DashPattern.Select(d => (float)(d * ctmScale)).ToArray();
            // A PDF dash array may legally be odd-length — it is applied cyclically (e.g. [3] =
            // 3 on, 3 off, 3 on, ...). SkiaSharp.CreateDash requires an even count, so repeat the
            // array to form a full even cycle. Skia also rejects all-zero/negative arrays; per the
            // PDF spec such a dash array yields a solid line, so fall through without a dash effect.
            if (intervals.Length % 2 != 0)
                intervals = [.. intervals, .. intervals];
            if (intervals.All(v => v >= 0) && intervals.Any(v => v > 0))
                paint.PathEffect = SKPathEffect.CreateDash(intervals, (float)(state.DashPhase * ctmScale));
        }
        canvas.DrawPath(path, paint);
    }

    private static SKStrokeCap  Cap(int c) => c switch { 1 => SKStrokeCap.Round,  2 => SKStrokeCap.Square, _ => SKStrokeCap.Butt };
    private static SKStrokeJoin Join(int j) => j switch { 1 => SKStrokeJoin.Round, 2 => SKStrokeJoin.Bevel, _ => SKStrokeJoin.Miter };

    /// <summary>sh operator: paint the shading across the current clip. The canvas already carries the
    /// page matrix; the shading's coords are in the user space of the CTM captured at sh time, so we
    /// concat that CTM (paths elsewhere arrive CTM-baked, but shading coords do not).</summary>
    private static void PaintShading(SKCanvas canvas, ShadingCommand cmd)
    {
        // Mesh shadings (type 6/7): Gouraud-fill the pre-tessellated triangles, positioned by the CTM
        // captured at sh time (matching the gradient path's Concat below).
        if (cmd.Shading.MeshTriangles.Length > 0)
        {
            int meshSave = canvas.Save();
            canvas.Concat(SkiaConversions.ToSkMatrix(cmd.State.Ctm));
            DrawMesh(canvas, cmd.Shading, cmd.State);
            canvas.RestoreToCount(meshSave);
            return;
        }

        using SKShader? shader = BuildShader(cmd.Shading);
        if (shader is null) return;

        int save = canvas.Save();
        canvas.Concat(SkiaConversions.ToSkMatrix(cmd.State.Ctm));
        using var paint = new SKPaint
        {
            Shader = shader,
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, PdfColorToRgb.AlphaByte(cmd.State.FillAlpha)),
            BlendMode = BlendModeMap.FromPdf(cmd.State.BlendMode),
        };
        canvas.DrawPaint(paint);   // fills the current clip
        canvas.RestoreToCount(save);
    }

    /// <summary>PatternType 2 shading-pattern fill: clip to the (CTM-baked) path, then paint the gradient
    /// positioned by the pattern matrix (pattern space → page default user space), independent of the CTM.</summary>
    private static void FillWithShadingPattern(SKCanvas canvas, ShadingPatternFillCommand cmd)
    {
        bool isMesh = cmd.Shading.MeshTriangles.Length > 0;
        using SKShader? shader = isMesh ? null : BuildShader(cmd.Shading);
        if (!isMesh && shader is null) return;

        int save = canvas.Save();
        using (SKPath clip = SkiaConversions.ToSkPath(cmd.Segments, cmd.EvenOdd))
            canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
        canvas.Concat(SkiaConversions.ToSkMatrix(cmd.Shading.PatternMatrix ?? Matrix3x2.Identity));
        if (isMesh)
        {
            DrawMesh(canvas, cmd.Shading, cmd.State);
            canvas.RestoreToCount(save);
            return;
        }
        using var paint = new SKPaint
        {
            Shader = shader,
            IsAntialias = true,
            Color = new SKColor(0, 0, 0, PdfColorToRgb.AlphaByte(cmd.State.FillAlpha)),
            BlendMode = BlendModeMap.FromPdf(cmd.State.BlendMode),
        };
        canvas.DrawPaint(paint);
        canvas.RestoreToCount(save);
    }

    // Gouraud-fills a mesh shading's triangle soup. Vertex colours (sRGB) carry the fill alpha; a white
    // paint under SKBlendMode.Modulate passes them through unchanged, then the paint's PDF blend mode
    // composites the result onto the backdrop. Positions are in the shading target space already mapped
    // by the caller's canvas matrix (CTM for sh, pattern matrix for a pattern).
    private static void DrawMesh(SKCanvas canvas, ShadingDescriptor shading, PdfGraphicsState state)
    {
        MeshVertex[] tris = shading.MeshTriangles;
        var pts = new SKPoint[tris.Length];
        var cols = new SKColor[tris.Length];
        byte alpha = PdfColorToRgb.AlphaByte(state.FillAlpha);
        for (var i = 0; i < tris.Length; i++)
        {
            pts[i] = new SKPoint(tris[i].X, tris[i].Y);
            uint rgb = tris[i].Rgb;
            cols[i] = new SKColor((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF), alpha);
        }
        using SKVertices vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, pts, cols);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White, BlendMode = BlendModeMap.FromPdf(state.BlendMode) };
        canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
    }

    /// <summary>Builds an axial (type 2) or radial (type 3) SkiaSharp gradient from the descriptor.
    /// Mirrors PdfLibrary.Rendering.SkiaSharp.ShadingRenderer: /Extend both → Clamp, else Decal.</summary>
    private static SKShader? BuildShader(ShadingDescriptor shading)
    {
        if (shading.Colors.Length == 0 || shading.Coords.Length == 0) return null;

        var colors = new SKColor[shading.Colors.Length];
        for (var i = 0; i < colors.Length; i++) colors[i] = new SKColor(shading.Colors[i]);

        SKShaderTileMode mode = shading is { ExtendStart: true, ExtendEnd: true }
            ? SKShaderTileMode.Clamp
            : SKShaderTileMode.Decal;

        float[] c = shading.Coords;
        return shading.ShadingType switch
        {
            2 when c.Length >= 4 => SKShader.CreateLinearGradient(
                new SKPoint(c[0], c[1]), new SKPoint(c[2], c[3]), colors, shading.Stops, mode),
            3 when c.Length >= 6 => SKShader.CreateTwoPointConicalGradient(
                new SKPoint(c[0], c[1]), c[2], new SKPoint(c[3], c[4]), c[5], colors, shading.Stops, mode),
            _ => null,
        };
    }

    private static void DrawImage(SKCanvas canvas, ImageCommand i)
    {
        // RGBA8888 top-row-first → SKBitmap. Draw unit square under (yflip * ctm), like AvaloniaRenderTarget.
        // SkiaSharp 3.x removed FilterQuality; use SKSamplingOptions instead.
        var info = new SKImageInfo(i.Width, i.Height, SKColorType.Rgba8888,
            i.Alpha == AlphaMode.Premultiplied ? SKAlphaType.Premul
            : i.Alpha == AlphaMode.Opaque ? SKAlphaType.Opaque
            : SKAlphaType.Unpremul);
        using var bmp = new SKBitmap();
        GCHandle pinned = System.Runtime.InteropServices.GCHandle.Alloc(i.Rgba, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bmp.InstallPixels(info, pinned.AddrOfPinnedObject(), info.RowBytes);
            // canvas already has page matrix set. Concat(yflip*ctm) post-multiplies: new_CTM = old_CTM * (yflip*ctm).
            // Points in unit square go through (yflip*ctm) first, then page matrix — matching AvaloniaRenderTarget.
            var yflip = new Matrix3x2(1, 0, 0, -1, 0, 1);
            Matrix3x2 combined = yflip * i.Ctm;
            int imgSave = canvas.Save();
            canvas.Concat(SkiaConversions.ToSkMatrix(combined));
            // SkiaSharp 3.x: DrawBitmap(bitmap, SKRect, SKPaint) exists; SKSamplingOptions is via DrawImage(SKImage).
            // Convert to SKImage to use the SKSamplingOptions overload (DrawImage(img, dest, sampling, paint)).
            using SKImage? skImg = SKImage.FromBitmap(bmp);
            // null paint is deliberate: image antialias/sampling handled by SKSamplingOptions, matching the retained ImageDrawing which applies no explicit paint
            canvas.DrawImage(skImg, new SKRect(0, 0, 1, 1), new SKSamplingOptions(SKFilterMode.Linear), null);
            canvas.RestoreToCount(imgSave);
        }
        finally { pinned.Free(); }
    }
}
