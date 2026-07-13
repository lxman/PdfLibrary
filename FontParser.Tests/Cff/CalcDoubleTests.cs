using FontParser.Tables.Cff.Type1;
using Xunit;

namespace FontParser.Tests;

/// <summary>
/// Decoding of CFF DICT real operands (0x1E ... 0xF) per Adobe TN#5176 §5 Table 5.
/// Nibbles: 0–9 digit, 0xa '.', 0xb 'E' (positive exponent), 0xc 'E-' (negative exponent),
/// 0xe '-', 0xf end. The E / E- exponent nibbles were previously ignored, so any FontMatrix
/// scale written in scientific notation (as real fonts do, e.g. LinLibertine) decoded ~10^n too
/// large — collapsing UnitsPerEm to 0 and blanking the text.
/// </summary>
public class CalcDoubleTests
{
    [Fact]
    public void Double_DecodesNegativeReal_NoExponent()
    {
        // TN#5176 example: -2.25 => 1e e2 a2 5f
        byte[] data = [0x1e, 0xe2, 0xa2, 0x5f];
        var index = 0;
        Assert.Equal(-2.25, Calc.Double(data, ref index), 10);
    }

    [Fact]
    public void Double_DecodesRealWithNegativeExponent()
    {
        // TN#5176 example: 0.140541E-3 => 1e 0a 14 05 41 c3 ff  == 0.000140541
        byte[] data = [0x1e, 0x0a, 0x14, 0x05, 0x41, 0xc3, 0xff];
        var index = 0;
        Assert.Equal(0.000140541, Calc.Double(data, ref index), 12);
    }

    [Fact]
    public void Double_DecodesRealWithPositiveExponent()
    {
        // 2.5E4 => nibbles 2 a 5 b 4 f => 1e 2a 5b 4f  == 25000
        byte[] data = [0x1e, 0x2a, 0x5b, 0x4f];
        var index = 0;
        Assert.Equal(25000.0, Calc.Double(data, ref index), 6);
    }

    [Fact]
    public void Double_DecodesTypicalFontMatrixScale_WithNegativeExponent()
    {
        // 1.004E-3 => nibbles 1 a 0 0 4 c 3 f => 1e 1a 00 4c 3f  == 0.001004
        // (1/0.001004 ~= 996 UnitsPerEm — a real, non-zero em square.)
        byte[] data = [0x1e, 0x1a, 0x00, 0x4c, 0x3f];
        var index = 0;
        Assert.Equal(0.001004, Calc.Double(data, ref index), 9);
    }
}
