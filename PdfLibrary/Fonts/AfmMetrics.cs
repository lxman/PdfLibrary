using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;

namespace PdfLibrary.Fonts;

/// <summary>
/// Glyph-name → advance width, read from Adobe's Core-14 AFM files vendored under
/// <c>Resources/Afm</c> (APAFML; the licence ships embedded alongside). Parsed lazily, once per
/// face.
///
/// <para>Keyed on the AFM's <c>N</c> field, never its <c>C</c> field. AFM <c>C</c> codes are
/// StandardEncoding — <c>C 39 ; WX 222 ; N quoteright</c> in Helvetica, where WinAnsi code 39 is
/// <c>quotesingle</c> at 191. Conflating those two readings is what produced the eight wrong widths
/// L-1 had to correct, so this layer never looks at <c>C</c> at all. <c>C -1</c> rows are ordinary
/// data here: they carry the unencoded glyphs (<c>eacute</c>, <c>copyright</c>, …) that the previous
/// hand-written tables lacked entirely.</para>
/// </summary>
internal static class AfmMetrics
{
    /// <summary>Faces with a vendored AFM. Courier is absent deliberately — every one of its 315
    /// glyphs is 600, so <see cref="Standard14Metrics"/> answers it with a flat arm. The Oblique
    /// faces are absent because their widths are identical to the upright faces, which
    /// <see cref="Standard14Metrics.WidthByName"/> already routes to.</summary>
    private static readonly string[] Vendored =
    [
        "Helvetica", "Helvetica-Bold",
        "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
        "Symbol", "ZapfDingbats",
    ];

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, double>?> Cache =
        new(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, double>? ForFace(string? canonicalFaceName)
    {
        if (canonicalFaceName is null) return null;
        if (Array.IndexOf(Vendored, canonicalFaceName) < 0) return null;
        return Cache.GetOrAdd(canonicalFaceName, Load);
    }

    private static IReadOnlyDictionary<string, double>? Load(string face)
    {
        try
        {
            using Stream? raw = typeof(AfmMetrics).Assembly
                .GetManifestResourceStream($"PdfLibrary.Resources.Afm.{face}.afm.gz");
            if (raw is null) return null;

            using var gz = new GZipStream(raw, CompressionMode.Decompress);
            using var reader = new StreamReader(gz);

            var widths = new Dictionary<string, double>(StringComparer.Ordinal);
            var inCharMetrics = false;
            while (reader.ReadLine() is { } line)
            {
                if (!inCharMetrics)
                {
                    if (line.StartsWith("StartCharMetrics", StringComparison.Ordinal)) inCharMetrics = true;
                    continue;
                }
                if (line.StartsWith("EndCharMetrics", StringComparison.Ordinal)) break;

                if (ParseCharMetric(line) is not ({ } name, { } width)) continue;
                widths[name] = width;
            }

            return widths.Count == 0 ? null : widths;
        }
        catch
        {
            // A corrupt or missing resource yields no table; Standard14Metrics then returns null and
            // callers fall through to the descriptor, exactly as for a non-Standard-14 font.
            return null;
        }
    }

    /// <summary>Reads one <c>C … ; WX … ; N … ; B … ;</c> row. Semicolon-delimited key/value groups
    /// in any order, per the AFM spec — so the fields are located by key, not by position.</summary>
    private static (string? Name, double? Width) ParseCharMetric(string line)
    {
        string? name = null;
        double? width = null;

        foreach (string part in line.Split(';'))
        {
            ReadOnlySpan<char> p = part.AsSpan().Trim();
            if (p.StartsWith("WX ".AsSpan(), StringComparison.Ordinal))
            {
                if (double.TryParse(p[3..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double w))
                    width = w;
            }
            else if (p.StartsWith("N ".AsSpan(), StringComparison.Ordinal))
            {
                name = p[2..].Trim().ToString();
            }
        }

        return string.IsNullOrEmpty(name) || width is null ? (null, null) : (name, width);
    }
}
