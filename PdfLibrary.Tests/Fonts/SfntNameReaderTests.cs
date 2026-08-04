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

    /// <summary>Wraps N independent bare-sfnt faces into a valid `ttcf` collection: header
    /// ('ttcf', major 1, minor 0, numFonts, N absolute offsets) followed by each face's bytes, with
    /// each face's OWN table-directory offsets rebased by its base offset in the file — table
    /// offsets inside a .ttc are file-absolute, not face-relative. Mirrors the rebasing pattern in
    /// SfntFontFaceSelectionTests.WrapAsTwoFaceTtc.</summary>
    private static byte[] Ttc(params byte[][] faces)
    {
        int headerSize = 12 + faces.Length * 4;
        var baseOffsets = new int[faces.Length];
        int running = headerSize;
        for (var i = 0; i < faces.Length; i++)
        {
            baseOffsets[i] = running;
            running += faces[i].Length;
        }

        var f = new List<byte>();
        f.AddRange(new byte[] { 0x74, 0x74, 0x63, 0x66 });   // 'ttcf'
        AddU16(f, 1); AddU16(f, 0);                          // major 1, minor 0
        AddU32(f, (uint)faces.Length);                       // numFonts
        foreach (int off in baseOffsets) AddU32(f, (uint)off);

        for (var i = 0; i < faces.Length; i++)
        {
            byte[] face = (byte[])faces[i].Clone();
            int numTables = (face[4] << 8) | face[5];
            int firstRecord = 12;
            for (var t = 0; t < numTables; t++)
            {
                int offPos = firstRecord + t * 16 + 8;
                uint off = ((uint)face[offPos] << 24) | ((uint)face[offPos + 1] << 16)
                         | ((uint)face[offPos + 2] << 8) | face[offPos + 3];
                off += (uint)baseOffsets[i];
                face[offPos] = (byte)(off >> 24); face[offPos + 1] = (byte)(off >> 16);
                face[offPos + 2] = (byte)(off >> 8); face[offPos + 3] = (byte)off;
            }
            f.AddRange(face);
        }
        return f.ToArray();
    }

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

    [Fact]
    public void Ttc_FaceCount_matches_the_number_of_wrapped_faces()
    {
        byte[] face0 = Sfnt(0, (3, 0x409, 6, "FaceA-Regular"));
        byte[] face1 = Sfnt(0x0002, (3, 0x409, 6, "FaceB-Italic"));
        byte[] data = Ttc(face0, face1);

        Assert.Equal(2, SfntNameReader.FaceCount(data));
    }

    [Fact]
    public void Ttc_Each_face_reads_back_its_own_identity_not_a_neighbours()
    {
        byte[] face0 = Sfnt(0,
            (3, 0x409, 1, "Face Regular"),
            (3, 0x409, 6, "FaceA-Regular"));
        byte[] face1 = Sfnt(0x0002,
            (3, 0x409, 1, "Face Italic"),
            (3, 0x409, 2, "Italic"),
            (3, 0x409, 6, "FaceB-Italic"));
        byte[] data = Ttc(face0, face1);

        FontFaceRecord? read0 = SfntNameReader.ReadFace(data, 0, "test.ttc");
        FontFaceRecord? read1 = SfntNameReader.ReadFace(data, 1, "test.ttc");

        Assert.NotNull(read0);
        Assert.Equal("FaceA-Regular", read0!.PostScriptName);
        Assert.Equal("Face Regular", read0.EnglishFamily);
        Assert.False(read0.Italic);

        Assert.NotNull(read1);
        Assert.Equal("FaceB-Italic", read1!.PostScriptName);
        Assert.Equal("Face Italic", read1.EnglishFamily);
        Assert.True(read1.Italic);
    }

    [Fact]
    public void Ttc_Face_index_beyond_FaceCount_returns_null_rather_than_throwing()
    {
        byte[] face0 = Sfnt(0, (3, 0x409, 6, "FaceA-Regular"));
        byte[] face1 = Sfnt(0, (3, 0x409, 6, "FaceB-Regular"));
        byte[] data = Ttc(face0, face1);

        Assert.Null(SfntNameReader.ReadFace(data, 2, "test.ttc"));
    }
}
