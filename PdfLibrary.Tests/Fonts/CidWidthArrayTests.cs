using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Issue 24: CidFont.ParseWidthArray discarded a legal /W whose inner width array is an indirect
/// reference (e.g. /W [1 56 0 R]), so every CID fell back to /DW (default 1000). Reproducer:
/// "XS Benefits overview.pdf" (local-708) — 14 Type0 fonts all returning 1000 for every CID,
/// page 1 unreadable. These are ordinary unit tests, NOT LocalOnly: fixtures are in memory.
/// </summary>
public class CidWidthArrayTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    private static PdfDictionary CidDict(PdfObject w) => new()
    {
        [N("Type")] = N("Font"),
        [N("Subtype")] = N("CIDFontType2"),
        [N("BaseFont")] = N("Test"),
        [N("DW")] = new PdfInteger(1000),
        [N("W")] = w,
    };

    [Fact]
    public void Indirect_inner_width_array_is_resolved()
    {
        // The reproducer's shape: /W [1 <ref>] where <ref> resolves to the per-CID width list.
        using var doc = new PdfDocument();
        doc.AddObject(56, 0, new PdfArray(
            new PdfInteger(560), new PdfInteger(534), new PdfInteger(651),
            new PdfInteger(765), new PdfInteger(481)));

        var font = new CidFont(CidDict(new PdfArray(new PdfInteger(1), Ref(56))), doc);

        Assert.Equal(560, font.GetCharacterWidth(1));
        Assert.Equal(534, font.GetCharacterWidth(2));
        Assert.Equal(651, font.GetCharacterWidth(3));
        Assert.Equal(765, font.GetCharacterWidth(4));
        Assert.Equal(481, font.GetCharacterWidth(5));
        Assert.Equal(1000, font.GetCharacterWidth(6)); // outside /W → /DW
    }

    [Fact]
    public void Direct_inner_width_array_still_parses()
    {
        using var doc = new PdfDocument();
        var w = new PdfArray(new PdfInteger(1),
            new PdfArray(new PdfInteger(560), new PdfInteger(534)));
        var font = new CidFont(CidDict(w), doc);
        Assert.Equal(560, font.GetCharacterWidth(1));
        Assert.Equal(534, font.GetCharacterWidth(2));
    }

    [Fact]
    public void Format_two_range_still_parses()
    {
        using var doc = new PdfDocument();
        var w = new PdfArray(new PdfInteger(1), new PdfInteger(5), new PdfInteger(700));
        var font = new CidFont(CidDict(w), doc);
        Assert.Equal(700, font.GetCharacterWidth(1));
        Assert.Equal(700, font.GetCharacterWidth(5));
        Assert.Equal(1000, font.GetCharacterWidth(6));
    }

    [Fact]
    public void Indirect_element_inside_inner_width_array_is_resolved()
    {
        using var doc = new PdfDocument();
        doc.AddObject(57, 0, new PdfInteger(560));
        var w = new PdfArray(new PdfInteger(1), new PdfArray(Ref(57), new PdfInteger(534)));
        var font = new CidFont(CidDict(w), doc);
        Assert.Equal(560, font.GetCharacterWidth(1));
        Assert.Equal(534, font.GetCharacterWidth(2));
    }

    [Fact]
    public void Unresolvable_reference_degrades_to_default_width_without_throwing()
    {
        // Object 99 is never registered — the reference cannot resolve. The fix must stop
        // discarding a READABLE array; an unreadable one keeps today's degrade-to-DW behaviour.
        using var doc = new PdfDocument();
        var font = new CidFont(CidDict(new PdfArray(new PdfInteger(1), Ref(99))), doc);
        Assert.Equal(1000, font.GetCharacterWidth(1));
    }

    [Fact]
    public void Indirect_inner_array_with_null_document_degrades_to_default_width()
    {
        var font = new CidFont(CidDict(new PdfArray(new PdfInteger(1), Ref(56))), null);
        Assert.Equal(1000, font.GetCharacterWidth(1));
    }

    [Fact]
    public void Garbage_element_in_width_slot_degrades_to_default_width()
    {
        using var doc = new PdfDocument();
        var font = new CidFont(CidDict(new PdfArray(new PdfInteger(1), N("NotAWidth"))), doc);
        Assert.Equal(1000, font.GetCharacterWidth(1));
    }
}
