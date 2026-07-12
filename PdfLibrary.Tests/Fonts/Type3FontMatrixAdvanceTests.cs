using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// A Type 3 font's glyph widths are in the font's own glyph space and must be transformed to text
/// space by its /FontMatrix, not a hardcoded 1/1000 (ISO 32000-1 §9.2.4, §9.6.5). Chrome/Skia emits
/// Type 3 fonts with FontMatrix [1/2048 0 0 -1/2048 0 0]; treating the width as 1000-unit made every
/// advance 2.048× too large and scrambled the text. GetGlyphAdvanceWidth returns the text-space
/// advance (em fraction) = width × FontMatrix[0]; the renderer multiplies it by the font size.
/// </summary>
public class Type3FontMatrixAdvanceTests
{
    private static Type3Font Build(double fontMatrixScale, double[] widths)
    {
        var widthsArr = new PdfArray();
        foreach (double w in widths) widthsArr.Add(new PdfReal(w));

        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type3"),
            [new PdfName("FontBBox")] = new PdfArray
                { new PdfInteger(0), new PdfInteger(0), new PdfInteger(2048), new PdfInteger(2048) },
            [new PdfName("FontMatrix")] = new PdfArray
            {
                new PdfReal(fontMatrixScale), new PdfInteger(0),
                new PdfInteger(0), new PdfReal(-fontMatrixScale),
                new PdfInteger(0), new PdfInteger(0)
            },
            [new PdfName("FirstChar")] = new PdfInteger(0),
            [new PdfName("LastChar")] = new PdfInteger(widths.Length - 1),
            [new PdfName("Widths")] = widthsArr
        };
        return (Type3Font)PdfFont.Create(dict)!;
    }

    // Chrome/Skia's 1/2048 FontMatrix: a 1024-unit glyph is 0.5 em, a 632-unit glyph is 0.30859375 em.
    [Fact]
    public void Skia2048FontMatrix_ScalesWidthsToTextSpace()
    {
        Type3Font font = Build(1.0 / 2048, [1024, 632]);
        Assert.Equal(0.5, font.GetGlyphAdvanceWidth(0), 10);
        Assert.Equal(632.0 / 2048, font.GetGlyphAdvanceWidth(1), 10);
    }

    // Ordinary Type 3 font (FontMatrix 1/1000): the result must match the historical /1000 behaviour,
    // so the fix is backward-compatible with fonts that were already rendering correctly.
    [Fact]
    public void Standard1000FontMatrix_MatchesLegacyThousandthsBehaviour()
    {
        Type3Font font = Build(0.001, [500, 250]);
        Assert.Equal(0.5, font.GetGlyphAdvanceWidth(0), 10);
        Assert.Equal(0.25, font.GetGlyphAdvanceWidth(1), 10);
    }
}
