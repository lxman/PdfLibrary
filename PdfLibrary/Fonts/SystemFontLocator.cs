namespace PdfLibrary.Fonts;

/// <summary>
/// The default, SkiaSharp-free <see cref="ISystemFontProvider"/>: locates metric-compatible
/// substitutes for standard-14 fonts among the fonts installed on the system and returns their
/// raw bytes. Reading installed fonts is not redistribution.
/// </summary>
public sealed partial class SystemFontLocator : ISystemFontProvider
{
    private readonly FontDirectoryIndex _index;

    /// <summary>Create a locator that scans the given directories (used for testing).</summary>
    public SystemFontLocator(IEnumerable<string> directories)
    {
        string[] dirs = directories as string[] ?? directories.ToArray();
        _index = new FontDirectoryIndex(dirs);
    }

    /// <inheritdoc/>
    public byte[]? GetFontData(string baseFontName)
    {
        foreach (string candidate in Standard14Fonts.SubstituteFileBaseNames(baseFontName))
        {
            string? path = _index.FindPath(candidate);
            if (path is null) continue;
            try { return File.ReadAllBytes(path); }
            catch { /* path exists but is unreadable — try the next candidate */ }
        }
        return null;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns installed font file base-names (e.g. "LiberationSans-Regular"),
    /// not family display-names.
    /// </remarks>
    public IReadOnlyCollection<string> GetAvailableFontFamilies() => _index.BaseNames;

    /// <inheritdoc/>
    /// <remarks>
    /// <paramref name="familyName"/> must be a font file base-name
    /// (e.g. "LiberationSans-Regular"), not a family display-name.
    /// </remarks>
    public bool IsFontAvailable(string familyName) => _index.FindPath(familyName) is not null;

    /// <inheritdoc/>
    public string? FindFirstAvailable(IEnumerable<string> candidates)
    {
        foreach (string c in candidates)
            if (IsFontAvailable(c)) return c;
        return null;
    }

    /// <inheritdoc/>
    public void RefreshCache() { /* Index is built at construction; create a new locator to refresh. */ }
}
