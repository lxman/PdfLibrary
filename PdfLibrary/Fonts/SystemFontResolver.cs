using Logging;
using SkiaSharp;

namespace PdfLibrary.Fonts;

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
    private static readonly string[] SansSerifFallbacks =
    {
        "Arial",
        "Helvetica",
        "Liberation Sans",
        "DejaVu Sans",
        "Nimbus Sans",
        "FreeSans",
        "Noto Sans",
        "Segoe UI"
    };

    private static readonly string[] SerifFallbacks =
    {
        "Times New Roman",
        "Times",
        "Liberation Serif",
        "DejaVu Serif",
        "Nimbus Roman",
        "FreeSerif",
        "Noto Serif",
        "Georgia"
    };

    private static readonly string[] MonospaceFallbacks =
    {
        "Courier New",
        "Courier",
        "Consolas",
        "Liberation Mono",
        "DejaVu Sans Mono",
        "Nimbus Mono",
        "FreeMono",
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

        PdfLogger.Log(LogCategory.Text, $"[FONT RESOLVER] Initialized fallbacks:");
        PdfLogger.Log(LogCategory.Text, $"  Sans-serif: {_resolvedSansSerif ?? "NONE"}");
        PdfLogger.Log(LogCategory.Text, $"  Serif: {_resolvedSerif ?? "NONE"}");
        PdfLogger.Log(LogCategory.Text, $"  Monospace: {_resolvedMonospace ?? "NONE"}");
        PdfLogger.Log(LogCategory.Text, $"  Symbol: {_resolvedSymbol ?? "NONE"}");
        PdfLogger.Log(LogCategory.Text, $"  Dingbats: {_resolvedDingbats ?? "NONE"}");
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
