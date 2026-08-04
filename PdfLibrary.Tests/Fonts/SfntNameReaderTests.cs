using System.Text;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class SfntNameReaderTests
{
    /// <summary>Builds a minimal but structurally valid sfnt carrying ONLY a `name` and a `head`
    /// table. SfntNameReader reads nothing else, so this is sufficient and keeps the fixtures
    /// readable — a real font would bury the fields under a megabyte of glyph data.</summary>
    private static byte[] Sfnt(int macStyle, params (int platformId, int langId, int nameId, string value)[] names)
    {
        var storage = new List<byte>();
        var records = new List<byte>();
        foreach ((int pid, int lang, int nid, string v) in names)
        {
            byte[] bytes = pid == 3 ? Encoding.BigEndianUnicode.GetBytes(v) : Encoding.ASCII.GetBytes(v);
            AddU16(records, pid);
            AddU16(records, pid == 3 ? 1 : 0);   // encodingID
            AddU16(records, lang);
            AddU16(records, nid);
            AddU16(records, bytes.Length);
            AddU16(records, storage.Count);      // offset into storage
            storage.AddRange(bytes);
        }

        var name = new List<byte>();
        AddU16(name, 0);                          // format
        AddU16(name, names.Length);               // count
        AddU16(name, 6 + records.Count);          // stringOffset
        name.AddRange(records);
        name.AddRange(storage);

        var head = new byte[54];
        head[44] = (byte)(macStyle >> 8);
        head[45] = (byte)(macStyle & 0xFF);

        const int numTables = 2;
        int dirSize = 12 + numTables * 16;
        int headOff = dirSize;
        int nameOff = headOff + head.Length;

        var f = new List<byte>();
        f.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x00 });   // sfntVersion 1.0
        AddU16(f, numTables);
        AddU16(f, 0); AddU16(f, 0); AddU16(f, 0);            // searchRange/entrySelector/rangeShift
        f.AddRange(Encoding.ASCII.GetBytes("head")); AddU32(f, 0); AddU32(f, (uint)headOff); AddU32(f, (uint)head.Length);
        f.AddRange(Encoding.ASCII.GetBytes("name")); AddU32(f, 0); AddU32(f, (uint)nameOff); AddU32(f, (uint)name.Count);
        f.AddRange(head);
        f.AddRange(name);
        return f.ToArray();
    }

    private static void AddU16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
    private static void AddU32(List<byte> b, uint v)
    { b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v); }

    [Fact]
    public void Reads_postscript_name_family_and_style()
    {
        byte[] data = Sfnt(0x0002,
            (3, 0x409, 1, "Test Family"),
            (3, 0x409, 2, "Italic"),
            (3, 0x409, 6, "TestFamily-Italic"));

        FontFaceRecord? face = SfntNameReader.ReadFace(data, 0, "test.ttf");

        Assert.NotNull(face);
        Assert.Equal("TestFamily-Italic", face!.PostScriptName);
        Assert.Equal("Test Family", face.EnglishFamily);
        Assert.True(face.Italic);
        Assert.False(face.Bold);
    }

    [Fact]
    public void Indexes_every_localized_family_not_just_english()
    {
        byte[] data = Sfnt(0,
            (3, 0x409, 1, "Hiragino Mincho ProN"),
            (3, 0x411, 1, "ヒラギノ明朝 ProN"),
            (3, 0x409, 6, "HiraMinProN-W3"));

        FontFaceRecord? face = SfntNameReader.ReadFace(data, 0, "test.ttf");

        Assert.NotNull(face);
        Assert.Contains("ヒラギノ明朝 ProN", face!.Families);
        Assert.Contains("Hiragino Mincho ProN", face.Families);
        Assert.Equal("Hiragino Mincho ProN", face.EnglishFamily);
    }

    [Fact]
    public void English_family_wins_regardless_of_record_order()
    {
        // The Spanish record comes FIRST. Taking "the first ID 1" would canonicalise to it and make
        // the index locale-dependent across machines — observed on a real box as "Times New Roman
        // cursiva".
        byte[] data = Sfnt(0,
            (3, 0x0C0A, 1, "Times New Roman cursiva"),
            (3, 0x409, 1, "Times New Roman"),
            (3, 0x409, 6, "TimesNewRomanPSMT"));

        FontFaceRecord? face = SfntNameReader.ReadFace(data, 0, "test.ttf");

        Assert.Equal("Times New Roman", face!.EnglishFamily);
    }

    [Fact]
    public void FaceCount_is_one_for_a_bare_sfnt()
    {
        Assert.Equal(1, SfntNameReader.FaceCount(Sfnt(0, (3, 0x409, 6, "X"))));
    }

    [Fact]
    public void Malformed_data_returns_null_rather_than_throwing()
    {
        Assert.Null(SfntNameReader.ReadFace([0x00, 0x01], 0, "truncated.ttf"));
        Assert.Null(SfntNameReader.ReadFace([], 0, "empty.ttf"));
    }
}
