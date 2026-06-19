using ICCSharp.Profile;
using ICCSharp.Tags;

namespace ICCSharp.Tests.Tags;

public class CurveTagTests
{
    private static byte[] WithHeader(string typeSig, params byte[] payload)
    {
        var buf = new byte[8 + payload.Length];
        for (var i = 0; i < 4; i++) buf[i] = (byte)typeSig[i];
        Buffer.BlockCopy(payload, 0, buf, 8, payload.Length);
        return buf;
    }

    private static byte[] U32Be(uint v) => new[]
    {
        (byte)((v >> 24) & 0xFF), (byte)((v >> 16) & 0xFF),
        (byte)((v >> 8) & 0xFF),  (byte)(v & 0xFF),
    };

    private static byte[] U16Be(ushort v) => new[] { (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF) };

    // --- curveType --------------------------------------------------------

    [Fact]
    public void Curve_identity_when_count_zero()
    {
        var c = Assert.IsType<CurveTagElement>(
            TagElementReader.Parse(WithHeader("curv", U32Be(0))));
        Assert.True(c.IsIdentity);
        Assert.False(c.IsSingleGamma);
        Assert.Empty(c.Samples);
    }

    [Fact]
    public void Curve_single_gamma_when_count_one()
    {
        // gamma 2.2 encoded as u8Fixed8 = 2.2 * 256 = 563.2 → 0x0233
        var c = Assert.IsType<CurveTagElement>(
            TagElementReader.Parse(WithHeader("curv", [..U32Be(1), ..U16Be(0x0233)])));
        Assert.True(c.IsSingleGamma);
        Assert.False(c.IsIdentity);
        Assert.Equal(2.19921875, c.SingleGamma, 6); // 0x0233 / 256 = 2.19921875
    }

    [Fact]
    public void Curve_sampled_returns_all_samples_in_order()
    {
        byte[] payload =
        [
            ..U32Be(4),
            ..U16Be(0x0000),
            ..U16Be(0x5555),
            ..U16Be(0xAAAA),
            ..U16Be(0xFFFF),
        ];
        var c = Assert.IsType<CurveTagElement>(
            TagElementReader.Parse(WithHeader("curv", payload)));
        Assert.Equal(4, c.Samples.Count);
        Assert.Equal(0x0000, c.Samples[0]);
        Assert.Equal(0x5555, c.Samples[1]);
        Assert.Equal(0xAAAA, c.Samples[2]);
        Assert.Equal(0xFFFF, c.Samples[3]);
        Assert.False(c.IsIdentity);
        Assert.False(c.IsSingleGamma);
    }

    [Fact]
    public void Curve_count_exceeding_payload_throws()
    {
        byte[] payload = [..U32Be(100), ..U16Be(0)];
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("curv", payload)));
    }

    [Fact]
    public void Curve_payload_under_four_bytes_throws()
    {
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("curv", new byte[3])));
    }

    // --- parametricCurveType ----------------------------------------------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 3)]
    [InlineData(2, 4)]
    [InlineData(3, 5)]
    [InlineData(4, 7)]
    public void RequiredParameterCount_matches_spec(ushort fnType, int expected)
    {
        Assert.Equal(expected, ParametricCurveTagElement.RequiredParameterCount(fnType));
    }

    [Fact]
    public void Para_type0_reads_single_gamma_parameter()
    {
        // y = x^g, g = 2.4 → s15Fixed16 = 0x00026666
        byte[] payload =
        [
            ..U16Be(0), ..U16Be(0),       // function type 0 + reserved
            ..U32Be(0x00026666),          // g = 2.4
        ];
        var p = Assert.IsType<ParametricCurveTagElement>(
            TagElementReader.Parse(WithHeader("para", payload)));
        Assert.Equal(0, p.FunctionType);
        Assert.Single(p.Parameters);
        Assert.Equal(2.4, p.Parameters[0], 4);
    }

    [Fact]
    public void Para_type3_reads_five_parameters_in_spec_order()
    {
        // sRGB parametric: g=2.4 a=1/1.055 b=0.055/1.055 c=1/12.92 d=0.04045
        // Encoded as s15Fixed16. Values rounded.
        byte[] payload =
        [
            ..U16Be(3), ..U16Be(0),
            ..U32Be(0x00026666),  // g  = 2.4
            ..U32Be(0x0000F2A7),  // a  ≈ 0.9479
            ..U32Be(0x00000D59),  // b  ≈ 0.0521
            ..U32Be(0x0000139A),  // c  ≈ 0.0774
            ..U32Be(0x00000A5B),  // d  ≈ 0.04045
        ];
        var p = Assert.IsType<ParametricCurveTagElement>(
            TagElementReader.Parse(WithHeader("para", payload)));
        Assert.Equal(3, p.FunctionType);
        Assert.Equal(5, p.Parameters.Count);
        Assert.Equal(2.4, p.Parameters[0], 4);
        Assert.Equal(0.04045, p.Parameters[4], 3);
    }

    [Fact]
    public void Para_unknown_function_type_throws()
    {
        byte[] payload = [..U16Be(99), ..U16Be(0), ..U32Be(0)];
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("para", payload)));
    }

    [Fact]
    public void Para_payload_truncated_for_declared_function_throws()
    {
        // function type 4 needs 7 params (28 bytes), supply only 8 bytes of params.
        byte[] payload = [..U16Be(4), ..U16Be(0), ..new byte[8]];
        Assert.Throws<IccParseException>(() => TagElementReader.Parse(WithHeader("para", payload)));
    }
}
