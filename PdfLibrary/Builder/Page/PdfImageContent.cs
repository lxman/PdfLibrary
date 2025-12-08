namespace PdfLibrary.Builder.Page;

/// <summary>
/// Image content element
/// </summary>
public class PdfImageContent : PdfContentElement
{
    public byte[] ImageData { get; set; } = [];
    public PdfRect Rect { get; set; }
    public double Opacity { get; set; } = 1.0;
    public double Rotation { get; set; }
    public bool PreserveAspectRatio { get; set; } = true;
    public PdfImageCompression Compression { get; set; } = PdfImageCompression.Auto;
    public int JpegQuality { get; set; } = 85;
    public bool Interpolate { get; set; } = true;
}