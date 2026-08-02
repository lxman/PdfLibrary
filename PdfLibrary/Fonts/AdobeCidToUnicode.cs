using System.IO.Compression;

namespace PdfLibrary.Fonts;

/// <summary>
/// CID→Unicode lookup for the registered Adobe CID collections (B-1 text extraction), backed by
/// Adobe's published <c>Adobe-&lt;Ordering&gt;-UCS2</c> mapping CMaps (github.com/adobe-type-tools/
/// mapping-resources-pdf, <c>pdf2unicode/</c>, BSD-3-Clause —
/// Resources/CMaps/LICENSE-Adobe-CMaps.txt ships alongside), bundled gzip-compressed and loaded
/// lazily once per ordering. The files are <c>CMapType 2</c> ToUnicode CMaps in the
/// <c>bfchar</c>/<c>bfrange</c> dialect mapping CID→UTF-16BE Unicode DIRECTLY, so they parse with
/// the existing <see cref="ToUnicodeCMap"/> and its Lookup keys ARE CIDs — no inversion.
/// Any load failure yields a null table and Lookup returns null — extraction falls through.
/// </summary>
public static class AdobeCidToUnicode
{
    private static readonly Dictionary<string, Lazy<ToUnicodeCMap?>> Tables =
        new(StringComparer.Ordinal)
        {
            ["Japan1"] = new(() => Load("Adobe-Japan1-UCS2")),
            ["Korea1"] = new(() => Load("Adobe-Korea1-UCS2")),
            ["GB1"] = new(() => Load("Adobe-GB1-UCS2")),
            ["CNS1"] = new(() => Load("Adobe-CNS1-UCS2")),
        };

    public static bool IsSupportedOrdering(string? ordering) =>
        ordering is not null && Tables.ContainsKey(ordering);

    public static string? Lookup(string? ordering, int cid)
    {
        if (ordering is null || !Tables.TryGetValue(ordering, out Lazy<ToUnicodeCMap?>? lazy))
            return null;
        return lazy.Value?.Lookup(cid);
    }

    private static ToUnicodeCMap? Load(string name)
    {
        try
        {
            using Stream? s = typeof(AdobeCidToUnicode).Assembly
                .GetManifestResourceStream($"PdfLibrary.Resources.CMaps.{name}.gz");
            if (s is null) return null;
            using var gz = new GZipStream(s, CompressionMode.Decompress);
            using var ms = new MemoryStream();
            gz.CopyTo(ms);
            return ToUnicodeCMap.Parse(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }
}
