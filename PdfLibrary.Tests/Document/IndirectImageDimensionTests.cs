using System.Text;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Document;

/// <summary>
/// ISO 32000-1:2008 §7.3.10 lets <em>any</em> dictionary value be an indirect reference, including
/// entries readers habitually assume are direct. A 65 MB scanner-produced contract in the wild
/// writes <c>/Width 5008</c> as a literal and <c>/Height 56 0 R</c> as a reference in the same image
/// dictionary.
///
/// Before the fix, <see cref="PdfImage.Height"/> pattern-matched on <c>PdfInteger</c> only, so an
/// indirect height fell through to the <c>0</c> fallback. That is the worst possible fallback: zero
/// rows decodes to an empty raster and the page renders as blank white **without throwing**, so the
/// document sails through a text-extraction pipeline looking like 13 legitimately empty pages.
///
/// These tests therefore assert two different things on purpose — the resolved dimension, and that
/// the decoded raster is the right size and actually contains ink. A dimensions-only test would
/// still pass against a decoder that silently produced nothing.
/// </summary>
public class IndirectImageDimensionTests
{
    private const int ImageWidth = 16;
    private const int ImageHeight = 8;

    [Fact]
    public void Height_IndirectReference_IsResolved()
    {
        using PdfDocument doc = Load(indirectHeight: true);
        PdfImage image = doc.GetPage(0)!.GetImages().Single();

        Assert.Equal(ImageHeight, image.Height);
    }

    [Fact]
    public void Width_IndirectReference_IsResolved()
    {
        using PdfDocument doc = Load(indirectHeight: true, indirectWidth: true);
        PdfImage image = doc.GetPage(0)!.GetImages().Single();

        Assert.Equal(ImageWidth, image.Width);
    }

    [Fact]
    public void BitsPerComponent_IndirectReference_IsResolved()
    {
        using PdfDocument doc = Load(indirectHeight: true, indirectBpc: true);
        PdfImage image = doc.GetPage(0)!.GetImages().Single();

        Assert.Equal(1, image.BitsPerComponent);
    }

    /// <summary>
    /// The control. If this ever fails, the fixture is wrong rather than the library, and the
    /// indirect cases above would be proving nothing.
    /// </summary>
    [Fact]
    public void DirectValues_StillWork()
    {
        using PdfDocument doc = Load(indirectHeight: false);
        PdfImage image = doc.GetPage(0)!.GetImages().Single();

        Assert.Equal(ImageWidth, image.Width);
        Assert.Equal(ImageHeight, image.Height);
        Assert.Equal(1, image.BitsPerComponent);
    }

    /// <summary>
    /// The regression test that matches the observed symptom: not "the number is wrong" but
    /// "the page came out blank".
    /// </summary>
    /// <remarks>
    /// Deliberately asserts at the RGBA conversion, not at <see cref="PdfImage.GetDecodedData"/>.
    /// That distinction was measured, not assumed: with the fix reverted, the decoded-bytes
    /// version of this test still PASSED, because <c>GetDecodedData</c> returns the stream's
    /// bytes without consulting /Height. The blank page is produced further downstream, where
    /// <see cref="PdfImageToRgba.ToRgba"/> lays those bytes out using the declared dimensions and
    /// a zero height yields an empty raster. A test one layer too low would have been green
    /// against the bug it was written for.
    /// </remarks>
    [Fact]
    public void IndirectHeight_ConvertsToFullRgbaRasterWithInk()
    {
        using PdfDocument doc = Load(indirectHeight: true);
        PdfImage image = doc.GetPage(0)!.GetImages().Single();

        PdfImageToRgba.RgbaImage rgba = PdfImageToRgba.ToRgba(image, doc)
            ?? throw new InvalidOperationException("conversion returned null");

        Assert.Equal(ImageWidth, rgba.Width);
        Assert.Equal(ImageHeight, rgba.Height);
        Assert.Equal(ImageWidth * ImageHeight * 4, rgba.Rgba.Length);

        // A blank page is a uniform raster. Requiring more than one distinct pixel value is
        // what separates "rendered the image" from "rendered nothing".
        Assert.True(rgba.Rgba.Distinct().Count() > 1, "raster is uniform - the page is blank");
    }

    /// <summary>
    /// An indirect height and a direct height must produce byte-identical output. Stated as an
    /// equivalence so the test cannot be satisfied by a decoder that is merely consistent about
    /// being wrong.
    /// </summary>
    [Fact]
    public void IndirectAndDirectHeight_ProduceIdenticalRasters()
    {
        using PdfDocument indirect = Load(indirectHeight: true);
        using PdfDocument direct = Load(indirectHeight: false);

        PdfImageToRgba.RgbaImage a =
            PdfImageToRgba.ToRgba(indirect.GetPage(0)!.GetImages().Single(), indirect)!.Value;
        PdfImageToRgba.RgbaImage b =
            PdfImageToRgba.ToRgba(direct.GetPage(0)!.GetImages().Single(), direct)!.Value;

        Assert.Equal(b.Height, a.Height);
        Assert.Equal(b.Rgba, a.Rgba);
    }

    /// <summary>
    /// 16x8 1-bit raster: alternating 0xF0/0x0F byte pairs, so every row carries ink and the
    /// pattern is position-sensitive (a truncated or mis-strided decode cannot match by accident).
    /// </summary>
    private static byte[] ExpectedRaster { get; } = BuildRaster();

    private static byte[] BuildRaster()
    {
        int bytesPerRow = (ImageWidth + 7) / 8;
        var raster = new byte[bytesPerRow * ImageHeight];
        for (var row = 0; row < ImageHeight; row++)
        {
            raster[row * bytesPerRow] = (byte)(row % 2 == 0 ? 0xF0 : 0x0F);
            raster[row * bytesPerRow + 1] = (byte)(row % 2 == 0 ? 0x0F : 0xF0);
        }
        return raster;
    }

    /// <summary>
    /// Builds a minimal one-page PDF carrying a single uncompressed 1-bit image XObject, with any
    /// combination of /Width, /Height and /BitsPerComponent written as indirect references to
    /// standalone integer objects (objects 6, 7 and 8).
    /// </summary>
    /// <remarks>
    /// Uncompressed on purpose. A filtered stream would make a failure ambiguous between dimension
    /// resolution and the codec, and dimension resolution is what is under test.
    /// </remarks>
    private static PdfDocument Load(bool indirectHeight, bool indirectWidth = false,
        bool indirectBpc = false)
    {
        var bytes = new List<byte>();
        var offset = new Dictionary<int, long>();

        void Append(string s) => bytes.AddRange(Encoding.Latin1.GetBytes(s));
        void StartObj(int n)
        {
            offset[n] = bytes.Count;
            Append($"{n} 0 obj\n");
        }

        string width = indirectWidth ? "6 0 R" : ImageWidth.ToString();
        string height = indirectHeight ? "7 0 R" : ImageHeight.ToString();
        string bpc = indirectBpc ? "8 0 R" : "1";

        Append("%PDF-1.7\n");

        StartObj(1);
        Append("<</Type/Catalog/Pages 2 0 R>>\nendobj\n");
        StartObj(2);
        Append("<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n");
        StartObj(3);
        Append("<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]"
               + "/Resources<</XObject<</Im0 4 0 R>>>>/Contents 5 0 R>>\nendobj\n");

        StartObj(4);
        Append($"<</Type/XObject/Subtype/Image/Width {width}/Height {height}"
               + $"/ColorSpace/DeviceGray/BitsPerComponent {bpc}"
               + $"/Length {ExpectedRaster.Length}>>\nstream\n");
        bytes.AddRange(ExpectedRaster);
        Append("\nendstream\nendobj\n");

        const string content = "q 16 0 0 8 0 0 cm /Im0 Do Q";
        StartObj(5);
        Append($"<</Length {content.Length}>>\nstream\n{content}\nendstream\nendobj\n");

        // The standalone integers the image dictionary may point at.
        StartObj(6);
        Append($"{ImageWidth}\nendobj\n");
        StartObj(7);
        Append($"{ImageHeight}\nendobj\n");
        StartObj(8);
        Append("1\nendobj\n");

        long xrefStart = bytes.Count;
        Append("xref\n0 9\n");
        Append("0000000000 65535 f\r\n");
        for (var n = 1; n <= 8; n++)
            Append($"{offset[n]:D10} 00000 n\r\n");
        Append("trailer\n<</Size 9/Root 1 0 R>>\n");
        Append($"startxref\n{xrefStart}\n%%EOF");

        return PdfDocument.Load(new MemoryStream([.. bytes]));
    }
}
