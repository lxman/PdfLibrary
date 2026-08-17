using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Remediation;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>
/// F-4b Task 4: <see cref="CidReplacementMap"/> — the ToUnicode-driven, all-or-nothing CID
/// coverage answer (spec §3 step 2) and the big-endian CIDToGIDMap stream builder (step 4).
/// Substitute is the committed Liberation asset, loaded the same way as
/// <c>EmbedProgramRoundTripTests.LoadLiberationFace</c>.
/// </summary>
public sealed class CidReplacementMapTests
{
    private static EmbeddedFontMetrics LoadLiberationSans()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Resources", "Liberation", "LiberationSans-Regular.ttf");
        return new EmbeddedFontMetrics(File.ReadAllBytes(path));
    }

    private static ToUnicodeCMap BfChar(params (int Code, string HexUnicode)[] entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/CIDInit /ProcSet findresource begin");
        sb.AppendLine("12 dict begin");
        sb.AppendLine("begincmap");
        sb.AppendLine($"{entries.Length} beginbfchar");
        foreach ((int code, string hexUnicode) in entries)
            sb.AppendLine($"<{code:X4}> <{hexUnicode}>");
        sb.AppendLine("endbfchar");
        sb.AppendLine("endcmap");
        sb.AppendLine("CMapName currentdict /CMap defineresource pop");
        sb.AppendLine("end");
        sb.AppendLine("end");
        return ToUnicodeCMap.Parse(Encoding.ASCII.GetBytes(sb.ToString()));
    }

    [Fact] // <0041> 'A' — Liberation covers it
    public void A_covered_cid_maps_to_a_real_glyph()
    {
        EmbeddedFontMetrics substitute = LoadLiberationSans();
        ToUnicodeCMap toUnicode = BfChar((0x41, "0041"));

        CidReplacementMapResult result = CidReplacementMap.Build(toUnicode, [0x41], substitute);

        Assert.NotEqual(0, result.CidToGid[0x41]);
        Assert.Empty(result.Unresolvable);
        Assert.Equal(0x41, result.MaxCid);
    }

    [Fact] // bfchar value <E000> (private use area — absent from Liberation)
    public void A_cid_whose_unicode_the_substitute_lacks_is_unresolvable()
    {
        EmbeddedFontMetrics substitute = LoadLiberationSans();
        // Confirm the premise: Liberation Sans has no glyph for U+E000.
        Assert.Equal(0, substitute.GetGlyphIdByUnicode(0xE000));

        ToUnicodeCMap toUnicode = BfChar((0x10, "E000"));

        CidReplacementMapResult result = CidReplacementMap.Build(toUnicode, [0x10], substitute);

        Assert.DoesNotContain(0x10, result.CidToGid.Keys);
        Assert.Contains(0x10, result.Unresolvable);
    }

    [Fact] // bfchar value <00660066 0069>-style multi-code-point expansion ("ffi")
    public void A_multi_code_point_tounicode_value_is_unresolvable()
    {
        EmbeddedFontMetrics substitute = LoadLiberationSans();
        ToUnicodeCMap toUnicode = BfChar((0x20, "0066 0066 0069"));

        // Confirm the premise: the CMap really does resolve to a 3-character string.
        Assert.Equal("ffi", toUnicode.Lookup(0x20));

        CidReplacementMapResult result = CidReplacementMap.Build(toUnicode, [0x20], substitute);

        Assert.DoesNotContain(0x20, result.CidToGid.Keys);
        Assert.Contains(0x20, result.Unresolvable);
    }

    [Fact]
    public void A_cid_with_no_tounicode_entry_is_unresolvable()
    {
        EmbeddedFontMetrics substitute = LoadLiberationSans();
        ToUnicodeCMap toUnicode = BfChar((0x41, "0041")); // CID 0x99 deliberately absent

        CidReplacementMapResult result = CidReplacementMap.Build(toUnicode, [0x99], substitute);

        Assert.DoesNotContain(0x99, result.CidToGid.Keys);
        Assert.Contains(0x99, result.Unresolvable);
    }

    [Fact]
    public void The_stream_serialises_big_endian_with_zeros_for_unused_cids()
    {
        var cidToGid = new Dictionary<int, ushort> { [1] = 0x0102, [3] = 0x0304 };

        byte[] bytes = CidReplacementMap.ToStreamBytes(cidToGid, maxCid: 3);

        Assert.Equal(new byte[] { 0, 0, 1, 2, 0, 0, 3, 4 }, bytes);
    }
}
