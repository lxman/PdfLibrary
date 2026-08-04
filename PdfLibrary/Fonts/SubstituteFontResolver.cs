using System.Collections.Concurrent;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Fonts;

/// <summary>
/// Resolves a non-embedded PDF font to a system substitute font, parsed as EmbeddedFontMetrics so
/// the core text pipeline can render its glyph outlines exactly like an embedded font. SkiaSharp-free:
/// font classification ported from TextRenderer.RenderWithFallbackFont, byte loading via
/// ISystemFontProvider (Plan A's SystemFontLocator). Cached by BaseFont so the same substitute
/// instance is reused (keeps GlyphPathService's identity-keyed cache effective).
/// </summary>
internal sealed class SubstituteFontResolver(ISystemFontProvider provider)
{
    private readonly ConcurrentDictionary<string, EmbeddedFontMetrics?> _cache = new();

    public EmbeddedFontMetrics? Resolve(string baseFont, PdfFontDescriptor? descriptor)
        => _cache.GetOrAdd(baseFont ?? "", _ => Load(baseFont ?? "", descriptor));

    private EmbeddedFontMetrics? Load(string baseFont, PdfFontDescriptor? descriptor)
    {
        // Classified up front, not just on the fallback branch: the style is needed to pick the FACE
        // even when the raw BaseFont resolves, because the file it resolves to may be a collection.
        (bool serif, bool mono, bool bold, bool italic) = Classify(baseFont, descriptor);

        // Try the raw BaseFont first (resolves genuine Standard-14 names incl. Symbol/ZapfDingbats
        // precisely), then a synthetic name from classification (covers arbitrary subset names).
        byte[]? bytes = provider.GetFontData(baseFont)
                        ?? provider.GetFontData(SyntheticStd14Name(serif, mono, bold, italic));
        if (bytes is null) return null;

        EmbeddedFontMetrics? metrics = SelectFace(bytes, bold, italic);
        return metrics is { IsValid: true } ? metrics : null;
    }

    /// <summary>Opens the face of <paramref name="bytes"/> whose own style bits best match the requested
    /// style. A bare sfnt has one face and this is a no-op; a TrueType Collection is the case that
    /// matters, since its styles share a single file and face 0 is the upright regular.
    ///
    /// <para>Scored rather than matched exactly so a collection lacking the exact combination still
    /// degrades sensibly (an italic request against a Regular/Bold-only collection keeps face 0 instead
    /// of failing). Ties keep the LOWEST face index, so a collection whose faces are indistinguishable
    /// resolves exactly as it did before this existed.</para></summary>
    private static EmbeddedFontMetrics? SelectFace(byte[] bytes, bool bold, bool italic)
    {
        var face0 = new EmbeddedFontMetrics(bytes);

        int faceCount;
        try { faceCount = new FontParser.SfntFont(bytes).FaceCount; }
        catch { return face0; }
        if (faceCount <= 1) return face0;

        EmbeddedFontMetrics best = face0;
        int bestScore = Score(face0, bold, italic);
        for (var i = 1; i < faceCount && bestScore < 2; i++)
        {
            EmbeddedFontMetrics candidate;
            try { candidate = new EmbeddedFontMetrics(bytes, i); }
            catch { continue; }
            if (!candidate.IsValid) continue;

            int score = Score(candidate, bold, italic);
            if (score <= bestScore) continue;
            best = candidate;
            bestScore = score;
        }
        return best;
    }

    private static int Score(EmbeddedFontMetrics face, bool bold, bool italic)
        => (face.IsItalic == italic ? 1 : 0) + (face.IsBold == bold ? 1 : 0);

    public static (bool serif, bool mono, bool bold, bool italic) Classify(
        string baseFont, PdfFontDescriptor? descriptor)
    {
        var bold = false; var italic = false; var serif = false; var mono = false;
        if (descriptor is not null)
        {
            bold = descriptor.IsBold || descriptor.StemV >= 120;
            italic = descriptor.IsItalic;
            serif = descriptor.IsSerif;
            mono = descriptor.IsFixedPitch;
        }

        string name = baseFont ?? "";
        if (name.Contains("Bold", StringComparison.OrdinalIgnoreCase)) bold = true;
        if (name.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Oblique", StringComparison.OrdinalIgnoreCase)) italic = true;
        if (name.Contains("Courier", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Consolas", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Monaco", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Mono", StringComparison.OrdinalIgnoreCase)) mono = true;
        if (name.Contains("Times", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Serif", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Georgia", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Palatino", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Garamond", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Cambria", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Bodoni", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Century", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Bookman", StringComparison.OrdinalIgnoreCase)) serif = true;

        return (serif, mono, bold, italic);
    }

    public static string SyntheticStd14Name(bool serif, bool mono, bool bold, bool italic)
    {
        string family = mono ? "Courier" : serif ? "Times" : "Helvetica";
        string style = (bold, italic) switch
        {
            (true, true) => "-BoldItalic",
            (true, false) => "-Bold",
            (false, true) => "-Italic",
            _ => ""
        };
        return family + style;
    }
}
