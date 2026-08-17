using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Fonts.Remediation;

/// <summary>Every used CID resolved through /ToUnicode into the substitute's cmap, or the list
/// of CIDs that could not be — the all-or-nothing coverage answer (spec §3 step 2).</summary>
internal sealed record CidReplacementMapResult(
    IReadOnlyDictionary<int, ushort> CidToGid,
    IReadOnlyList<int> Unresolvable,
    int MaxCid);

/// <summary>
/// Resolves every CID a Type0 font actually uses to a glyph in a substitute embedded program via
/// /ToUnicode, and serialises the result as a CIDToGIDMap stream (spec §3 steps 2 and 4).
///
/// <para>A CID resolves iff its /ToUnicode value is exactly one code point (a single BMP char, or
/// a surrogate pair) AND the substitute's cmap has a glyph for that code point. A multi-character
/// value (ligature decomposition such as "ffi") or a missing /ToUnicode entry has no single-glyph
/// answer, so the CID is reported unresolvable rather than partially replaced — coverage is
/// all-or-nothing by design; the caller decides what to do with a non-empty
/// <see cref="CidReplacementMapResult.Unresolvable"/>.</para>
/// </summary>
internal static class CidReplacementMap
{
    /// <summary>Resolves every CID in <paramref name="usedCids"/> against <paramref name="toUnicode"/>
    /// and <paramref name="substitute"/>. Distinct and ordered internally; callers may pass CIDs in
    /// any order and with duplicates.</summary>
    public static CidReplacementMapResult Build(
        ToUnicodeCMap toUnicode, IEnumerable<int> usedCids, EmbeddedFontMetrics substitute)
    {
        var map = new Dictionary<int, ushort>();
        var unresolvable = new List<int>();
        var maxCid = 0;

        foreach (int cid in usedCids.Distinct().OrderBy(c => c))
        {
            maxCid = Math.Max(maxCid, cid);
            string? text = toUnicode.Lookup(cid);

            // Only a single-code-point value can honestly select ONE glyph. A multi-character
            // expansion (ligature decomposition) or a missing entry has no single-GID answer,
            // and a partial replacement is forbidden (spec §3 step 2).
            bool singleCodePoint = text is { Length: 1 }
                || (text is { Length: 2 } && char.IsSurrogatePair(text[0], text[1]));
            ushort gid = singleCodePoint
                ? substitute.GetGlyphIdByUnicode(char.ConvertToUtf32(text!, 0))
                : (ushort)0;

            if (gid == 0) unresolvable.Add(cid);
            else map[cid] = gid;
        }

        return new CidReplacementMapResult(map, unresolvable, maxCid);
    }

    /// <summary>Big-endian 2-byte-per-CID stream, GID 0 for every CID not in the map
    /// (unused CIDs → .notdef, spec §3 step 4). Length = (maxCid + 1) * 2.</summary>
    public static byte[] ToStreamBytes(IReadOnlyDictionary<int, ushort> cidToGid, int maxCid)
    {
        var bytes = new byte[(maxCid + 1) * 2];
        foreach ((int cid, ushort gid) in cidToGid)
        {
            bytes[cid * 2] = (byte)(gid >> 8);
            bytes[cid * 2 + 1] = (byte)(gid & 0xFF);
        }

        return bytes;
    }
}
