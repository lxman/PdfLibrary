using Logging;
using PdfLibrary.Fonts;
using SkiaSharp;

namespace PdfLibrary.Rendering.SkiaSharp;

/// <summary>
/// Implementation of <see cref="ISystemFontProvider"/> using SkiaSharp's SKFontManager.
/// This provides cross-platform font discovery through Skia's abstraction layer.
/// </summary>
public class SkiaFontProvider : ISystemFontProvider
{
    private HashSet<string> _availableFonts;
    private readonly object _lock = new();

    public SkiaFontProvider()
    {
        _availableFonts = LoadAvailableFonts();
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> GetAvailableFontFamilies()
    {
        lock (_lock)
        {
            return _availableFonts.ToList().AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public bool IsFontAvailable(string familyName)
    {
        if (string.IsNullOrEmpty(familyName))
            return false;

        lock (_lock)
        {
            // Check exact match first
            if (_availableFonts.Contains(familyName))
                return true;

            // Check case-insensitive match
            return _availableFonts.Any(f => f.Equals(familyName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <inheritdoc/>
    public string? FindFirstAvailable(IEnumerable<string> candidates)
    {
        foreach (string candidate in candidates)
        {
            if (IsFontAvailable(candidate))
                return GetExactFontName(candidate);
        }
        return null;
    }

    /// <inheritdoc/>
    public void RefreshCache()
    {
        lock (_lock)
        {
            _availableFonts = LoadAvailableFonts();
        }
    }

    /// <summary>
    /// Gets the exact font name as known by the system (preserving correct casing).
    /// </summary>
    private string? GetExactFontName(string familyName)
    {
        lock (_lock)
        {
            // Return exact match if exists
            if (_availableFonts.Contains(familyName))
                return familyName;

            // Return case-insensitive match
            return _availableFonts.FirstOrDefault(f => f.Equals(familyName, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Loads all available font families from the system using SKFontManager.
    /// </summary>
    private static HashSet<string> LoadAvailableFonts()
    {
        var fonts = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var fontManager = SKFontManager.Default;
            int count = fontManager.FontFamilyCount;

            for (var i = 0; i < count; i++)
            {
                string? familyName = fontManager.GetFamilyName(i);
                if (!string.IsNullOrEmpty(familyName))
                {
                    fonts.Add(familyName);
                }
            }

            PdfLogger.Log(LogCategory.Text, $"[FONT PROVIDER] Loaded {fonts.Count} font families from system");
        }
        catch (Exception ex)
        {
            PdfLogger.Log(LogCategory.Text, $"[FONT PROVIDER] Error loading system fonts: {ex.Message}");
        }

        return fonts;
    }
}
