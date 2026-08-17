using System.Buffers.Binary;

namespace PdfLibrary.Fonts.Remediation;

/// <summary>
/// Patches glyph advance widths inside a raw sfnt program (TrueType, 'true', or OpenType-CFF
/// 'OTTO' — advances live in hmtx for all three). Parses ONLY the table directory and treats
/// every table as opaque bytes; touches exactly hmtx (advances, expanding past the shared tail
/// when needed), hhea (numberOfHMetrics on expansion), and head (checkSumAdjustment), then
/// rebuilds the directory with recomputed offsets and checksums. Untouched tables are
/// byte-identical by construction (spec 2026-08-16-font-program-remediation-f4 §2).
/// Returns null with a user-presentable reason instead of throwing: the planner turns it into a
/// DeclineProposal.
/// </summary>
internal static class SfntAdvancePatcher
{
    public static byte[]? Patch(
        byte[] program, IReadOnlyDictionary<ushort, ushort> advanceByGid, out string? failReason)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(advanceByGid);
        failReason = null;
        if (advanceByGid.Count == 0) { failReason = "no advances to patch."; return null; }

        if (program.Length < 12) { failReason = "the font program is too short to carry a table directory."; return null; }
        uint version = BinaryPrimitives.ReadUInt32BigEndian(program);
        if (version is not (0x00010000 or 0x74727565 or 0x4F54544F))
        { failReason = "the font program is not an sfnt (TrueType/OpenType) container."; return null; }

        int numTables = BinaryPrimitives.ReadUInt16BigEndian(program.AsSpan(4));
        if (program.Length < 12 + numTables * 16)
        { failReason = "the font program's table directory is truncated."; return null; }

        var tables = new List<(string Tag, byte[] Data)>(numTables);
        for (var i = 0; i < numTables; i++)
        {
            int entry = 12 + i * 16;
            string tag = System.Text.Encoding.ASCII.GetString(program, entry, 4);
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(program.AsSpan(entry + 8));
            uint length = BinaryPrimitives.ReadUInt32BigEndian(program.AsSpan(entry + 12));
            // Subtraction form deliberately avoids `offset + length`, which can wrap past
            // uint.MaxValue for a corrupt directory entry (e.g. offset=0xFFFFFFF0, length=0x20
            // sums to 0x10 and would pass an addition-based check) and then throw from the
            // AsSpan cast below instead of failing cleanly.
            if (offset > (uint)program.Length || length > (uint)program.Length - offset)
            { failReason = $"the '{tag}' table extends past the end of the program."; return null; }
            tables.Add((tag, program.AsSpan((int)offset, (int)length).ToArray()));
        }

        int headIdx = tables.FindIndex(t => t.Tag == "head");
        int hheaIdx = tables.FindIndex(t => t.Tag == "hhea");
        int hmtxIdx = tables.FindIndex(t => t.Tag == "hmtx");
        int maxpIdx = tables.FindIndex(t => t.Tag == "maxp");
        if (headIdx < 0 || hheaIdx < 0 || hmtxIdx < 0 || maxpIdx < 0)
        { failReason = "the font program has no hmtx/hhea/head/maxp metrics tables to patch."; return null; }

        byte[] hhea = tables[hheaIdx].Data;
        byte[] maxp = tables[maxpIdx].Data;
        if (hhea.Length < 36 || maxp.Length < 6)
        { failReason = "the font program's hhea/maxp tables are truncated."; return null; }
        int numberOfHMetrics = BinaryPrimitives.ReadUInt16BigEndian(hhea.AsSpan(34));
        int numGlyphs = BinaryPrimitives.ReadUInt16BigEndian(maxp.AsSpan(4));
        if (numberOfHMetrics == 0 || numberOfHMetrics > numGlyphs)
        { failReason = "the font program's own tables disagree about its glyph metrics."; return null; }

        int maxGid = advanceByGid.Keys.Max();
        if (maxGid >= numGlyphs)
        { failReason = "a patched glyph id lies beyond the program's glyph count."; return null; }

        byte[] hmtx = tables[hmtxIdx].Data;
        int requiredLength = numberOfHMetrics * 4 + (numGlyphs - numberOfHMetrics) * 2;
        if (hmtx.Length < requiredLength)
        { failReason = "the font program's hmtx table is shorter than its declared metrics."; return null; }

        byte[] newHmtx;
        if (maxGid < numberOfHMetrics)
        {
            newHmtx = (byte[])hmtx.Clone();
            foreach ((ushort gid, ushort advance) in advanceByGid)
                BinaryPrimitives.WriteUInt16BigEndian(newHmtx.AsSpan(gid * 4), advance);
        }
        else
        {
            // Expansion: promote gids up to maxGid into long metrics. Tail entries inherit the
            // last long metric's advance (that is exactly what the shared tail means) and their
            // own lsb from the trailing i16 array.
            int newCount = maxGid + 1;
            ushort lastAdvance = BinaryPrimitives.ReadUInt16BigEndian(
                hmtx.AsSpan((numberOfHMetrics - 1) * 4));
            var expanded = new byte[newCount * 4 + (numGlyphs - newCount) * 2];
            for (var gid = 0; gid < newCount; gid++)
            {
                ushort advance = gid < numberOfHMetrics
                    ? BinaryPrimitives.ReadUInt16BigEndian(hmtx.AsSpan(gid * 4))
                    : lastAdvance;
                short lsb = gid < numberOfHMetrics
                    ? BinaryPrimitives.ReadInt16BigEndian(hmtx.AsSpan(gid * 4 + 2))
                    : BinaryPrimitives.ReadInt16BigEndian(
                        hmtx.AsSpan(numberOfHMetrics * 4 + (gid - numberOfHMetrics) * 2));
                BinaryPrimitives.WriteUInt16BigEndian(expanded.AsSpan(gid * 4), advance);
                BinaryPrimitives.WriteInt16BigEndian(expanded.AsSpan(gid * 4 + 2), lsb);
            }
            for (int gid = newCount; gid < numGlyphs; gid++)
            {
                short lsb = BinaryPrimitives.ReadInt16BigEndian(
                    hmtx.AsSpan(numberOfHMetrics * 4 + (gid - numberOfHMetrics) * 2));
                BinaryPrimitives.WriteInt16BigEndian(
                    expanded.AsSpan(newCount * 4 + (gid - newCount) * 2), lsb);
            }
            foreach ((ushort gid, ushort advance) in advanceByGid)
                BinaryPrimitives.WriteUInt16BigEndian(expanded.AsSpan(gid * 4), advance);
            newHmtx = expanded;

            byte[] newHhea = (byte[])hhea.Clone();
            BinaryPrimitives.WriteUInt16BigEndian(newHhea.AsSpan(34), (ushort)newCount);
            tables[hheaIdx] = ("hhea", newHhea);
        }
        tables[hmtxIdx] = ("hmtx", newHmtx);

        // head.checkSumAdjustment: zero before any checksum is computed.
        byte[] newHead = (byte[])tables[headIdx].Data.Clone();
        if (newHead.Length < 12)
        { failReason = "the font program's head table is truncated."; return null; }
        BinaryPrimitives.WriteUInt32BigEndian(newHead.AsSpan(8), 0);
        tables[headIdx] = ("head", newHead);

        byte[] rebuilt = Serialize(version, tables, out int headOffset);
        uint total = ChecksumOf(rebuilt, 0, rebuilt.Length);
        BinaryPrimitives.WriteUInt32BigEndian(rebuilt.AsSpan(headOffset + 8),
            unchecked(0xB1B0AFBAu - total));
        return rebuilt;
    }

    private static byte[] Serialize(
        uint version, List<(string Tag, byte[] Data)> tables, out int headOffset)
    {
        headOffset = -1;
        int directorySize = 12 + tables.Count * 16;
        int total = directorySize;
        foreach ((_, byte[] data) in tables) total += (data.Length + 3) & ~3;

        var file = new byte[total];
        BinaryPrimitives.WriteUInt32BigEndian(file, version);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(4), (ushort)tables.Count);
        // searchRange/entrySelector/rangeShift: computed per spec, read by nothing in this engine.
        int entrySelector = tables.Count == 0 ? 0 : (int)Math.Floor(Math.Log2(tables.Count));
        int searchRange = (1 << entrySelector) * 16;
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(10), (ushort)(tables.Count * 16 - searchRange));

        int offset = directorySize;
        // Directory entries MUST be sorted by tag (the format's binary-search contract); table
        // DATA keeps the caller's order, which Patch preserved from the original file.
        (string Tag, byte[] Data, int Offset)[] placed = new (string, byte[], int)[tables.Count];
        for (var i = 0; i < tables.Count; i++)
        {
            placed[i] = (tables[i].Tag, tables[i].Data, offset);
            if (tables[i].Tag == "head") headOffset = offset;
            Array.Copy(tables[i].Data, 0, file, offset, tables[i].Data.Length);
            offset += (tables[i].Data.Length + 3) & ~3;
        }
        (string Tag, byte[] Data, int Offset)[] sorted = placed.OrderBy(t => t.Tag, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < sorted.Length; i++)
        {
            int entry = 12 + i * 16;
            System.Text.Encoding.ASCII.GetBytes(sorted[i].Tag, file.AsSpan(entry, 4));
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(entry + 4),
                ChecksumOf(file, sorted[i].Offset, sorted[i].Data.Length));
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(entry + 8), (uint)sorted[i].Offset);
            BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(entry + 12), (uint)sorted[i].Data.Length);
        }
        return file;
    }

    /// <summary>Standard sfnt checksum: sum of big-endian u32s over the range, zero-padded to 4.</summary>
    private static uint ChecksumOf(byte[] data, int offset, int length)
    {
        uint sum = 0;
        int end = offset + length;
        for (int i = offset; i < end; i += 4)
        {
            uint word = 0;
            for (var b = 0; b < 4; b++)
                word = (word << 8) | (i + b < end ? data[i + b] : 0u);
            sum = unchecked(sum + word);
        }
        return sum;
    }
}
