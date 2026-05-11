using JpegCodec.Stream;

namespace JpegCodec;

public sealed class JpegImageInfo
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int NumberOfComponents { get; init; }
    public int Precision { get; init; }
    public JpegMarker StartOfFrame { get; init; }
    public bool HasAdobeMarker { get; init; }
    public byte AdobeColorTransform { get; init; }
    public bool HasJfif { get; init; }
}
