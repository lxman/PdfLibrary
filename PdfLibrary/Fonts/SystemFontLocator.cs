namespace PdfLibrary.Fonts;

/// <summary>
/// The default, SkiaSharp-free <see cref="ISystemFontProvider"/>: locates metric-compatible
/// substitutes for standard-14 fonts among the fonts installed on the system and returns their
/// raw bytes. Reading installed fonts is not redistribution.
/// </summary>
public sealed partial class SystemFontLocator : ISystemFontProvider
{
    private readonly FontDirectoryIndex _index;
    private readonly List<string> _baseNames;

    /// <summary>Create a locator that scans the given directories (used for testing).</summary>
    public SystemFontLocator(IEnumerable<string> directories)
    {
        string[] dirs = directories as string[] ?? directories.ToArray();
        _index = new FontDirectoryIndex(dirs);
        _baseNames = EnumerateBaseNames(dirs);
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
    public IReadOnlyCollection<string> GetAvailableFontFamilies() =>
        _baseNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();

    /// <inheritdoc/>
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

    private static List<string> EnumerateBaseNames(IEnumerable<string> directories)
    {
        var names = new List<string>();
        foreach (string dir in directories)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext is ".ttf" or ".otf" or ".ttc")
                        names.Add(Path.GetFileNameWithoutExtension(f));
                }
            }
            catch { /* skip unreadable dir */ }
        }
        return names;
    }
}
