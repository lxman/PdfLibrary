using Jbig2Decoder.Image;

namespace Jbig2Decoder.Tests;

/// <summary>
/// Region and page dimensions in JBIG2 come straight from untrusted segment headers (cast from
/// uint). All region/page bitmaps funnel through the <see cref="Bitmap"/> constructor, so the
/// allocation-size guard lives there: a hostile width/height must throw cleanly rather than overflow
/// the byte[] length (Stride*Height in int) or exhaust memory.
/// </summary>
public class BitmapGuardTests
{
    [Fact]
    public void Oversize_dimensions_throw_instead_of_overflowing()
    {
        // Stride = (100000+7)/8 = 12500; ×200000 = 2.5e9 bytes — overflows int in the old
        // Stride*height arithmetic and exceeds the size cap. Must surface as a clean argument error.
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bitmap(100_000, 200_000));
    }

    [Fact]
    public void Reasonable_dimensions_still_allocate()
    {
        var bmp = new Bitmap(1000, 1000);
        Assert.Equal(1000, bmp.Width);
        Assert.Equal(1000, bmp.Height);
        Assert.Equal(125 * 1000, bmp.Data.Length); // stride 125 × 1000 rows
    }
}
