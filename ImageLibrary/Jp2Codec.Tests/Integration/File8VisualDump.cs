using System.IO;
using CoreJ2K;
using CoreJ2K.j2k.util;
using CoreJ2K.Util;
using Jp2Codec;
using Jp2Codec.Color;

namespace Jp2Codec.Tests.Integration;

/// <summary>
/// Writes BMP renderings of every reference test image to a <c>visual/</c>
/// directory under the test output, so each pair can be opened side by side
/// in Windows Photos / Explorer preview. One subfolder per test image with:
///   reference.bmp — CSJ2K default decode (the "expected viewer output";
///                   for file8 this includes the ICC tone curve).
///   ours.bmp      — Jp2Codec output (raw decoded samples, no ICC).
/// file8 also gets reference_no_icc.bmp (CSJ2K with nocolorspace=on); this
/// must be bit-identical to ours.bmp.
///
/// BMP keeps things simple — no extra dependency, opens natively on Windows
/// in Photos, Paint, browsers (via &lt;img&gt;), and any image viewer.
/// </summary>
public class File8VisualDump
{
    private static readonly bool Run = true;

    // (file, folder, hasIccOrSyccProfile). hasIccOrSyccProfile=true → also emit
    // reference_no_icc.bmp (CSJ2K with nocolorspace=on) which should be
    // bit-identical to ours.bmp.
    private static readonly (string File, string Folder, bool HasColorProfile)[] TestImages =
    {
        ("test_8x8.jp2",       "test_8x8",       false),
        ("test_16x16.jp2",     "test_16x16",     false),
        ("file1.jp2",          "file1",          false),
        ("file2.jp2",          "file2",          true),   // sYCC colorspace
        ("file3.jp2",          "file3",          true),   // sYCC + subsampling (4:2:0)
        ("file4.jp2",          "file4",          false),
        ("file5.jp2",          "file5",          false),
        ("file6.jp2",          "file6",          false),  // 12-bit grayscale
        ("file7.jp2",          "file7",          true),   // 16-bit RGB with colorspace transform
        ("file8.jp2",          "file8",          true),   // monochrome ICC profile
        ("file9.jp2",          "file9",          false),
        ("subsampling_1.jp2",  "subsampling_1",  false),  // 9/7 no MCT, 4:2:0
        ("subsampling_2.jp2",  "subsampling_2",  false),  // 9/7 + ICT, 2×2 subsampled
    };

    [Fact]
    public void DumpReferenceImagesAsBmp()
    {
        if (!Run) return;

        string visualRoot = "C:/Users/jorda/RiderProjects/ImageLibraries/Jp2Codec.Tests/bin/Debug/net10.0/visual";
        Directory.CreateDirectory(visualRoot);

        foreach ((string fileName, string folderName, bool hasColorProfile) in TestImages)
        {
            string dir = Path.Combine(visualRoot, folderName);
            Directory.CreateDirectory(dir);

            byte[] bytes = File.ReadAllBytes(Path.Combine("TestData", fileName));

            // Our decoder.
            Jp2DecodeResult ours = new Jp2StreamDecoder().Decode(bytes);

            // Subsampled images (chroma half-res, etc.) need ChromaUpsampler
            // to bring every component back up to the image reference grid
            // before BMP output, which expects all channels at the same
            // resolution.
            bool uniformComponents = ComponentsAreUniform(ours);
            int[][] componentData = uniformComponents
                ? ours.ComponentArrays
                : ChromaUpsampler.UpsampleAll(ours);

            BmpWriter.Save(Path.Combine(dir, "ours.bmp"),
                ours.Width, ours.Height, ours.NumberOfComponents, componentData,
                ours.ComponentPrecisionArray);

            // Run our raw output through Unicolour-based colour management
            // so JP2-wrapper-declared colorspaces (sYCC, ICC) render naturally.
            // SrgbRenderer handles upsampling internally when needed.
            byte[] srgb = SrgbRenderer.RenderToSrgb(ours);
            BmpWriter.SaveSrgb(Path.Combine(dir, "displayable.bmp"),
                ours.Width, ours.Height, srgb);

            // CSJ2K with default settings — what a normal viewer would produce.
            // CSJ2K's Resampler upsamples chroma so the resulting image is
            // always at full uniform resolution.
            {
                using var ms = new MemoryStream(bytes);
                PortableImage img = J2kImage.FromStream(ms);
                var data = new int[img.NumberOfComponents][];
                for (var c = 0; c < img.NumberOfComponents; c++)
                    data[c] = img.GetComponent(c);
                BmpWriter.Save(Path.Combine(dir, "reference.bmp"),
                    img.Width, img.Height, img.NumberOfComponents, data,
                    ours.ComponentPrecisionArray);
            }

            // Files with embedded ICC/sYCC profiles AND uniform component
            // dimensions: capture the raw (no-colorspace-mapping) reference
            // so it can be compared against ours.bmp byte-by-byte. CSJ2K's
            // nocolorspace path crashes on subsampled images, so skip that
            // for file3-style 4:2:0.
            if (hasColorProfile && uniformComponents)
            {
                using var ms = new MemoryStream(bytes);
                ParameterList pl = new ParameterList(J2kImage.GetDefaultDecoderParameterList());
                pl["nocolorspace"] = "on";
                PortableImage img = J2kImage.FromStream(ms, pl);
                var data = new int[img.NumberOfComponents][];
                for (var c = 0; c < img.NumberOfComponents; c++)
                    data[c] = img.GetComponent(c);
                BmpWriter.Save(Path.Combine(dir, "reference_no_icc.bmp"),
                    img.Width, img.Height, img.NumberOfComponents, data,
                    ours.ComponentPrecisionArray);
            }
        }
    }

    private static bool ComponentsAreUniform(Jp2DecodeResult r)
    {
        for (var c = 0; c < r.NumberOfComponents; c++)
        {
            if (r.ComponentWidth[c] != r.Width) return false;
            if (r.ComponentHeight[c] != r.Height) return false;
        }
        return true;
    }
}

/// <summary>
/// Minimal Windows BMP encoder. Supports 8-bit grayscale (1 component) and
/// 24-bit RGB (3 components). Rows are stored bottom-to-top per BMP
/// convention, padded to a 4-byte boundary.
/// </summary>
internal static class BmpWriter
{
    public static void Save(string path, int width, int height, int numComps, int[][] componentData, int[]? componentPrecision = null)
    {
        if (numComps == 1)
        {
            int prec = componentPrecision?[0] ?? 8;
            SaveGrayscale(path, width, height, componentData[0], prec);
        }
        else if (numComps == 3)
        {
            int p0 = componentPrecision?[0] ?? 8;
            int p1 = componentPrecision?[1] ?? 8;
            int p2 = componentPrecision?[2] ?? 8;
            SaveRgb(path, width, height, componentData[0], componentData[1], componentData[2], p0, p1, p2);
        }
        else
        {
            throw new System.NotSupportedException($"BMP output: unsupported component count {numComps}.");
        }
    }

    /// <summary>
    /// Save a tightly-packed interleaved sRGB byte raster (length = width·height·3,
    /// R, G, B per pixel, row-major top-to-bottom) as a 24-bit BMP.
    /// </summary>
    public static void SaveSrgb(string path, int width, int height, byte[] srgb)
    {
        int rowSize = (width * 3 + 3) & ~3;
        int pixelDataSize = rowSize * height;
        int pixelDataOffset = 14 + 40;
        int fileSize = pixelDataOffset + pixelDataSize;

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        w.Write((byte)'B'); w.Write((byte)'M');
        w.Write(fileSize);
        w.Write(0);
        w.Write(pixelDataOffset);

        w.Write(40);
        w.Write(width);
        w.Write(height);
        w.Write((short)1);
        w.Write((short)24);
        w.Write(0);
        w.Write(pixelDataSize);
        w.Write(2835);
        w.Write(2835);
        w.Write(0);
        w.Write(0);

        var rowBuf = new byte[rowSize];
        for (var y = height - 1; y >= 0; y--)
        {
            int srcBase = y * width * 3;
            for (var x = 0; x < width; x++)
            {
                int si = srcBase + x * 3;
                int di = x * 3;
                // sRGB byte raster is R,G,B; BMP wants B,G,R.
                rowBuf[di]     = srgb[si + 2];
                rowBuf[di + 1] = srgb[si + 1];
                rowBuf[di + 2] = srgb[si];
            }
            for (var x = width * 3; x < rowSize; x++) rowBuf[x] = 0;
            w.Write(rowBuf);
        }
    }

    private static void SaveGrayscale(string path, int width, int height, int[] samples, int precision = 8)
    {
        int rowSize = (width + 3) & ~3; // padded to multiple of 4
        int pixelDataSize = rowSize * height;
        int paletteSize = 256 * 4;
        int fileHeaderSize = 14;
        int dibHeaderSize = 40;
        int pixelDataOffset = fileHeaderSize + dibHeaderSize + paletteSize;
        int fileSize = pixelDataOffset + pixelDataSize;

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        // BMP file header.
        w.Write((byte)'B'); w.Write((byte)'M');
        w.Write(fileSize);
        w.Write(0);                       // reserved
        w.Write(pixelDataOffset);

        // DIB header (BITMAPINFOHEADER).
        w.Write(dibHeaderSize);
        w.Write(width);
        w.Write(height);                  // positive = bottom-up
        w.Write((short)1);                // planes
        w.Write((short)8);                // bpp
        w.Write(0);                       // compression = BI_RGB
        w.Write(pixelDataSize);
        w.Write(2835);                    // x pixels per meter (~72 DPI)
        w.Write(2835);                    // y pixels per meter
        w.Write(256);                     // colors in palette
        w.Write(0);                       // important colors

        // Identity grayscale palette (BGRA per entry).
        for (var i = 0; i < 256; i++)
        {
            w.Write((byte)i); w.Write((byte)i); w.Write((byte)i); w.Write((byte)0);
        }

        // Pixel data, bottom-up.
        var rowBuf = new byte[rowSize];
        for (var y = height - 1; y >= 0; y--)
        {
            int srcBase = y * width;
            for (var x = 0; x < width; x++) rowBuf[x] = NormaliseToByte(samples[srcBase + x], precision);
            for (var x = width; x < rowSize; x++) rowBuf[x] = 0;
            w.Write(rowBuf);
        }
    }

    private static void SaveRgb(string path, int width, int height, int[] r, int[] g, int[] b, int pR = 8, int pG = 8, int pB = 8)
    {
        int rowSize = (width * 3 + 3) & ~3;
        int pixelDataSize = rowSize * height;
        int fileHeaderSize = 14;
        int dibHeaderSize = 40;
        int pixelDataOffset = fileHeaderSize + dibHeaderSize;
        int fileSize = pixelDataOffset + pixelDataSize;

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        // BMP file header.
        w.Write((byte)'B'); w.Write((byte)'M');
        w.Write(fileSize);
        w.Write(0);
        w.Write(pixelDataOffset);

        // DIB header.
        w.Write(dibHeaderSize);
        w.Write(width);
        w.Write(height);
        w.Write((short)1);
        w.Write((short)24);
        w.Write(0);
        w.Write(pixelDataSize);
        w.Write(2835);
        w.Write(2835);
        w.Write(0);
        w.Write(0);

        var rowBuf = new byte[rowSize];
        for (var y = height - 1; y >= 0; y--)
        {
            int srcBase = y * width;
            for (var x = 0; x < width; x++)
            {
                int bi = x * 3;
                rowBuf[bi    ] = NormaliseToByte(b[srcBase + x], pB);
                rowBuf[bi + 1] = NormaliseToByte(g[srcBase + x], pG);
                rowBuf[bi + 2] = NormaliseToByte(r[srcBase + x], pR);
            }
            for (var x = width * 3; x < rowSize; x++) rowBuf[x] = 0;
            w.Write(rowBuf);
        }
    }

    /// <summary>
    /// Linear-scale a sample at the supplied precision into 0..255 for BMP output.
    /// 8-bit input clamps; higher precisions divide by (2^precision - 1).
    /// </summary>
    private static byte NormaliseToByte(int sample, int precision)
    {
        if (precision == 8)
        {
            if (sample < 0) return 0;
            if (sample > 255) return 255;
            return (byte)sample;
        }
        int max = (1 << precision) - 1;
        if (sample < 0) sample = 0;
        if (sample > max) sample = max;
        return (byte)((sample * 255 + (max >> 1)) / max);
    }
}
