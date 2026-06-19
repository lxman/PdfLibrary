using System;
using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;
using FontParser.Subsetting.Cff;
using FontParser.Tables.Cff.Type1;
using Xunit;

namespace FontParser.Tests;

/// <summary>
/// Round-trips the CFF wire-format encoders through the existing reader to prove byte-exactness.
/// </summary>
public class CffWriterTests
{
    [Fact]
    public void WriteIndex_RoundTripsThroughType1Index()
    {
        var entries = new List<byte[]> { new byte[] { 1, 2, 3 }, Array.Empty<byte>(), new byte[] { 9 } };

        byte[] bytes = CffWriter.WriteIndex(entries);

        using var reader = new BigEndianReader(bytes);
        var idx = new Type1Index(reader);
        Assert.Equal(3, idx.Data.Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, idx.Data[0].ToArray());
        Assert.Empty(idx.Data[1]);
        Assert.Equal(new byte[] { 9 }, idx.Data[2].ToArray());
        Assert.Equal(bytes.Length, (int)reader.Position); // consumed exactly
    }

    [Fact]
    public void WriteIndex_Empty_IsTwoZeroBytes()
    {
        Assert.Equal(new byte[] { 0, 0 }, CffWriter.WriteIndex(new List<byte[]>()));
    }

    [Fact]
    public void WriteIndex_LargeEntries_PicksOffSizeAndRoundTrips()
    {
        // Total data length > 255 forces offSize > 1.
        byte[] big = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
        var entries = new List<byte[]> { big, new byte[] { 7, 7 } };

        byte[] bytes = CffWriter.WriteIndex(entries);

        using var reader = new BigEndianReader(bytes);
        var idx = new Type1Index(reader);
        Assert.Equal(big, idx.Data[0].ToArray());
        Assert.Equal(new byte[] { 7, 7 }, idx.Data[1].ToArray());
    }
}
