using ICCSharp.IO;
using ICCSharp.Profile;

namespace ICCSharp.Tests.Profile;

public class TagTableTests
{
    /// <summary>
    /// Builds a tag-table byte sequence: uint32 count followed by 12-byte entries.
    /// Each entry is (signature, offset, size) all big-endian uint32.
    /// </summary>
    private static byte[] BuildTagTable(params (string Sig, uint Offset, uint Size)[] entries)
    {
        var buf = new byte[4 + 12 * entries.Length];
        WriteUInt32(buf, 0, (uint)entries.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            int p = 4 + i * 12;
            for (var j = 0; j < 4; j++) buf[p + j] = (byte)entries[i].Sig[j];
            WriteUInt32(buf, p + 4, entries[i].Offset);
            WriteUInt32(buf, p + 8, entries[i].Size);
        }
        return buf;
    }

    private static void WriteUInt32(byte[] buf, int offset, uint value)
    {
        buf[offset]     = (byte)((value >> 24) & 0xFF);
        buf[offset + 1] = (byte)((value >> 16) & 0xFF);
        buf[offset + 2] = (byte)((value >> 8) & 0xFF);
        buf[offset + 3] = (byte)(value & 0xFF);
    }

    [Fact]
    public void Parse_reads_count_and_entries_in_order()
    {
        byte[] bytes = BuildTagTable(
            ("desc", 256, 100),
            ("cprt", 356, 50),
            ("wtpt", 406, 20));

        TagTable t = TagTable.Parse(new IccBinaryReader(bytes), profileSize: 512);
        Assert.Equal(3, t.Count);
        Assert.Equal("desc", t.Entries[0].Signature.ToString());
        Assert.Equal(256u, t.Entries[0].Offset);
        Assert.Equal(100u, t.Entries[0].Size);
        Assert.Equal("cprt", t.Entries[1].Signature.ToString());
        Assert.Equal("wtpt", t.Entries[2].Signature.ToString());
    }

    [Fact]
    public void TryGet_returns_entry_by_signature()
    {
        byte[] bytes = BuildTagTable(("desc", 256, 100), ("cprt", 356, 50));
        TagTable t = TagTable.Parse(new IccBinaryReader(bytes), 512);

        Assert.True(t.TryGet(IccSignature.FromAscii("desc"), out TagDirectoryEntry desc));
        Assert.Equal(256u, desc.Offset);
        Assert.True(t.Contains(IccSignature.FromAscii("cprt")));
        Assert.False(t.Contains(IccSignature.FromAscii("zzzz")));
    }

    [Fact]
    public void Indexer_throws_when_signature_absent()
    {
        TagTable t = TagTable.Parse(new IccBinaryReader(BuildTagTable(("desc", 256, 100))), 512);
        Assert.Throws<KeyNotFoundException>(() => t[IccSignature.FromAscii("cprt")]);
    }

    [Fact]
    public void Duplicate_signatures_keep_first_in_index_but_preserve_both_in_entries()
    {
        byte[] bytes = BuildTagTable(
            ("desc", 256, 100),
            ("desc", 356, 50));
        TagTable t = TagTable.Parse(new IccBinaryReader(bytes), 512);
        Assert.Equal(2, t.Count);
        Assert.Equal(256u, t[IccSignature.FromAscii("desc")].Offset); // first wins
    }

    [Fact]
    public void Offset_inside_header_throws()
    {
        byte[] bytes = BuildTagTable(("desc", 64, 32));
        Assert.Throws<IccParseException>(() => TagTable.Parse(new IccBinaryReader(bytes), 512));
    }

    [Fact]
    public void Tag_extending_past_profile_end_throws()
    {
        byte[] bytes = BuildTagTable(("desc", 256, 1000));
        Assert.Throws<IccParseException>(() => TagTable.Parse(new IccBinaryReader(bytes), profileSize: 512));
    }

    [Fact]
    public void Tag_count_exceeding_remaining_buffer_throws()
    {
        // Declare 1000 entries but only supply enough bytes for the count itself
        var bytes = new byte[4];
        WriteUInt32(bytes, 0, 1000);
        Assert.Throws<IccParseException>(() => TagTable.Parse(new IccBinaryReader(bytes), profileSize: 1_000_000));
    }

    [Fact]
    public void Empty_tag_table_parses_to_zero_entries()
    {
        byte[] bytes = BuildTagTable();
        TagTable t = TagTable.Parse(new IccBinaryReader(bytes), profileSize: 512);
        Assert.Equal(0, t.Count);
    }

    [Fact]
    public void Aliased_tags_sharing_same_offset_are_both_recorded()
    {
        // §7.3 explicitly permits multiple distinct signatures pointing at the same data.
        byte[] bytes = BuildTagTable(
            ("rTRC", 256, 100),
            ("gTRC", 256, 100),
            ("bTRC", 256, 100));
        TagTable t = TagTable.Parse(new IccBinaryReader(bytes), 512);
        Assert.Equal(3, t.Count);
        Assert.Equal(256u, t[IccSignature.FromAscii("rTRC")].Offset);
        Assert.Equal(256u, t[IccSignature.FromAscii("gTRC")].Offset);
        Assert.Equal(256u, t[IccSignature.FromAscii("bTRC")].Offset);
    }
}
