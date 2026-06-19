using System;
using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;
using FontParser.Subsetting.Cff;
using FontParser.Tables.Cff;
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

    [Theory]
    [InlineData(0)] [InlineData(107)] [InlineData(-107)] [InlineData(108)] [InlineData(-108)]
    [InlineData(1131)] [InlineData(-1131)] [InlineData(1132)] [InlineData(-1132)]
    [InlineData(32767)] [InlineData(-32768)] [InlineData(32768)] [InlineData(-32769)]
    [InlineData(1234567)] [InlineData(-1234567)]
    public void EncodeInteger_RoundTripsThroughCalc(int value)
    {
        byte[] enc = CffWriter.EncodeInteger(value);
        var idx = 0;
        int dec = Calc.Integer(enc, ref idx);
        Assert.Equal(value, dec);
        Assert.Equal(enc.Length, idx); // consumed exactly
    }

    [Fact]
    public void EncodeFixedOffset_IsFiveBytes_AndDecodes()
    {
        byte[] enc = CffWriter.EncodeFixedOffset(70000);
        Assert.Equal(5, enc.Length);
        var idx = 0;
        Assert.Equal(70000, Calc.Integer(enc, ref idx));
    }

    [Theory]
    [InlineData(0.001)] [InlineData(-0.5)] [InlineData(123.456)] [InlineData(0.0)]
    public void EncodeReal_RoundTripsThroughCalc(double value)
    {
        byte[] enc = CffWriter.EncodeReal(value);
        Assert.Equal(0x1E, enc[0]);
        var idx = 0;
        double dec = Calc.Double(enc, ref idx);
        Assert.Equal(value, dec, precision: 3);
    }

    [Fact]
    public void DictBuilder_RoundTripsTopDictOperators()
    {
        var b = new CffDictBuilder();
        b.Add(0x0011, 12345);     // CharStrings offset (Number)
        b.Add(0x0012, 40, 9999);  // Private: size, offset (NumberNumber)

        List<CffDictEntry> dest = DecodeTopDict(b.Build());

        Assert.Equal(12345, Convert.ToInt32(dest.Single(e => e.Name == "CharStrings").Operand));
        var priv = (List<double>)dest.Single(e => e.Name == "Private").Operand;
        Assert.Equal(40, (int)priv[0]);
        Assert.Equal(9999, (int)priv[1]);
    }

    [Fact]
    public void DictBuilder_OffsetPlaceholder_BackfillsAndDecodes()
    {
        var b = new CffDictBuilder();
        int pos = b.AddOffset(0x0011); // CharStrings, fixed-width offset
        b.PatchOffset(pos, 0x12345);

        List<CffDictEntry> dest = DecodeTopDict(b.Build());

        Assert.Equal(0x12345, Convert.ToInt32(dest.Single(e => e.Name == "CharStrings").Operand));
    }

    private static List<CffDictEntry> DecodeTopDict(byte[] bytes)
    {
        var src = new Type1TopDictOperatorEntries(new Dictionary<ushort, CffDictEntry?>());
        var dest = new List<CffDictEntry>();
        DictEntryReader.Read(bytes.ToList(), src, dest);
        return dest;
    }
}
