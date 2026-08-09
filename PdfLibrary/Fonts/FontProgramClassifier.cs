using System.Text;
using FontParser;
using FontParser.Tables.Cff.Type1;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Fonts;

/// <summary>
/// Turns raw font-program bytes from <see cref="ISystemFontProvider.Resolve"/> — which carries no
/// format tag, only bytes and a face index — into a <see cref="ClassifiedProgram"/> the editor can
/// write to a font descriptor. Sniffs the leading magic bytes per the sfnt/CFF/Type1 conventions;
/// a <c>ttcf</c> collection is not embeddable as-is (a PDF font file must be a single standalone
/// sfnt), so its requested face is extracted to a fresh sfnt first, then reclassified.
/// </summary>
public static class FontProgramClassifier
{
    private static readonly byte[] TtcfMagic = [0x74, 0x74, 0x63, 0x66];        // 'ttcf'
    private static readonly byte[] TrueTypeMagic = [0x00, 0x01, 0x00, 0x00];
    private static readonly byte[] AppleTrueTypeMagic = [0x74, 0x72, 0x75, 0x65]; // 'true'
    private static readonly byte[] OpenTypeMagic = [0x4F, 0x54, 0x54, 0x4F];      // 'OTTO'

    private const string PfaHeaderAdobeFont = "%!PS-AdobeFont";
    private const string PfaHeaderFontType1 = "%!FontType1";

    /// <summary>
    /// Classifies <paramref name="bytes"/> into a format the editor can embed, extracting
    /// <paramref name="faceIndex"/> out of a TrueType Collection first if necessary. Returns null
    /// when the bytes are not a font program this library can embed (Task 6 turns that into a
    /// user-facing decline).
    /// </summary>
    public static ClassifiedProgram? Classify(byte[] bytes, int faceIndex)
    {
        if (bytes.Length < 2) return null;

        // A collection is not itself embeddable — extract the requested face into a standalone
        // sfnt and reclassify those bytes. Decided from the 'ttcf' magic, not from whether
        // faceIndex is non-zero: a collection can legitimately have FaceIndex 0.
        if (StartsWith(bytes, TtcfMagic))
        {
            byte[]? extracted = ExtractCollectionFace(bytes, faceIndex);
            return extracted is null ? null : Classify(extracted, 0);
        }

        if (StartsWith(bytes, TrueTypeMagic) || StartsWith(bytes, AppleTrueTypeMagic))
            return new ClassifiedProgram(bytes, FontProgramFormat.TrueType);

        if (StartsWith(bytes, OpenTypeMagic))
            return new ClassifiedProgram(bytes, FontProgramFormat.OpenType);

        // Bare CFF (major.minor version 01 00). CID-keyed CFF carries a ROS operator in its Top
        // DICT; a parse failure on the fragment is treated as "not CID" rather than "not a font",
        // since the magic bytes alone already committed us to the CFF family.
        if (bytes[0] == 0x01 && bytes[1] == 0x00)
        {
            bool isCid = TryIsCidKeyedCff(bytes);
            return new ClassifiedProgram(bytes, isCid ? FontProgramFormat.CidFontType0C : FontProgramFormat.Type1C);
        }

        // PFB, segmented Type 1 (leading 0x80 0x01 marker byte + ASCII segment type).
        if (bytes[0] == 0x80 && bytes[1] == 0x01)
            return new ClassifiedProgram(bytes, FontProgramFormat.Type1);

        // PFA, ASCII Type 1.
        if (LooksLikePfa(bytes))
            return new ClassifiedProgram(bytes, FontProgramFormat.Type1);

        return null;
    }

    /// <summary>Constructs <see cref="EmbeddedFontMetrics"/> over <paramref name="program"/> and
    /// reports whether it parsed as a loadable font — not merely that it started with the right
    /// magic. Exists so tests (and Task 4/5 callers) can assert this without
    /// <see cref="EmbeddedFontMetrics"/> becoming public.</summary>
    public static bool IsLoadable(byte[] program) => new EmbeddedFontMetrics(program).IsValid;

    private static bool StartsWith(byte[] bytes, byte[] magic) =>
        bytes.Length >= magic.Length && bytes.AsSpan(0, magic.Length).SequenceEqual(magic);

    private static bool LooksLikePfa(byte[] bytes)
    {
        int len = Math.Min(bytes.Length, 32);
        string head = Encoding.ASCII.GetString(bytes, 0, len);
        return head.StartsWith(PfaHeaderAdobeFont, StringComparison.Ordinal)
            || head.StartsWith(PfaHeaderFontType1, StringComparison.Ordinal);
    }

    private static bool TryIsCidKeyedCff(byte[] bytes)
    {
        try { return new Type1Table(bytes).IsCid; }
        catch { return false; }
    }

    /// <summary>
    /// Extracts face <paramref name="faceIndex"/> out of a <c>ttcf</c> collection into a fresh
    /// standalone sfnt: a new 12-byte offset table, one table-directory record per table (sorted by
    /// tag, offsets rewritten to the tables' new positions in this file), the table data copied and
    /// padded to a 4-byte boundary. Tables shared between faces are simply copied — the source data
    /// says nothing about sharing, so each extraction copies whatever bytes the face's own
    /// directory entries point at.
    /// <para>Deliberately does NOT recompute the copied <c>head</c> table's
    /// <c>checkSumAdjustment</c>. That field is a whole-file integrity checksum that no PDF
    /// consumer validates; it was computed for the ORIGINAL collection's byte layout and will not
    /// match this standalone file's, but leaving it alone is harmless, whereas computing a wrong
    /// one (or paying for a correct one — zero it, checksum the whole assembled file, patch it back
    /// in) buys nothing observable.</para>
    /// </summary>
    private static byte[]? ExtractCollectionFace(byte[] collection, int faceIndex)
    {
        SfntFont face;
        try { face = new SfntFont(collection, faceIndex); }
        catch { return null; }

        var tags = new List<string>(face.TableTags);
        if (tags.Count == 0) return null;
        tags.Sort(StringComparer.Ordinal);

        var tableData = new (string Tag, byte[] Data)[tags.Count];
        for (var i = 0; i < tags.Count; i++)
        {
            byte[]? data = face.GetTableBytes(tags[i]);
            if (data is null) return null; // directory entry pointed outside the buffer
            tableData[i] = (tags[i], data);
        }

        int numTables = tableData.Length;
        var headerSize = (uint)(12 + numTables * 16);
        var offsets = new uint[numTables];
        uint pos = headerSize;
        for (var i = 0; i < numTables; i++)
        {
            offsets[i] = pos;
            pos += (uint)tableData[i].Data.Length;
            pos = (pos + 3) & ~3u; // pad to a 4-byte boundary
        }

        var output = new byte[pos];
        var p = 0;

        // 0x00010000 = TrueType outlines, 'OTTO' = CFF outlines — the two shapes this library's
        // own SfntFont recognises. 'true' (legacy Apple TrueType) is never chosen here: a face this
        // library already parsed as TrueType outlines re-serialises under the modern tag.
        uint sfntVersion = face.OutlineKind == SfntOutlineKind.Cff ? 0x4F54544Fu : 0x00010000u;
        WriteU32(output, ref p, sfntVersion);

        var searchPow = 1;
        while (searchPow * 2 <= numTables) searchPow *= 2;
        int searchRange = searchPow * 16;
        var entrySelector = 0;
        while (1 << entrySelector < searchPow) entrySelector++;
        int rangeShift = numTables * 16 - searchRange;

        WriteU16(output, ref p, (ushort)numTables);
        WriteU16(output, ref p, (ushort)searchRange);
        WriteU16(output, ref p, (ushort)entrySelector);
        WriteU16(output, ref p, (ushort)rangeShift);

        for (var i = 0; i < numTables; i++)
        {
            byte[] tagBytes = Encoding.ASCII.GetBytes(tableData[i].Tag.PadRight(4));
            Array.Copy(tagBytes, 0, output, p, 4);
            p += 4;
            WriteU32(output, ref p, TableChecksum(tableData[i].Data));
            WriteU32(output, ref p, offsets[i]);
            WriteU32(output, ref p, (uint)tableData[i].Data.Length);
        }

        for (var i = 0; i < numTables; i++)
            Array.Copy(tableData[i].Data, 0, output, (int)offsets[i], tableData[i].Data.Length);

        return output;
    }

    /// <summary>OpenType per-table checksum: sum of the table's bytes as big-endian uint32s,
    /// zero-padding the final partial word (which is never written back to the buffer).</summary>
    private static uint TableChecksum(byte[] data)
    {
        uint sum = 0;
        var i = 0;
        while (i < data.Length)
        {
            uint word = 0;
            for (var k = 0; k < 4; k++)
            {
                word <<= 8;
                if (i + k < data.Length) word |= data[i + k];
            }
            sum += word;
            i += 4;
        }
        return sum;
    }

    private static void WriteU32(byte[] buf, ref int p, uint value)
    {
        buf[p] = (byte)(value >> 24);
        buf[p + 1] = (byte)(value >> 16);
        buf[p + 2] = (byte)(value >> 8);
        buf[p + 3] = (byte)value;
        p += 4;
    }

    private static void WriteU16(byte[] buf, ref int p, ushort value)
    {
        buf[p] = (byte)(value >> 8);
        buf[p + 1] = (byte)value;
        p += 2;
    }
}
