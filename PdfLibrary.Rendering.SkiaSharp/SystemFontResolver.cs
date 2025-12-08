using Logging;
using PdfLibrary.Fonts;
using SkiaSharp;

namespace PdfLibrary.Rendering.SkiaSharp;

/// <summary>
/// Resolves PDF font names to system fonts with fallback support.
/// Maps the 14 standard PDF fonts to system equivalents and provides
/// fallback chains when preferred fonts are unavailable.
/// </summary>
public class SystemFontResolver
{
    private readonly ISystemFontProvider _fontProvider;
    private readonly Dictionary<string, SKTypeface> _typefaceCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    // Fallback chains for different font categories
    // Listed in order of preference - first available wins
    // Prioritize metric-compatible Base14 replacements (URW, Liberation, TeX Gyre)
    // These fonts have identical metrics to Adobe's Base14 fonts
    private static readonly string[] SansSerifFallbacks =
    {
        // Metric-compatible Helvetica replacements (HIGHEST PRIORITY)
        "Nimbus Sans L",           // URW font, exact Helvetica metrics
        "Nimbus Sans",             // Newer URW variant
        "Liberation Sans",         // Red Hat, based on URW
        "TeX Gyre Heros",          // TeX font, based on URW
        "FreeSans",                // GNU FreeFont

        // System fonts (FALLBACK - different metrics!)
        "Arial",                   // Windows default (metrics differ from Helvetica)
        "Helvetica",               // macOS
        "DejaVu Sans",
        "Noto Sans",
        "Segoe UI"
    };

    private static readonly string[] SerifFallbacks =
    {
        // Metric-compatible Times replacements (HIGHEST PRIORITY)
        "Nimbus Roman No9 L",      // URW font, exact Times metrics
        "Nimbus Roman",            // Newer URW variant
        "Liberation Serif",        // Red Hat, based on URW
        "TeX Gyre Termes",         // TeX font, based on URW
        "FreeSerif",               // GNU FreeFont

        // System fonts (FALLBACK - different metrics!)
        "Times New Roman",         // Windows default (metrics differ from Times-Roman)
        "Times",                   // macOS
        "DejaVu Serif",
        "Noto Serif",
        "Georgia"
    };

    private static readonly string[] MonospaceFallbacks =
    {
        // Metric-compatible Courier replacements (HIGHEST PRIORITY)
        "Nimbus Mono L",           // URW font, exact Courier metrics
        "Nimbus Mono PS",          // Newer URW variant
        "Liberation Mono",         // Red Hat, based on URW
        "TeX Gyre Cursor",         // TeX font, based on URW
        "FreeMono",                // GNU FreeFont

        // System fonts (FALLBACK - different metrics!)
        "Courier New",             // Windows default (metrics differ from Courier)
        "Courier",                 // macOS
        "Consolas",
        "DejaVu Sans Mono",
        "Noto Sans Mono",
        "Lucida Console"
    };

    private static readonly string[] SymbolFallbacks =
    {
        "Symbol",
        "Segoe UI Symbol",
        "DejaVu Sans"
    };

    private static readonly string[] DingbatsFallbacks =
    {
        "Zapf Dingbats",
        "ZapfDingbats",
        "Wingdings",
        "Segoe UI Symbol",
        "DejaVu Sans"
    };

    // Resolved fallback fonts for each category (cached at startup)
    private string? _resolvedSansSerif;
    private string? _resolvedSerif;
    private string? _resolvedMonospace;
    private string? _resolvedSymbol;
    private string? _resolvedDingbats;

    public SystemFontResolver(ISystemFontProvider fontProvider)
    {
        _fontProvider = fontProvider;
        InitializeFallbacks();
    }

    /// <summary>
    /// Creates a SystemFontResolver with the default SkiaFontProvider.
    /// </summary>
    public SystemFontResolver() : this(new SkiaFontProvider())
    {
    }

    /// <summary>
    /// Resolves a PDF font name to an SKTypeface.
    /// </summary>
    /// <param name="pdfFontName">The PDF font name (e.g., "Helvetica", "Courier-Bold").</param>
    /// <param name="bold">Whether bold style is requested.</param>
    /// <param name="italic">Whether italic style is requested.</param>
    /// <returns>An SKTypeface for the resolved font, or a default fallback if none found.</returns>
    public SKTypeface GetTypeface(string pdfFontName, bool bold = false, bool italic = false)
    {
        var cacheKey = $"{pdfFontName}|{bold}|{italic}";

        lock (_lock)
        {
            if (_typefaceCache.TryGetValue(cacheKey, out SKTypeface? cached))
                return cached;
        }

        SKTypeface typeface = ResolveTypeface(pdfFontName, bold, italic);

        lock (_lock)
        {
            _typefaceCache[cacheKey] = typeface;
        }

        return typeface;
    }

    /// <summary>
    /// Gets the resolved system font name for a PDF font category.
    /// Useful for debugging/logging.
    /// </summary>
    public string GetResolvedFontName(FontCategory category)
    {
        return category switch
        {
            FontCategory.SansSerif => _resolvedSansSerif ?? "Unknown",
            FontCategory.Serif => _resolvedSerif ?? "Unknown",
            FontCategory.Monospace => _resolvedMonospace ?? "Unknown",
            FontCategory.Symbol => _resolvedSymbol ?? "Unknown",
            FontCategory.Dingbats => _resolvedDingbats ?? "Unknown",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Determines the font category from a PDF font name.
    /// </summary>
    public static FontCategory GetFontCategory(string pdfFontName)
    {
        if (string.IsNullOrEmpty(pdfFontName))
            return FontCategory.SansSerif;

        string name = pdfFontName.ToUpperInvariant();

        // Monospace detection
        if (name.Contains("COURIER") || name.Contains("CONSOLAS") ||
            name.Contains("MONO") || name.Contains("FIXED"))
        {
            return FontCategory.Monospace;
        }

        // Serif detection
        if (name.Contains("TIMES") || name.Contains("SERIF") ||
            name.Contains("GEORGIA") || name.Contains("PALATINO") ||
            name.Contains("GARAMOND") || name.Contains("CAMBRIA") ||
            name.Contains("BODONI") || name.Contains("CENTURY") ||
            name.Contains("BOOKMAN"))
        {
            return FontCategory.Serif;
        }

        // Symbol detection
        if (name.Contains("SYMBOL"))
        {
            return FontCategory.Symbol;
        }

        // Dingbats detection
        if (name.Contains("DINGBAT") || name.Contains("ZAPF"))
        {
            return FontCategory.Dingbats;
        }

        // Default to sans-serif (Helvetica, Arial, etc.)
        return FontCategory.SansSerif;
    }

    /// <summary>
    /// Extracts style information from a PDF font name.
    /// </summary>
    public static (bool bold, bool italic) GetStyleFromFontName(string pdfFontName)
    {
        if (string.IsNullOrEmpty(pdfFontName))
            return (false, false);

        string name = pdfFontName.ToUpperInvariant();

        bool bold = name.Contains("BOLD");
        bool italic = name.Contains("ITALIC") || name.Contains("OBLIQUE");

        return (bold, italic);
    }

    private void InitializeFallbacks()
    {
        _resolvedSansSerif = _fontProvider.FindFirstAvailable(SansSerifFallbacks);
        _resolvedSerif = _fontProvider.FindFirstAvailable(SerifFallbacks);
        _resolvedMonospace = _fontProvider.FindFirstAvailable(MonospaceFallbacks);
        _resolvedSymbol = _fontProvider.FindFirstAvailable(SymbolFallbacks);
        _resolvedDingbats = _fontProvider.FindFirstAvailable(DingbatsFallbacks);

        PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER] Initialized fallbacks:");
        PdfLogger.Log(LogCategory.Text, $"  Sans-serif: {_resolvedSansSerif ?? "NONE"}");
        PdfLogger.Log(LogCategory.Text, $"  Serif: {_resolvedSerif ?? "NONE"}");
        PdfLogger.Log(LogCategory.Text, $"  Monospace: {_resolvedMonospace ?? "NONE"}");
        PdfLogger.Log(LogCategory.Text, $"  Symbol: {_resolvedSymbol ?? "NONE"}");
        PdfLogger.Log(LogCategory.Text, $"  Dingbats: {_resolvedDingbats ?? "NONE"}");

        // Warn if using system fonts instead of metric-compatible fonts
        bool usingMetricCompatibleSans = _resolvedSansSerif is "Nimbus Sans L" or "Nimbus Sans" or "Liberation Sans" or "TeX Gyre Heros" or "FreeSans";
        bool usingMetricCompatibleSerif = _resolvedSerif is "Nimbus Roman No9 L" or "Nimbus Roman" or "Liberation Serif" or "TeX Gyre Termes" or "FreeSerif";
        bool usingMetricCompatibleMono = _resolvedMonospace is "Nimbus Mono L" or "Nimbus Mono PS" or "Liberation Mono" or "TeX Gyre Cursor" or "FreeMono";

        if (!usingMetricCompatibleSans || !usingMetricCompatibleSerif || !usingMetricCompatibleMono)
        {
            PdfLogger.Log(LogCategory.Text, "");
            PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER] ⚠ WARNING: Metric-compatible Base14 fonts not found!");
            PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER] PDFs may render with incorrect text spacing/layout.");
            PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER] ");
            PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER] For accurate rendering, install one of these font packages:");

            if (!usingMetricCompatibleSans)
            {
                PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER]   • Sans-serif (Helvetica replacement):");
                PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER]     - Liberation Sans (https://github.com/liberationfonts/liberation-fonts)");
                PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER]     - Nimbus Sans L (from URW Base35 fonts)");
                PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER]     - TeX Gyre Heros (https://www.gust.org.pl/projects/e-foundry/tex-gyre)");
            }

            if (!usingMetricCompatibleSerif)
            {
                PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER]   • Serif (Times-Roman replacement):");
                PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER]     - Liberation Serif (https://github.com/liberationfonts/liberation-fonts)");
                PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER]     - Nimbus Roman No9 L (from URW Base35 fonts)");
                PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER]     - TeX Gyre Termes (https://www.gust.org.pl/projects/e-foundry/tex-gyre)");
            }

            if (!usingMetricCompatibleMono)
            {
                PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER]   • Monospace (Courier replacement):");
                PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER]     - Liberation Mono (https://github.com/liberationfonts/liberation-fonts)");
                PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER]     - Nimbus Mono L (from URW Base35 fonts)");
                PdfLogger.Log(LogCategory.Text, "[FONT RESOLVER]     - TeX Gyre Cursor (https://www.gust.org.pl/projects/e-foundry/tex-gyre)");
            }

            PdfLogger.Log(LogCategory.Text, "");
        }
    }

    private SKTypeface ResolveTypeface(string pdfFontName, bool bold, bool italic)
    {
        // Extract style from font name if not explicitly provided
        (bool nameBold, bool nameItalic) = GetStyleFromFontName(pdfFontName);
        bold = bold || nameBold;
        italic = italic || nameItalic;

        // Determine font category
        FontCategory category = GetFontCategory(pdfFontName);

        // Get the resolved system font for this category
        string? systemFontName = category switch
        {
            FontCategory.SansSerif => _resolvedSansSerif,
            FontCategory.Serif => _resolvedSerif,
            FontCategory.Monospace => _resolvedMonospace,
            FontCategory.Symbol => _resolvedSymbol,
            FontCategory.Dingbats => _resolvedDingbats,
            _ => _resolvedSansSerif
        };

        // Build SKFontStyle
        SKFontStyle fontStyle = (bold, italic) switch
        {
            (true, true) => SKFontStyle.BoldItalic,
            (true, false) => SKFontStyle.Bold,
            (false, true) => SKFontStyle.Italic,
            _ => SKFontStyle.Normal
        };

        // Try to create typeface with resolved font
        if (!string.IsNullOrEmpty(systemFontName))
        {
            SKTypeface? typeface = SKTypeface.FromFamilyName(systemFontName, fontStyle);

            // Validate that we actually got the requested font
            if (typeface != null && typeface.FamilyName.Equals(systemFontName, StringComparison.OrdinalIgnoreCase))
            {
                PdfLogger.Log(LogCategory.Text, $"[FONT RESOLVER] Resolved '{pdfFontName}' -> '{systemFontName}' (Category: {category}, Bold: {bold}, Italic: {italic})");
                return typeface;
            }

            // Log if we got a different font than expected (silent substitution)
            if (typeface != null)
            {
                PdfLogger.Log(LogCategory.Text, $"[FONT RESOLVER] Warning: Requested '{systemFontName}' but got '{typeface.FamilyName}'");
            }
        }

        // Last resort: use SKTypeface default
        PdfLogger.Log(LogCategory.Text, $"[FONT RESOLVER] Warning: Could not resolve '{pdfFontName}', using system default");
        return SKTypeface.FromFamilyName(null, fontStyle) ?? SKTypeface.Default;
    }
}

/// <summary>
/// Categories of fonts for fallback resolution.
/// </summary>
public enum FontCategory
{
    SansSerif,
    Serif,
    Monospace,
    Symbol,
    Dingbats
}
