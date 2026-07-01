namespace PdfLibrary.Fonts;

/// <summary>
/// The default, SkiaSharp-free <see cref="ISystemFontProvider"/>: locates metric-compatible
/// substitutes for standard-14 fonts among the fonts installed on the system and returns their
/// raw bytes. Reading installed fonts is not redistribution.
/// </summary>
public sealed partial class SystemFontLocator : ISystemFontProvider
{
    private readonly FontDirectoryIndex _index;

    // Building the index recursively scans every OS font directory, so the default locator is a
    // process-wide shared singleton: the scan happens once per process, not once per PdfRenderer.
    // (Type3 fonts construct a sub-renderer per glyph, so a fresh scan per construction was ~86% of
    // page-record time.) The index is read-only after construction, so sharing it is thread-safe.
    private static readonly Lazy<SystemFontLocator> LazyDefault =
        new(static () => new SystemFontLocator(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The shared, lazily-built default locator over the system font directories. Reused
    /// everywhere a caller does not inject its own <see cref="ISystemFontProvider"/>.</summary>
    public static SystemFontLocator Default => LazyDefault.Value;

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
