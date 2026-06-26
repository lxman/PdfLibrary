namespace PdfLibrary.Fonts;

/// <summary>
/// Indexes font files found in a set of directories, keyed by case-insensitive file base-name
/// (no extension). Scanning is best-effort: missing or unreadable directories are skipped.
/// </summary>
internal sealed class FontDirectoryIndex
{
    private static readonly string[] Extensions = [".ttf", ".otf", ".ttc"];
    private readonly Dictionary<string, string> _byBaseName = new(StringComparer.OrdinalIgnoreCase);

    public FontDirectoryIndex(IEnumerable<string> directories)
    {
        foreach (string dir in directories)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                continue;

            try
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file);
                    if (Array.IndexOf(Extensions, ext.ToLowerInvariant()) < 0)
                        continue;

                    string baseName = Path.GetFileNameWithoutExtension(file);
                    // First writer wins, so earlier directories take precedence.
                    _byBaseName.TryAdd(baseName, file);
                }
            }
            catch
            {
                // Permission or IO error during recursive traversal — skip the rest of this directory.
                continue;
            }
        }
    }

    /// <summary>The distinct, case-insensitive set of indexed font file base-names.</summary>
    public IReadOnlyCollection<string> BaseNames => _byBaseName.Keys;

    /// <summary>Full path of the indexed font whose file base-name matches, or null.</summary>
    public string? FindPath(string fileBaseName) =>
        _byBaseName.TryGetValue(fileBaseName, out string? path) ? path : null;
}
