using System.Text;
using ICCSharp.Tags;
using ICCSharp.Profile;

namespace ICCSharp.Tests.Tags;

public class TextTagTests
{
    private static byte[] WithHeader(string typeSig, params byte[] payload)
    {
        var buf = new byte[8 + payload.Length];
        for (var i = 0; i < 4; i++) buf[i] = (byte)typeSig[i];
        Buffer.BlockCopy(payload, 0, buf, 8, payload.Length);
        return buf;
    }

    private static byte[] U32Be(uint v) => new[]
    {
        (byte)((v >> 24) & 0xFF), (byte)((v >> 16) & 0xFF),
        (byte)((v >> 8) & 0xFF),  (byte)(v & 0xFF),
    };

    private static byte[] U16Be(ushort v) => new[] { (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF) };

    // --- textType ---------------------------------------------------------

    [Fact]
    public void TextType_returns_ascii_string_without_trailing_nul()
    {
        byte[] payload = Encoding.ASCII.GetBytes("Copyright (c) 1998 Hewlett-Packard\0");
        var t = Assert.IsType<TextTagElement>(TagElementReader.Parse(WithHeader("text", payload)));
        Assert.Equal("Copyright (c) 1998 Hewlett-Packard", t.Value);
    }

    [Fact]
    public void TextType_with_empty_payload_returns_empty_string()
    {
        var t = Assert.IsType<TextTagElement>(TagElementReader.Parse(WithHeader("text")));
        Assert.Equal(string.Empty, t.Value);
    }

    // --- textDescriptionType ----------------------------------------------

    [Fact]
    public void TextDescription_with_only_ascii_section()
    {
        byte[] asciiBytes = Encoding.ASCII.GetBytes("sRGB IEC61966-2.1\0");
        byte[] payload =
        [
            ..U32Be((uint)asciiBytes.Length),
            ..asciiBytes,
            ..U32Be(0),         // unicode language
            ..U32Be(0),         // unicode count = 0
            ..U16Be(0),         // script code
            (byte)0,            // mac description length
            ..new byte[67],     // mac block
        ];
        var t = Assert.IsType<TextDescriptionTagElement>(
            TagElementReader.Parse(WithHeader("desc", payload)));

        Assert.Equal("sRGB IEC61966-2.1", t.AsciiDescription);
        Assert.Null(t.UnicodeDescription);
        Assert.Null(t.MacintoshDescription);
    }

    [Fact]
    public void TextDescription_with_unicode_section()
    {
        byte[] asciiBytes = Encoding.ASCII.GetBytes("Hello\0");
        // UTF-16BE "Bonjour" + NUL: 0x00 'B' 0x00 'o' ... 0x00 0x00
        byte[] unicodeBytes = Encoding.BigEndianUnicode.GetBytes("Bonjour\0");
        var unicodeCount = (uint)(unicodeBytes.Length / 2);

        byte[] payload =
        [
            ..U32Be((uint)asciiBytes.Length),
            ..asciiBytes,
            ..U32Be(0x66724652),    // language code 'frFR'
            ..U32Be(unicodeCount),
            ..unicodeBytes,
            ..U16Be(0),
            (byte)0,
            ..new byte[67],
        ];
        var t = Assert.IsType<TextDescriptionTagElement>(
            TagElementReader.Parse(WithHeader("desc", payload)));

        Assert.Equal("Hello", t.AsciiDescription);
        Assert.Equal((uint)0x66724652, t.UnicodeLanguageCode);
        Assert.Equal("Bonjour", t.UnicodeDescription);
    }

    [Fact]
    public void TextDescription_with_macintosh_section()
    {
        byte[] asciiBytes = Encoding.ASCII.GetBytes("Hi\0");
        var macBlock = new byte[67];
        byte[] macStr = Encoding.ASCII.GetBytes("MacHi");
        Buffer.BlockCopy(macStr, 0, macBlock, 0, macStr.Length);
        byte[] payload =
        [
            ..U32Be((uint)asciiBytes.Length),
            ..asciiBytes,
            ..U32Be(0),
            ..U32Be(0),
            ..U16Be(0),
            (byte)macStr.Length,
            ..macBlock,
        ];
        var t = Assert.IsType<TextDescriptionTagElement>(
            TagElementReader.Parse(WithHeader("desc", payload)));

        Assert.Equal("MacHi", t.MacintoshDescription);
    }

    [Fact]
    public void TextDescription_ascii_count_overflow_throws()
    {
        byte[] payload =
        [
            ..U32Be(1000), // declares 1000-byte ASCII section
            (byte)'X',     // but only 1 byte present
        ];
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("desc", payload)));
    }

    // --- multiLocalizedUnicodeType ----------------------------------------

    [Fact]
    public void Mluc_with_single_record()
    {
        // Tag data layout starting at offset 0 of full tag (NOT of payload):
        //   0..7   'mluc' + 4 reserved
        //   8..11  recordCount = 1
        //   12..15 recordSize  = 12
        //   16..27 record: langCode(2) + countryCode(2) + length(4) + offset(4)
        //   28..   UTF-16BE text
        var text = "Hello";
        byte[] uniBytes = Encoding.BigEndianUnicode.GetBytes(text);

        byte[] payload =
        [
            ..U32Be(1),   // recordCount
            ..U32Be(12),  // recordSize
            // record:
            (byte)'e', (byte)'n',                    // language code "en"
            (byte)'U', (byte)'S',                    // country code  "US"
            ..U32Be((uint)uniBytes.Length),          // string byte length
            ..U32Be(28),                             // offset from start of tag (8 header + 4+4 count/size + 12 record = 28)
            ..uniBytes,
        ];
        var t = Assert.IsType<MultiLocalizedUnicodeTagElement>(
            TagElementReader.Parse(WithHeader("mluc", payload)));

        Assert.Single(t.Records);
        Assert.Equal("en", t.Records[0].LanguageCode);
        Assert.Equal("US", t.Records[0].CountryCode);
        Assert.Equal("Hello", t.Records[0].Text);
        Assert.Equal("Hello", t.FirstText);
    }

    [Fact]
    public void Mluc_with_two_records_at_different_offsets()
    {
        var en = "Color";
        var fr = "Couleur";
        byte[] enBytes = Encoding.BigEndianUnicode.GetBytes(en);
        byte[] frBytes = Encoding.BigEndianUnicode.GetBytes(fr);

        // Two records (12 bytes each) → strings start at 8 + 8 + 24 = 40 (en), 40 + enBytes.Length (fr).
        uint enOffset = 40;
        uint frOffset = enOffset + (uint)enBytes.Length;

        byte[] payload =
        [
            ..U32Be(2),
            ..U32Be(12),
            (byte)'e', (byte)'n', (byte)'U', (byte)'S',
            ..U32Be((uint)enBytes.Length),
            ..U32Be(enOffset),
            (byte)'f', (byte)'r', (byte)'F', (byte)'R',
            ..U32Be((uint)frBytes.Length),
            ..U32Be(frOffset),
            ..enBytes,
            ..frBytes,
        ];
        var t = Assert.IsType<MultiLocalizedUnicodeTagElement>(
            TagElementReader.Parse(WithHeader("mluc", payload)));

        Assert.Equal(2, t.Records.Count);
        Assert.Equal("Color",   t.Records[0].Text);
        Assert.Equal("Couleur", t.Records[1].Text);
    }

    [Fact]
    public void Mluc_record_size_below_12_throws()
    {
        byte[] payload = [..U32Be(1), ..U32Be(8)];
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("mluc", payload)));
    }

    [Fact]
    public void Mluc_string_range_outside_tag_throws()
    {
        byte[] payload =
        [
            ..U32Be(1),
            ..U32Be(12),
            (byte)'e', (byte)'n', (byte)'U', (byte)'S',
            ..U32Be(10),
            ..U32Be(10_000), // wildly out of bounds
        ];
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("mluc", payload)));
    }

    [Fact]
    public void Mluc_with_zero_records()
    {
        byte[] payload = [..U32Be(0), ..U32Be(12)];
        var t = Assert.IsType<MultiLocalizedUnicodeTagElement>(
            TagElementReader.Parse(WithHeader("mluc", payload)));
        Assert.Empty(t.Records);
        Assert.Equal(string.Empty, t.FirstText);
    }
}
