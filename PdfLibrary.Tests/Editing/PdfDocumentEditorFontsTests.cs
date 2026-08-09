using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Tests for <see cref="PdfDocumentEditor.SetToUnicode"/> and <see cref="PdfDocumentEditor.HasFont"/>.
///
/// <para>No <c>TestFixtures.Path(...)</c> helper or vendored <c>type0-embedded.pdf</c> /
/// <c>has-tounicode.pdf</c> fixture exists in this project (confirmed by search — the only fixture
/// convention here is hand-built <see cref="PdfDocument"/> construction, per
/// <c>FontInventoryTests.cs</c>'s own comment on the point). These tests follow that established
/// convention instead: documents built directly with <c>AddObject</c>, mirroring
/// <c>FontInventoryTests.BuildType0Document</c>.</para>
/// </summary>
public class PdfDocumentEditorFontsTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    [Fact]
    public void SetToUnicode_WritesAReadableCMapOnTheLogicalFont()
    {
        using var buffer = new MemoryStream();
        using (PdfDocumentEditor editor = BuildType0Document().Edit())
        {
            FontInventoryEntry entry = FontInventory.Read(editor.Document).First();
            editor.SetToUnicode(entry.Id, new Dictionary<int, string> { [0x41] = "A" });
            editor.Save(buffer);
        }

        buffer.Position = 0;
        using PdfDocument reopened = PdfDocument.Load(buffer, "");
        FontInventoryEntry after = FontInventory.Read(reopened).First();
        Assert.True(after.HasToUnicode);
    }

    // The composite-font trap: /ToUnicode on the DESCENDANT is valid syntax that no viewer reads.
    [Fact]
    public void SetToUnicode_WritesOnTheLogicalFontNotTheDescendant()
    {
        using var buffer = new MemoryStream();
        int logical, holder;

        using (PdfDocumentEditor editor = BuildType0Document().Edit())
        {
            FontInventoryEntry entry = Assert.Single(
                FontInventory.Read(editor.Document), e => e.ProgramHolderId != e.Id);
            logical = entry.Id.ObjectNumber;
            holder = entry.ProgramHolderId!.Value.ObjectNumber;

            editor.SetToUnicode(entry.Id, new Dictionary<int, string> { [0x41] = "A" });
            editor.Save(buffer);
        }

        buffer.Position = 0;
        using PdfDocument reopened = PdfDocument.Load(buffer, "");
        Assert.NotNull(Dict(reopened, logical).Get("ToUnicode"));
        Assert.Null(Dict(reopened, holder).Get("ToUnicode"));
    }

    [Fact]
    public void SetToUnicode_ThrowsForAnUnknownFontId()
    {
        using PdfDocumentEditor editor = BuildType0Document().Edit();

        Assert.Throws<ArgumentException>(() =>
            editor.SetToUnicode(new FontId(999_999), new Dictionary<int, string> { [1] = "A" }));
    }

    [Fact]
    public void SetToUnicode_WithAnEmptyMapRemovesTheEntry()
    {
        using var buffer = new MemoryStream();
        using (PdfDocumentEditor editor = BuildSimpleFontWithToUnicodeDocument().Edit())
        {
            FontInventoryEntry entry = FontInventory.Read(editor.Document).First();
            Assert.True(entry.HasToUnicode);
            editor.SetToUnicode(entry.Id, new Dictionary<int, string>());
            editor.Save(buffer);
        }

        buffer.Position = 0;
        using PdfDocument reopened = PdfDocument.Load(buffer, "");
        Assert.False(FontInventory.Read(reopened).First().HasToUnicode);
    }

    [Fact]
    public void HasFont_TrueForARegisteredFontDictionary()
    {
        using PdfDocumentEditor editor = BuildType0Document().Edit();
        FontInventoryEntry entry = FontInventory.Read(editor.Document).First();

        Assert.True(editor.HasFont(entry.Id));
    }

    [Fact]
    public void HasFont_FalseForAnUnknownObjectNumber()
    {
        using PdfDocumentEditor editor = BuildType0Document().Edit();

        Assert.False(editor.HasFont(new FontId(999_999)));
    }

    // Object 22 is a real, registered object in this fixture — the descendant's /FontFile2 stream —
    // but it is a PdfStream, not a PdfDictionary. HasFont must say false, not throw and not confuse
    // "exists" with "is a font dictionary".
    [Fact]
    public void HasFont_FalseWhenTheObjectIsNotADictionary()
    {
        using PdfDocumentEditor editor = BuildType0Document().Edit();

        Assert.False(editor.HasFont(new FontId(22)));
    }

    private static PdfDictionary Dict(PdfDocument document, int objectNumber) =>
        (PdfDictionary)document.Objects[objectNumber];

    /// <summary>A Type0 font (object 20) over its descendant CIDFontType2 (object 21), referenced
    /// from a page's content stream — no /ToUnicode. Mirrors
    /// <c>FontInventoryTests.BuildType0Document</c>, minus its /ToUnicode object, so SetToUnicode has
    /// a genuinely absent entry to add.</summary>
    private static PdfDocument BuildType0Document()
    {
        var doc = new PdfDocument();
        doc.AddObject(22, 0, new PdfStream(new PdfDictionary { [N("Length1")] = new PdfInteger(0) }, []));
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("CIDFontX"),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = new PdfString(Encoding.ASCII.GetBytes("Adobe")),
                [N("Ordering")] = new PdfString(Encoding.ASCII.GetBytes("Identity")),
                [N("Supplement")] = new PdfInteger(0),
            },
            [N("FontDescriptor")] = new PdfDictionary
            {
                [N("Type")] = N("FontDescriptor"),
                [N("FontName")] = N("CIDFontX"),
                [N("FontFile2")] = Ref(22),
            },
        });
        doc.AddObject(20, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("CIDFontX"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(21)),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf <0001> Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(20) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    /// <summary>A single Type1 font (object 30) that already carries a /ToUnicode CMap (object 31),
    /// so removal has a genuine entry to remove.</summary>
    private static PdfDocument BuildSimpleFontWithToUnicodeDocument()
    {
        var doc = new PdfDocument();
        doc.AddObject(31, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(
            "/CIDInit /ProcSet findresource begin\n"
            + "1 begincodespacerange\n<00> <FF>\nendcodespacerange\n"
            + "1 beginbfchar\n<41> <0041>\nendbfchar\n"
            + "endcmap\nend\nend")));
        doc.AddObject(30, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type1"),
            [N("BaseFont")] = N("Helvetica"),
            [N("Encoding")] = N("WinAnsiEncoding"),
            [N("FirstChar")] = new PdfInteger(65),
            [N("LastChar")] = new PdfInteger(65),
            [N("Widths")] = new PdfArray(new PdfInteger(722)),
            [N("ToUnicode")] = Ref(31),
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(30) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }
}
