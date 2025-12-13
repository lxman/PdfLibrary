using CoreJ2K.Util;

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
        PortableImage portableImage = CoreJ2K.J2kImage.FromStream(stream);

        width = portableImage.Width;
        height = portableImage.Height;
        components = portableImage.NumberOfComponents;

        // Get raw component data and convert to interleaved bytes
        var result = new byte[width * height * components];

        switch (components)
        {
            case 1:
            {
                // Grayscale
                int[] component0 = portableImage.GetComponent(0);
                for (var i = 0; i < width * height; i++)
                {
                    result[i] = ClampToByte(component0[i]);
                }

                break;
            }
            case 3:
            {
                // RGB
                int[] component0 = portableImage.GetComponent(0);
                int[] component1 = portableImage.GetComponent(1);
                int[] component2 = portableImage.GetComponent(2);

                for (var i = 0; i < width * height; i++)
                {
                    result[i * 3 + 0] = ClampToByte(component0[i]);
                    result[i * 3 + 1] = ClampToByte(component1[i]);
                    result[i * 3 + 2] = ClampToByte(component2[i]);
                }

                break;
            }
            case 4:
            {
                // RGBA
                int[] component0 = portableImage.GetComponent(0);
                int[] component1 = portableImage.GetComponent(1);
                int[] component2 = portableImage.GetComponent(2);
                int[] component3 = portableImage.GetComponent(3);

                for (var i = 0; i < width * height; i++)
                {
                    result[i * 4 + 0] = ClampToByte(component0[i]);
                    result[i * 4 + 1] = ClampToByte(component1[i]);
                    result[i * 4 + 2] = ClampToByte(component2[i]);
                    result[i * 4 + 3] = ClampToByte(component3[i]);
                }

                break;
            }
            default:
                throw new NotSupportedException($"Number of components {components} not supported");
        }

        return result;
    }


    private static byte ClampToByte(int value)
    {
        return value switch
        {
            < 0 => 0,
            > 255 => 255,
            _ => (byte)value
        };
    }
}
