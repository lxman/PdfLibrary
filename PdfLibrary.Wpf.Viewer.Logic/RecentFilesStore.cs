namespace PdfLibrary.Wpf.Viewer.Logic;

/// <summary>Most-recently-used file list: newest first, de-duplicated (OrdinalIgnoreCase, matching
/// Windows path semantics), trimmed to a max. Serialized as newline-delimited paths.</summary>
public sealed class RecentFilesStore
{
    private readonly List<string> _items = new();
    private readonly int _max;

    public RecentFilesStore(int max = 10) => _max = Math.Max(1, max);

    public IReadOnlyList<string> Items => _items;

    public void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, path);
        if (_items.Count > _max) _items.RemoveRange(_max, _items.Count - _max);
    }

    public string Serialize() => string.Join('\n', _items);

    public static RecentFilesStore Deserialize(string? text, int max = 10)
    {
        var s = new RecentFilesStore(max);
        if (string.IsNullOrWhiteSpace(text)) return s;
        foreach (string line in text.Split('\n').Reverse())   // reverse so first line ends up front
            if (!string.IsNullOrWhiteSpace(line)) s.Add(line.Trim());
        return s;
    }
}
