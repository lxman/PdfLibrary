using CoreJ2K.j2k.image;
using CoreJ2K.Util;

namespace PdfLibrary.Filters.JpxDecode;

/// <summary>
/// Image creator for JPEG 2000 decoder that produces raw byte arrays
/// </summary>
internal sealed class RawImageCreator : IImageCreator
{
    private static readonly RawImageCreator Instance = new();

    public bool IsDefault => false;

    public IImage Create(int width, int height, int numComponents, byte[] bytes)
    {
        return new RawImage(width, height, numComponents, bytes);
    }

    public BlkImgDataSrc ToPortableImageSource(object imageObject)
    {
        throw new NotSupportedException("Converting to PortableImageSource is not supported.");
    }

    public static void Register()
    {
        ImageFactory.Register(Instance);
    }
}
