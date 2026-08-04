using System.Text;

namespace PdfLibrary.Tests.Fonts;

internal static class SfntFixtures
{
    /// <summary>Builds a minimal but structurally valid sfnt carrying ONLY a `name` and a `head`
    /// table. SfntNameReader reads nothing else, so this is sufficient and keeps the fixtures
    /// readable — a real font would bury the fields under a megabyte of glyph data.</summary>
    public static byte[] Sfnt(int macStyle, params (int platformId, int langId, int nameId, string value)[] names)
    {
        var storage = new List<byte>();
        var records = new List<byte>();
        foreach ((int pid, int lang, int nid, string v) in names)
        {
            // Platform 3 (Windows) AND platform 0 (Unicode) name strings are both UTF-16BE per the
            // OpenType spec; only the legacy Mac platform 1 is a byte encoding. Mirrors the
            // production decode in SfntNameReader — if these two drift, the fixtures stop being
            // fonts and the tests stop meaning anything.
            byte[] bytes = pid is 3 or 0 ? Encoding.BigEndianUnicode.GetBytes(v) : Encoding.ASCII.GetBytes(v);
            AddU16(records, pid);
            // encodingID: 1 = Unicode BMP (Windows), 3 = Unicode 2.0 BMP (Unicode platform).
            AddU16(records, pid switch { 3 => 1, 0 => 3, _ => 0 });
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
    public static byte[] Ttc(params byte[][] faces)
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
}
