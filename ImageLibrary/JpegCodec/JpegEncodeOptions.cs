namespace JpegCodec;

public sealed class JpegEncodeOptions
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int NumberOfComponents { get; init; }
    public int Quality { get; init; } = 90;
    public bool Progressive { get; init; }
    public bool EmitJfif { get; init; } = true;
    public bool EmitAdobeMarker { get; init; }
    public byte AdobeColorTransform { get; init; }
    public int? RestartInterval { get; init; }
}
