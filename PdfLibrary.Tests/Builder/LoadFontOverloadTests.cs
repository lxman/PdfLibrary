using PdfLibrary.Builder;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Builder;

public class LoadFontOverloadTests
{
    // Public-domain (CC0) test font; see Resources/PublicPixel.LICENSE.txt.
    private static byte[] FontBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    [Fact]
    public void LoadFont_FromBytes_RegistersTheFont()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().LoadFont(FontBytes(), "Pixel");

        Assert.True(builder.CustomFonts.ContainsKey("Pixel"));
    }

    [Fact]
    public void LoadFont_FromStream_RegistersTheFont()
    {
        using var stream = new MemoryStream(FontBytes());

        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().LoadFont(stream, "Pixel");

        Assert.True(builder.CustomFonts.ContainsKey("Pixel"));
    }

    [Fact]
    public void LoadFont_FromBytes_NullData_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PdfDocumentBuilder.Create().LoadFont((byte[])null!, "X"));
    }

    // End-to-end smoke: a byte[]-loaded custom font flows through to a usable PDF.
    [Fact]
    public void LoadFont_FromBytes_ProducesUsablePdf()
    {
        byte[] pdf = PdfDocumentBuilder.Create()
            .LoadFont(FontBytes(), "Pixel")
            .AddPage(p => p.AddText("Hello", 100, 700, "Pixel", 12))
            .ToByteArray();

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);
        Assert.Equal(1, doc.PageCount);
    }
}
