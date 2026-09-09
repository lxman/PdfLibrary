using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;

namespace PdfLibrary.Tests.Content;

public class PageContentArrayTextExtractionTests
{
    [Fact]
    public void FontState_CarriesAcrossContentArrayMembers_ForTextAndFragments()
    {
        var toUnicode = new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(@"
3 beginbfchar
<0003> <0041>
<0004> <0042>
<0005> <0043>
endbfchar"));
        var cidFont = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("CIDFontType2"),
            [new PdfName("BaseFont")] = new PdfName("TestCID"),
        };
        var type0Font = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Font"),
            [new PdfName("Subtype")] = new PdfName("Type0"),
            [new PdfName("BaseFont")] = new PdfName("TestComposite"),
            [new PdfName("Encoding")] = new PdfName("Identity-H"),
            [new PdfName("DescendantFonts")] = new PdfArray { cidFont },
            [new PdfName("ToUnicode")] = toUnicode,
        };
        var page = new PdfPage(new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Page"),
            [new PdfName("Resources")] = new PdfDictionary
            {
                [new PdfName("Font")] = new PdfDictionary
                {
                    [new PdfName("F0")] = type0Font,
                },
            },
            [new PdfName("Contents")] = new PdfArray
            {
                new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf ET")),
                new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT 10 20 Td <000300040005> Tj ET")),
            },
        });

        Assert.Contains("ABC", page.ExtractText());
        (string text, List<TextFragment> fragments) = page.ExtractTextWithFragments();
        Assert.Contains("ABC", text);
        Assert.DoesNotContain('\0', text);
        Assert.Equal("ABC", Assert.Single(fragments).Text);
        Assert.Equal("F0", fragments[0].FontName);
    }
}
