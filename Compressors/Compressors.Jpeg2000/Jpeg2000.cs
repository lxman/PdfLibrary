using Melville.CSJ2K;
using Melville.CSJ2K.Util;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Compressors.Jpeg2000;

/// <summary>
/// JPEG 2000 compression and decompression using Melville.CSJ2K library.
/// Cross-platform pure C# implementation.
/// </summary>
public static class Jpeg2000
{
    /// <summary>
    /// Decompress JPEG 2000 data to raw image bytes
    /// </summary>
    /// <param name="data">JPEG 2000 encoded data (J2K or JP2 format)</param>
    /// <param name="width">Output image width</param>
    /// <param name="height">Output image height</param>
    /// <param name="components">Number of color components (1=gray, 3=RGB, 4=RGBA)</param>
    /// <returns>Raw image bytes in component-interleaved format</returns>
    public static byte[] Decompress(byte[] data, out int width, out int height, out int components)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length == 0)
        {
            width = 0;
            height = 0;
            components = 0;
            return [];
        }

        // Decode using Melville.CSJ2K
        using var stream = new MemoryStream(data);
        PortableImage portableImage = J2kReader.FromStream(stream);

        width = portableImage.Width;
        height = portableImage.Height;
        components = portableImage.NumberOfComponents;

        // Get raw component data and convert to interleaved bytes
        var result = new byte[width * height * components];

        if (components == 1)
        {
            // Grayscale
            int[] component0 = portableImage.GetComponent(0);
            for (int i = 0; i < width * height; i++)
            {
                result[i] = ClampToByte(component0[i]);
            }
        }
        else if (components == 3)
        {
            // RGB
            int[] component0 = portableImage.GetComponent(0);
            int[] component1 = portableImage.GetComponent(1);
            int[] component2 = portableImage.GetComponent(2);

            for (int i = 0; i < width * height; i++)
            {
                result[i * 3 + 0] = ClampToByte(component0[i]);
                result[i * 3 + 1] = ClampToByte(component1[i]);
                result[i * 3 + 2] = ClampToByte(component2[i]);
            }
        }
        else if (components == 4)
        {
            // RGBA
            int[] component0 = portableImage.GetComponent(0);
            int[] component1 = portableImage.GetComponent(1);
            int[] component2 = portableImage.GetComponent(2);
            int[] component3 = portableImage.GetComponent(3);

            for (int i = 0; i < width * height; i++)
            {
                result[i * 4 + 0] = ClampToByte(component0[i]);
                result[i * 4 + 1] = ClampToByte(component1[i]);
                result[i * 4 + 2] = ClampToByte(component2[i]);
                result[i * 4 + 3] = ClampToByte(component3[i]);
            }
        }
        else
        {
            throw new NotSupportedException($"Number of components {components} not supported");
        }

        return result;
    }

    /// <summary>
    /// Decompress JPEG 2000 data directly to ImageSharp Image
    /// </summary>
    /// <param name="data">JPEG 2000 encoded data (J2K or JP2 format)</param>
    /// <returns>ImageSharp Image with decoded image data</returns>
    public static Image DecompressToImage(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length == 0)
        {
            throw new ArgumentException("Empty JP2 data", nameof(data));
        }

        // Decode using Melville.CSJ2K
        using var stream = new MemoryStream(data);
        PortableImage portableImage = J2kReader.FromStream(stream);

        int width = portableImage.Width;
        int height = portableImage.Height;
        int components = portableImage.NumberOfComponents;

        return components switch
        {
            1 => ConvertGrayscaleImage(portableImage, width, height),
            3 => ConvertRgbImage(portableImage, width, height),
            4 => ConvertRgbaImage(portableImage, width, height),
            _ => throw new NotSupportedException($"Number of components {components} not supported")
        };
    }

    private static Image<L8> ConvertGrayscaleImage(PortableImage portableImage, int width, int height)
    {
        var image = new Image<L8>(width, height);
        int[] component0 = portableImage.GetComponent(0);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                image[x, y] = new L8(ClampToByte(component0[idx]));
            }
        }

        return image;
    }

    private static Image<Rgb24> ConvertRgbImage(PortableImage portableImage, int width, int height)
    {
        var image = new Image<Rgb24>(width, height);
        int[] component0 = portableImage.GetComponent(0);
        int[] component1 = portableImage.GetComponent(1);
        int[] component2 = portableImage.GetComponent(2);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                image[x, y] = new Rgb24(
                    ClampToByte(component0[idx]),
                    ClampToByte(component1[idx]),
                    ClampToByte(component2[idx])
                );
            }
        }

        return image;
    }

    private static Image<Rgba32> ConvertRgbaImage(PortableImage portableImage, int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        int[] component0 = portableImage.GetComponent(0);
        int[] component1 = portableImage.GetComponent(1);
        int[] component2 = portableImage.GetComponent(2);
        int[] component3 = portableImage.GetComponent(3);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                image[x, y] = new Rgba32(
                    ClampToByte(component0[idx]),
                    ClampToByte(component1[idx]),
                    ClampToByte(component2[idx]),
                    ClampToByte(component3[idx])
                );
            }
        }

        return image;
    }

    private static byte ClampToByte(int value)
    {
        if (value < 0) return 0;
        if (value > 255) return 255;
        return (byte)value;
    }
}
