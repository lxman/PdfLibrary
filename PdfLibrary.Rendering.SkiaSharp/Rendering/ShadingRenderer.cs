using System.Numerics;
using PdfLibrary.Content;
using SkiaSharp;

namespace PdfLibrary.Rendering.SkiaSharp.Rendering;

/// <summary>
/// Paints PDF axial/radial shadings with SkiaSharp gradients — both the <c>sh</c> operator
/// (fill the current clip) and PatternType 2 shading-pattern fills (clip to a path, then paint
/// the gradient in pattern space).
/// </summary>
internal sealed class ShadingRenderer
{
    private readonly SKCanvas _canvas;
    private readonly Func<Matrix3x2> _getInitialTransform;

    public ShadingRenderer(SKCanvas canvas, Func<Matrix3x2> getInitialTransform)
    {
        _canvas = canvas;
        _getInitialTransform = getInitialTransform;
    }

    /// <summary>
    /// <c>sh</c> operator: paint the shading across the current clip. The shading's coordinates are
    /// in the current user space, which the canvas matrix already maps to device, so the gradient is
    /// built with the raw coords and the canvas matrix is left untouched.
    /// </summary>
    public void PaintShading(ShadingDescriptor shading, PdfGraphicsState state)
    {
        // Mesh shadings (type 6/7): the coords are in the current user space, which the canvas matrix
        // already maps to device (as for the gradient path), so Gouraud-fill with the raw coords.
        if (shading.MeshTriangles.Length > 0) { DrawMesh(shading, state); return; }

        using SKShader? shader = BuildShader(shading);
        if (shader is null) return;

        using var paint = new SKPaint { Shader = shader, IsAntialias = true, Color = AlphaColor(state.FillAlpha) };
        _canvas.DrawPaint(paint);
    }

    /// <summary>
    /// Shading-pattern fill: clip to the filled path in the current user space, then paint the
    /// gradient positioned by the pattern matrix (which maps pattern space → the page's default user
    /// space, independent of the current CTM).
    /// </summary>
    public void FillPathWithShadingPattern(SKPath skPath, ShadingDescriptor shading, PdfGraphicsState state)
    {
        bool isMesh = shading.MeshTriangles.Length > 0;
        using SKShader? shader = isMesh ? null : BuildShader(shading);
        if (!isMesh && shader is null) return;

        int save = _canvas.Save();
        _canvas.ClipPath(skPath, SKClipOperation.Intersect, antialias: true);

        Matrix3x2 m = (shading.PatternMatrix ?? Matrix3x2.Identity) * _getInitialTransform();
        _canvas.SetMatrix(new SKMatrix(m.M11, m.M21, m.M31, m.M12, m.M22, m.M32, 0, 0, 1));

        if (isMesh) { DrawMesh(shading, state); _canvas.RestoreToCount(save); return; }

        using var paint = new SKPaint { Shader = shader, IsAntialias = true, Color = AlphaColor(state.FillAlpha) };
        _canvas.DrawPaint(paint);

        _canvas.RestoreToCount(save);
    }

    // Gouraud-fills a mesh shading's triangle soup with per-vertex sRGB (fill alpha baked in). A white
    // paint under SKBlendMode.Modulate passes the vertex colours through unchanged.
    private void DrawMesh(ShadingDescriptor shading, PdfGraphicsState state)
    {
        MeshVertex[] tris = shading.MeshTriangles;
        var pts = new SKPoint[tris.Length];
        var cols = new SKColor[tris.Length];
        var alpha = (byte)(Math.Clamp(state.FillAlpha, 0.0, 1.0) * 255);
        for (var i = 0; i < tris.Length; i++)
        {
            pts[i] = new SKPoint(tris[i].X, tris[i].Y);
            uint rgb = tris[i].Rgb;
            cols[i] = new SKColor((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF), alpha);
        }
        using SKVertices vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, pts, cols);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        _canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
    }

    // Paint colour is ignored for RGB when a shader is set, but its alpha modulates the shader output.
    private static SKColor AlphaColor(double fillAlpha) => new(0, 0, 0, (byte)(Math.Clamp(fillAlpha, 0.0, 1.0) * 255));

    private static SKShader? BuildShader(ShadingDescriptor shading)
    {
        if (shading.Colors.Length == 0 || shading.Coords.Length == 0) return null;

        var colors = new SKColor[shading.Colors.Length];
        for (var i = 0; i < colors.Length; i++) colors[i] = new SKColor(shading.Colors[i]);

        // PDF /Extend maps to gradient tiling: extend-both clamps the end colours outward; otherwise
        // the shading is undefined past its axis, which Decal renders transparent.
        SKShaderTileMode mode = shading is { ExtendStart: true, ExtendEnd: true }
            ? SKShaderTileMode.Clamp
            : SKShaderTileMode.Decal;

        float[] c = shading.Coords;
        return shading.ShadingType switch
        {
            2 => SKShader.CreateLinearGradient(
                new SKPoint(c[0], c[1]), new SKPoint(c[2], c[3]), colors, shading.Stops, mode),
            3 => SKShader.CreateTwoPointConicalGradient(
                new SKPoint(c[0], c[1]), c[2], new SKPoint(c[3], c[4]), c[5], colors, shading.Stops, mode),
            _ => null
        };
    }
}
