using System;
using Xunit;

namespace CcittCodec.Tests;

/// <summary>
/// The /Columns (Width) parameter of a CCITTFaxDecode stream is fully attacker-controlled. A
/// non-positive value makes the per-row buffer zero/negative; an enormous value overflows the row
/// allocation. The decoder must reject these up front rather than crash or exhaust memory.
/// </summary>
public class CcittDimensionGuardTests
{
    [Fact]
    public void Decode_rejects_nonpositive_columns()
    {
        var decoder = new CcittDecoder(new CcittOptions { Width = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => decoder.Decode([0x00, 0x01]));
    }

    [Fact]
    public void Decode_rejects_absurd_columns()
    {
        var decoder = new CcittDecoder(new CcittOptions { Width = 5_000_000 });
        Assert.Throws<ArgumentOutOfRangeException>(() => decoder.Decode([0x00, 0x01]));
    }

    [Fact]
    public void Decode_rejects_oversize_image_when_rows_known()
    {
        // 200000 columns × 200000 rows of packed rows far exceeds the size cap.
        var decoder = new CcittDecoder(new CcittOptions { Width = 200_000, Height = 200_000 });
        Assert.Throws<ArgumentOutOfRangeException>(() => decoder.Decode([0x00, 0x01]));
    }
}
