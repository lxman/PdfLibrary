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
