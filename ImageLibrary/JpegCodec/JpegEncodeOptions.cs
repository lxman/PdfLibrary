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

    /// <summary>
    /// Chroma subsampling mode. Only meaningful for 3-component (RGB/YCbCr)
    /// images. Defaults to <see cref="ChromaSubsampling.Yuv444"/> so all
    /// existing callers are unaffected.
    /// </summary>
    public ChromaSubsampling ChromaSubsampling { get; init; } = ChromaSubsampling.Yuv444;
}
