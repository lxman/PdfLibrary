using System.Text;
using FontParser.Tables.Name;

namespace FontParser.Tests.Tables;

public class NameTableTests
{
    /// <summary>A minimal `name` table (format 0, no lang-tag records) carrying whichever
    /// (platformId, encodingId, languageId, nameId, value) records the caller supplies. Windows
    /// (platform 3) records with encodingId 1 (Unicode BMP) are UTF-16BE per the OpenType spec,
    /// mirroring both the production decode in <see cref="NameRecord.Process"/> and the
    /// PdfLibrary.Tests SfntFixtures.Sfnt builder's own convention.</summary>
    private static byte[] BuildNameTable(
        params (ushort PlatformId, ushort EncodingId, ushort LanguageId, ushort NameId, string Value)[] records)
    {
        var storage = new List<byte>();
        var recordBytes = new List<byte>();

        void U16(List<byte> b, int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }

        foreach ((ushort platformId, ushort encodingId, ushort languageId, ushort nameId, string value) in records)
        {
            byte[] bytes = Encoding.BigEndianUnicode.GetBytes(value);
            U16(recordBytes, platformId);
            U16(recordBytes, encodingId);
            U16(recordBytes, languageId);
            U16(recordBytes, nameId);
            U16(recordBytes, bytes.Length);
            U16(recordBytes, storage.Count); // offset into storage
            storage.AddRange(bytes);
        }

        var table = new List<byte>();
        U16(table, 0);              // format
        U16(table, records.Length); // count
        U16(table, 6 + recordBytes.Count); // stringOffset
        table.AddRange(recordBytes);
        table.AddRange(storage);
        return table.ToArray();
    }

    // Platform 3 (Windows), encoding 1 (Unicode BMP), language 0x409 (English - United States) — a
    // real-world combination Language.Ids actually recognises.
    private const ushort Windows = 3;
    private const ushort UnicodeBmp = 1;
    private const ushort EnglishUs = 0x409;

    [Fact]
    public void GetFamilyName_returns_the_name_id_1_record()
    {
        // NameId 1 translates to "Family" (NameIdTranslator.Translate). A prior version of
        // GetFamilyName() compared against "Font Family name" — a string the translator never
        // actually produces — so this getter always returned null for every font, silently. Pins
        // the fix (PdfLibrary Task 6 follow-up).
        byte[] data = BuildNameTable(
            (Windows, UnicodeBmp, EnglishUs, 1, "Test Family"),
            (Windows, UnicodeBmp, EnglishUs, 2, "Regular"));

        var table = new NameTable(data);

        Assert.Equal("Test Family", table.GetFamilyName());
    }

    [Fact]
    public void GetPostScriptName_returns_the_name_id_6_record()
    {
        // NameId 6 translates to "PostScript Name" (capital N). A prior version of
        // GetPostScriptName() compared against "PostScript name" (lowercase n), so it also always
        // returned null.
        byte[] data = BuildNameTable((Windows, UnicodeBmp, EnglishUs, 6, "TestFamily-Regular"));

        var table = new NameTable(data);

        Assert.Equal("TestFamily-Regular", table.GetPostScriptName());
    }

    [Fact]
    public void GetFamilyName_returns_null_when_no_name_id_1_record_exists()
    {
        byte[] data = BuildNameTable((Windows, UnicodeBmp, EnglishUs, 2, "Regular"));

        var table = new NameTable(data);

        Assert.Null(table.GetFamilyName());
    }
}
