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
        // Style comes from the descriptor AND the name; Classify already merges both. The provider
        // owns the ladder from here — this method no longer knows anything about filenames.
        (bool _, bool _, bool bold, bool italic) = Classify(baseFont, descriptor);

        FontMatch? match = provider.Resolve(new FontRequest(baseFont, bold, italic));
        if (match is null) return null;

        var metrics = new EmbeddedFontMetrics(match.Data, match.FaceIndex);
        return metrics.IsValid ? metrics : null;
    }

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
