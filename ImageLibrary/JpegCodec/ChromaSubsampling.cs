namespace JpegCodec;

/// <summary>
/// Chroma subsampling mode for JPEG encoding of 3-component (YCbCr) images.
/// </summary>
public enum ChromaSubsampling
{
    /// <summary>
    /// No subsampling: luma and chroma at full resolution (H=1/V=1 for all
    /// components). Default — backward-compatible with the v1 encoder.
    /// </summary>
    Yuv444 = 0,

    /// <summary>
    /// 4:2:0 subsampling: luma at H=2/V=2, chroma (Cb, Cr) at H=1/V=1.
    /// Reduces chroma data to 1/4, typically yielding 20–35% smaller files
    /// at equivalent quality settings. The input pixel buffer is expected in
    /// interleaved RGB order; the encoder performs the RGB→YCbCr conversion
    /// internally.
    /// </summary>
    Yuv420 = 1,
}
