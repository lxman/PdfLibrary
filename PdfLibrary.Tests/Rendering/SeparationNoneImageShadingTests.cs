using SkiaSharp;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ISO 32000-2 §8.6.6.4 row 4-8 — "shall not produce any visible output […] shall have no effect on
/// the current page" — for the painting operators that do NOT take their colour from the graphics
/// state: image XObjects, inline images, and the <c>sh</c> operator.
///
/// <para>
/// Each case paints over an existing red rectangle with a <c>/None</c> space whose tint transform ramps
/// to solid black, so an implementation that decodes normally paints black and fails. The backdrop is
/// deliberately red rather than white: a renderer that resolved <c>/None</c> to white would still mark
/// the page, and would look correct against white.
/// </para>
///
/// <para>
/// The stencil-mask case is the odd one out and the reason it is here. An image with
/// <c>/ImageMask true</c> has no colour space of its own — it paints with the current FILL colour — so
/// it must be gated on <c>FillPaintsNothing</c>, not on the image's (absent) space. Gating it the other
/// way leaves <c>/None</c> stencil masks painting, which is exactly the bug class this row describes.
/// </para>
/// </summary>
public class SeparationNoneImageShadingTests
{
    /// <summary>A type 2 tint transform ramping white → black in DeviceRGB.</summary>
    private const string ToBlack = "<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 0] /N 1 >>";

    /// <summary>Paints the red backdrop the /None operator must fail to disturb.</summary>
    private const string RedRect = "1 0 0 rg 100 400 200 200 re f ";

    [Fact]
    public void SeparationNone_ImageXObject_LeavesExistingContentUntouched()
    {
        // 2x2 8-bit single-component image, every sample at full tint (0xFF).
        const string img = "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 " +
                           "/ColorSpace [/Separation /None /DeviceRGB " + ToBlack + "] " +
                           "/BitsPerComponent 8 /Length 4 >>\r\nstream\r\n" +
                           "\u00FF\u00FF\u00FF\u00FF\r\nendstream";
        const string content = RedRect + "q 200 0 0 200 100 400 cm /Im0 Do Q";

        byte[] pdf = ColourConformancePage.Build("/DeviceRGB", content, withFont: false,
            extraResources: " /XObject << /Im0 5 0 R >>", extraObjects: img);

        ColourConformancePage.AssertRedRectUntouched(pdf, "/None image XObject");
    }

    [Fact]
    public void SeparationNone_InlineImage_LeavesExistingContentUntouched()
    {
        // BI/ID/EI with the colour space given by name, resolved from /ColorSpace << /Cs0 … >>.
        const string content = RedRect +
                               "q 200 0 0 200 100 400 cm BI /W 2 /H 2 /CS /Cs0 /BPC 8 ID " +
                               "\u00FF\u00FF\u00FF\u00FF EI Q";

        byte[] pdf = ColourConformancePage.Build(
            "[/Separation /None /DeviceRGB " + ToBlack + "]", content);

        ColourConformancePage.AssertRedRectUntouched(pdf, "/None inline image");
    }

    [Fact]
    public void SeparationNone_StencilMask_LeavesExistingContentUntouched()
    {
        // /ImageMask true: 2x2 1-bit stencil, all samples 0 = paint (default /Decode [0 1]). It paints
        // with the FILL colour, which is the /None Separation set by `/Cs0 cs 1 scn`.
        const string mask = "<< /Type /XObject /Subtype /Image /Width 2 /Height 2 " +
                            "/ImageMask true /BitsPerComponent 1 /Length 2 >>\r\nstream\r\n" +
                            "\u0000\u0000\r\nendstream";
        const string content = RedRect + "/Cs0 cs 1 scn q 200 0 0 200 100 400 cm /Im0 Do Q";

        byte[] pdf = ColourConformancePage.Build(
            "[/Separation /None /DeviceRGB " + ToBlack + "]", content, withFont: false,
            extraResources: " /XObject << /Im0 5 0 R >>", extraObjects: mask);

        ColourConformancePage.AssertRedRectUntouched(pdf, "/None stencil mask");
    }

    [Fact]
    public void SeparationNone_Shading_LeavesExistingContentUntouched()
    {
        // Axial shading whose /ColorSpace is the /None Separation; its /Function emits the tint, which
        // the space's transform would ramp to black. `sh` paints the whole clip, i.e. the whole page.
        const string sh = "<< /ShadingType 2 /ColorSpace [/Separation /None /DeviceRGB " + ToBlack + "] " +
                          "/Coords [100 400 300 400] /Extend [true true] " +
                          "/Function << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> >>";
        const string content = RedRect + "/Sh0 sh";

        byte[] pdf = ColourConformancePage.Build("/DeviceRGB", content, withFont: false,
            extraResources: " /Shading << /Sh0 5 0 R >>", extraObjects: sh);

        ColourConformancePage.AssertRedRectUntouched(pdf, "/None shading");
    }
}
