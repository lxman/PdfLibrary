using System;

namespace JpegCodec;

public sealed class JpegDecodeResult
{
    public byte[] ComponentData { get; init; } = [];
    public int Width { get; init; }
    public int Height { get; init; }
    public int NumberOfComponents { get; init; }
    public int Precision { get; init; }
    public bool HasAdobeMarker { get; init; }
    public byte AdobeColorTransform { get; init; }
}
