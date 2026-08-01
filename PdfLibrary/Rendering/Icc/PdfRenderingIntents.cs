using ICCSharp.Profile;

namespace PdfLibrary.Rendering.Icc;

/// <summary>
/// Maps a PDF rendering-intent name (ri operator / ExtGState /RI / image /Intent —
/// ISO 32000-2 §8.6.5.8) to the ICC intent. Unknown or absent → relative colorimetric
/// (the PDF and ICC default).
/// </summary>
internal static class PdfRenderingIntents
{
    public static RenderingIntent Map(string? pdfIntent) => pdfIntent switch
    {
        "Perceptual" => RenderingIntent.Perceptual,
        "Saturation" => RenderingIntent.Saturation,
        "AbsoluteColorimetric" => RenderingIntent.AbsoluteColorimetric,
        _ => RenderingIntent.RelativeColorimetric,
    };
}
