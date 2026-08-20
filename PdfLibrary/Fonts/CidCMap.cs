using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PdfLibrary.Fonts;

/// <summary>
/// Parses the CID-keyed operators of a CMap (ISO 32000-1:2008 §9.7.5.3): cidchar and cidrange
/// (code→CID; the CID operand is DECIMAL, unlike the bf* dialect's hex destinations — see
/// <see cref="ToUnicodeCMap"/> for that dialect). Used for an embedded Type0 /Encoding CMap
/// stream (B-1 CID→Unicode extraction). codespacerange is not needed for the fixed 2-byte
/// extraction loop and is not modeled. <c>usecmap</c> is recorded by name but NOT followed —
/// no predefined encoding bases are bundled (the measured corpus population never layers);
/// local operators still parse. Malformed input degrades to an empty map (the caller's decode
/// chain falls through) and Parse never throws.
/// </summary>
public partial class CidCMap
{
    // Widest legitimate range in a 2-byte codespace; anything wider is treated as corrupt
    // rather than materialized (a bogus 3-byte hi endpoint would otherwise allocate millions).
    private const int MaxRangeSpan = 0xFFFF;

    private readonly Dictionary<int, int> _codeToCid = new();

    /// <summary>The /Name operand of a <c>usecmap</c> directive, when present. Recorded for
    /// diagnostics; v1 does not resolve it (see class doc).</summary>
    public string? UseCMapName { get; private set; }

    public int MappingCount => _codeToCid.Count;

    public int? MapCodeToCid(int code) =>
        _codeToCid.TryGetValue(code, out int cid) ? cid : null;

    public static CidCMap Parse(byte[] data)
    {
        var cmap = new CidCMap();
        try
        {
            string content = Encoding.ASCII.GetString(data);
            ParseCidChar(cmap, content);
            ParseCidRange(cmap, content);
            Match use = UseCMapRegex().Match(content);
            if (use.Success) cmap.UseCMapName = use.Groups[1].Value;
        }
        catch
        {
            // Degrade to whatever parsed before the fault — same posture as ToUnicodeCMap.
        }
        return cmap;
    }

    /// <summary>
    /// The largest CID this data DECLARES, or null when it declares none — the quantity ISO 19005-2
    /// clause 6.1.13 test 10 bounds at 65535 (veraPDF object <c>CMapFile</c>, <c>maximalCID</c>).
    ///
    /// <para>A separate scan from <see cref="Parse"/> on purpose. Parse materialises every code in
    /// every range into <c>_codeToCid</c> — tens of thousands of entries for a CJK CMap — and a
    /// caller that needs only a maximum should not pay that. This reads the same operators, keeps
    /// no map, and leaves Parse and the decode path it feeds untouched.</para>
    ///
    /// <para>Returns <see cref="long"/> because a range's top CID (<c>cidStart + (hi - lo)</c>) can
    /// exceed <see cref="int"/>. Ranges wider than <see cref="MaxRangeSpan"/> are skipped, matching
    /// Parse's notion of a legitimate 2-byte range — a deliberate under-report, since a wider
    /// codespace is legal in ISO 32000 but this engine's CID handling assumes two bytes throughout.
    /// Never throws: malformed input degrades to whatever was read first, like Parse.</para>
    /// </summary>
    internal static long? MaxDeclaredCid(byte[] data)
    {
        long? max = null;

        try
        {
            string content = Encoding.ASCII.GetString(data);

            foreach (string block in FindBlocks(content, "begincidchar", "endcidchar"))
            foreach (Match match in CidCharRegex().Matches(block))
            {
                if (!long.TryParse(match.Groups[2].Value, out long cid)) continue;
                max = max is null ? cid : Math.Max(max.Value, cid);
            }

            foreach (string block in FindBlocks(content, "begincidrange", "endcidrange"))
            foreach (Match match in CidRangeRegex().Matches(block))
            {
                if (!int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, null, out int lo) ||
                    !int.TryParse(match.Groups[2].Value, NumberStyles.HexNumber, null, out int hi) ||
                    !long.TryParse(match.Groups[3].Value, out long cidStart))
                    continue;
                if (hi < lo || hi - lo > MaxRangeSpan) continue;

                long top = cidStart + (hi - lo);
                max = max is null ? top : Math.Max(max.Value, top);
            }
        }
        catch
        {
            // Same posture as Parse: degrade to whatever was read before the fault, never throw.
        }

        return max;
    }

    // cidchar entry: <code> cid   (cid decimal)
    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s+(\d+)")]
    private static partial Regex CidCharRegex();

    // cidrange entry: <lo> <hi> cid   (cid decimal)
    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s+(\d+)")]
    private static partial Regex CidRangeRegex();

    [GeneratedRegex(@"/(\S+)\s+usecmap")]
    private static partial Regex UseCMapRegex();

    private static void ParseCidChar(CidCMap cmap, string content)
    {
        foreach (string block in FindBlocks(content, "begincidchar", "endcidchar"))
        foreach (Match match in CidCharRegex().Matches(block))
        {
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, null, out int code)) continue;
            if (!int.TryParse(match.Groups[2].Value, out int cid)) continue;
            cmap._codeToCid[code] = cid;
        }
    }

    private static void ParseCidRange(CidCMap cmap, string content)
    {
        foreach (string block in FindBlocks(content, "begincidrange", "endcidrange"))
        foreach (Match match in CidRangeRegex().Matches(block))
        {
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, null, out int lo) ||
                !int.TryParse(match.Groups[2].Value, NumberStyles.HexNumber, null, out int hi) ||
                !int.TryParse(match.Groups[3].Value, out int cidStart))
                continue;
            if (hi < lo || hi - lo > MaxRangeSpan) continue;
            for (int code = lo; code <= hi; code++)
                cmap._codeToCid[code] = cidStart + (code - lo);
        }
    }

    // Same block scan as ToUnicodeCMap.FindBlocks (private there; the dialects stay independent).
    private static List<string> FindBlocks(string content, string beginMarker, string endMarker)
    {
        var blocks = new List<string>();
        var pos = 0;
        while (true)
        {
            int beginPos = content.IndexOf(beginMarker, pos, StringComparison.Ordinal);
            if (beginPos == -1) break;
            int endPos = content.IndexOf(endMarker, beginPos, StringComparison.Ordinal);
            if (endPos == -1) break;
            int blockStart = beginPos + beginMarker.Length;
            blocks.Add(content.Substring(blockStart, endPos - blockStart));
            pos = endPos + endMarker.Length;
        }
        return blocks;
    }
}
