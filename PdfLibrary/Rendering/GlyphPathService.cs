using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using PdfLibrary.Fonts.Embedded;
using CffGlyphOutline = FontParser.Tables.Cff.GlyphOutline;
using GlyphOutline = PdfLibrary.Fonts.Embedded.GlyphOutline;

namespace PdfLibrary.Rendering;

/// <summary>
/// Builds and caches glyph-space <see cref="IPathBuilder"/> paths for embedded-font glyphs
/// (SkiaSharp-free port of TextRenderer.GetCachedGlyphPath). The cached path is canonical and
/// immutable — callers position a copy via <see cref="IPathBuilder.Transform"/>, never mutate it.
/// </summary>
internal sealed class GlyphPathService
{
    private readonly ConcurrentDictionary<string, IPathBuilder> _cache = new();

    public IPathBuilder GetGlyphPath(EmbeddedFontMetrics metrics, ushort glyphId, float fontSize,
        GlyphOutline ttOutline, string? resolvedGlyphName)
    {
        // Identity-based key: different subsets can share a BaseFont; 0.1pt size precision.
        int fontId = RuntimeHelpers.GetHashCode(metrics);
        var roundedSize = (int)(fontSize * 10);
        var key = $"{fontId}_{glyphId}_{roundedSize}";

        if (_cache.TryGetValue(key, out IPathBuilder? cached))
            return cached;

        IPathBuilder path = Build(metrics, glyphId, fontSize, ttOutline, resolvedGlyphName);
        return _cache.GetOrAdd(key, path);
    }

    private static IPathBuilder Build(EmbeddedFontMetrics metrics, ushort glyphId, float fontSize,
        GlyphOutline ttOutline, string? resolvedGlyphName)
    {
        ushort upm = metrics.UnitsPerEm;

        if (metrics.IsCffFont)
        {
            CffGlyphOutline? cff = metrics.GetCffGlyphOutlineDirect(glyphId);
            return cff is not null
                ? GlyphOutlineToPath.FromCff(cff, fontSize, upm)
                : GlyphOutlineToPath.FromTrueType(ttOutline, fontSize, upm);
        }

        if (metrics.IsType1Font && resolvedGlyphName is not null)
        {
            CffGlyphOutline? t1 = metrics.GetType1GlyphOutlineDirect(resolvedGlyphName);
            return t1 is not null
                ? GlyphOutlineToPath.FromCff(t1, fontSize, upm)
                : GlyphOutlineToPath.FromTrueType(ttOutline, fontSize, upm);
        }

        return GlyphOutlineToPath.FromTrueType(ttOutline, fontSize, upm);
    }
}
