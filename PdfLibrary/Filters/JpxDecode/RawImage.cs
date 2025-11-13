using CoreJ2K.Util;

namespace PdfLibrary.Filters.JpxDecode;

/// <summary>
/// Simple container for raw image data from JPEG 2000 decoder
/// </summary>
internal sealed class RawImage(int width, int height, int components, byte[] bytes)
    : IImage
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Components { get; } = components;
    public byte[] Bytes { get; } = bytes;

    public T As<T>()
    {
        if (this is T result)
            return result;

        throw new InvalidOperationException($"Cannot convert RawImage to {typeof(T).Name}");
    }
}
