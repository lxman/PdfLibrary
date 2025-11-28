namespace PdfLibrary.Fonts;

/// <summary>
/// Provides an abstraction for discovering fonts available on the system.
/// This interface allows for different implementations (e.g., SkiaSharp-based,
/// platform-specific) without affecting consuming code.
/// </summary>
public interface ISystemFontProvider
{
    /// <summary>
    /// Gets all font family names available on the system.
    /// </summary>
    /// <returns>A collection of available font family names.</returns>
    IReadOnlyCollection<string> GetAvailableFontFamilies();

    /// <summary>
    /// Checks if a specific font family is available on the system.
    /// </summary>
    /// <param name="familyName">The font family name to check.</param>
    /// <returns>True if the font family is available; otherwise, false.</returns>
    bool IsFontAvailable(string familyName);

    /// <summary>
    /// Finds the first available font from a list of candidates.
    /// </summary>
    /// <param name="candidates">Ordered list of font family names to try.</param>
    /// <returns>The first available font family name, or null if none are available.</returns>
    string? FindFirstAvailable(IEnumerable<string> candidates);

    /// <summary>
    /// Refreshes the font cache, re-enumerating available system fonts.
    /// Useful if fonts may have been installed/uninstalled during runtime.
    /// </summary>
    void RefreshCache();
}
