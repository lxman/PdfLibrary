using System.IO;
using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// Pins ISO 32000-2 §9.2.4: the /Widths array is what positions glyphs in a simple font, and the
/// font program's own advances are a fallback for when the dictionary cannot answer.
///
/// <para>NOTE 3 of that clause is the reason this matters — TrueType stores advances in units of
/// 1024 or 2048 per em against /Widths' 1000, so the two are EXPECTED to differ slightly. An engine
/// that prefers the program therefore shifts text by a sub-point amount on every glyph the moment a
/// program is embedded, which is exactly what font remediation must never do.</para>
/// </summary>
public class WidthPrecedenceTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    [Fact]
    public void A_usable_Widths_entry_wins_over_the_embedded_program()
    {
        // Fixture: a TrueType font dict whose /Widths says 500 for 'A' while the embedded
        // program's hmtx says something else. /Widths must win.
        using PdfDocument document = WidthFixtures.TrueTypeWithWidthsAndProgram(
            code: 'A', declaredWidth: 500);

        double width = WidthFixtures.WidthOf(document, 'A');

        Assert.Equal(500, width, precision: 3);
    }

    [Fact]
    public void A_zero_Widths_entry_falls_through_to_the_program()
    {
        // The degenerate case the original program-first ordering was written for: a /Widths array
        // present but carrying no usable value. Zero is a legal advance, so this is a deliberate
        // concession to broken files rather than a claim that zero is invalid.
        using PdfDocument document = WidthFixtures.TrueTypeWithWidthsAndProgram(
            code: 'A', declaredWidth: 0);

        double width = WidthFixtures.WidthOf(document, 'A');

        Assert.Equal(WidthFixtures.ProgramAdvanceFor('A'), width, precision: 3);
    }

    [Fact]
    public void A_code_outside_FirstChar_LastChar_falls_through_to_the_program()
    {
        using PdfDocument document = WidthFixtures.TrueTypeWithWidthsAndProgram(
            code: 'A', declaredWidth: 500);

        double width = WidthFixtures.WidthOf(document, 'Z');   // outside the declared range

        Assert.Equal(WidthFixtures.ProgramAdvanceFor('Z'), width, precision: 3);
    }
}

/// <summary>
/// Hand-built font-dictionary fixtures for <see cref="WidthPrecedenceTests"/>, following the
/// established pattern in <c>PdfLibrary.Tests/Editing/PdfDocumentEditorFontsTests.cs</c>: documents
/// built directly with <c>AddObject</c>, no corpus file (<c>TestPDFs/</c> is gitignored and absent on
/// CI).
///
/// <para>The embedded program is the real system-substitute font bundled with this test project
/// (<c>Resources/PublicPixel.ttf</c>, already used by <c>SystemFontLocatorTests</c> and others) rather
/// than a synthetic sfnt — the brief explicitly permits this route. Its glyph advances for 'A' and 'Z'
/// are read directly off the font's own hmtx table via <see cref="EmbeddedFontMetrics"/>, so the
/// fixture proves precedence with the program's ACTUAL advance rather than an asserted number.</para>
/// </summary>
internal static class WidthFixtures
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    private static byte[]? _fontBytes;
    private static byte[] FontBytes => _fontBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    /// <summary>The embedded program's own advance for <paramref name="code"/>, scaled to the PDF
    /// 1000-unit space exactly as <c>TrueTypeFont.GetCharacterWidth</c> does. This is what a test
    /// asserts against when the program is expected to win — never a made-up number.</summary>
    internal static double ProgramAdvanceFor(int code)
    {
        var metrics = new EmbeddedFontMetrics(FontBytes);
        Assert.True(metrics.IsValid);
        ushort raw = metrics.GetCharacterAdvanceWidth((ushort)code);
        Assert.True(raw > 0, $"Fixture font has no usable advance for code {code}.");
        return raw * 1000.0 / metrics.UnitsPerEm;
    }

    /// <summary>
    /// A single simple TrueType font dict (object 10) declaring <paramref name="declaredWidth"/> for
    /// <paramref name="code"/>, over an embedded /FontFile2 (object 12, the real PublicPixel.ttf
    /// bytes) whose hmtx gives that code a genuinely different advance. The setup asserts the two
    /// values differ so the precedence tests cannot pass by coincidence.
    /// </summary>
    internal static PdfDocument TrueTypeWithWidthsAndProgram(int code, int declaredWidth)
    {
        double programAdvance = ProgramAdvanceFor(code);
        Assert.NotEqual(programAdvance, declaredWidth, 3);

        var doc = new PdfDocument();
        doc.AddObject(12, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(FontBytes.Length) }, FontBytes));
        doc.AddObject(11, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("PublicPixelTest"),
            [N("Flags")] = new PdfInteger(32),
            [N("FontFile2")] = Ref(12),
        });
        // FirstChar/LastChar span exactly the fixture's declared code, so a lookup for anything
        // else (e.g. 'Z' when the fixture was built for 'A') is genuinely out of range.
        doc.AddObject(10, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("PublicPixelTest"),
            [N("Encoding")] = N("WinAnsiEncoding"),
            [N("FirstChar")] = new PdfInteger(code),
            [N("LastChar")] = new PdfInteger(code),
            [N("Widths")] = new PdfArray(new PdfInteger(declaredWidth)),
            [N("FontDescriptor")] = Ref(11),
        });
        doc.AddObject(4, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf <41> Tj ET")));
        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(4),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(10) } },
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

    /// <summary>Resolves the font dict at object 10 to a <see cref="PdfFont"/> and asks it for the
    /// width of <paramref name="code"/> — the actual production code path under test.</summary>
    internal static double WidthOf(PdfDocument document, int code)
    {
        var dictionary = (PdfDictionary)document.Objects[10];
        PdfFont? font = PdfFont.Create(dictionary, document);
        Assert.NotNull(font);
        return font!.GetCharacterWidth(code);
    }
}
