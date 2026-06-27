using System.Reflection;
using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Tests.Region;

/// <summary>
/// Differential tests for <c>GenericRegionDecoder</c> against fixtures dumped
/// from the patched jbig2dec (see oracle-notes/dump_generic.h). Each fixture
/// is one generic-region segment from a corpus .jb2 file: it carries the params,
/// the raw arithmetic-coded byte buffer, and the expected output bitmap.
///
/// Bitmap comparison is bit-exact. On mismatch, the test reports the row/byte
/// of the first divergence so failures localise immediately.
/// </summary>
public class GenericRegionDecoderTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "GenericRegion", name);

    private readonly ITestOutputHelper _out;
    public GenericRegionDecoderTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Template0_DefaultAt_042_1()
    {
        RunFixture("042_1.bin");
    }

    [Fact]
    public void Template0_CustomAt_042_7()
    {
        RunFixture("042_7.bin");
    }

    [Fact]
    public void Template1_042_4() => RunFixture("042_4.bin");

    [Fact]
    public void Template2_042_5() => RunFixture("042_5.bin");

    [Fact]
    public void Template3_042_6() => RunFixture("042_6.bin");

    [Fact]
    public void Template0_TpgdOn_042_8() => RunFixture("042_8.bin");

    private void RunFixture(string fixtureName)
    {
        GenericRegionFixture fx = GenericRegionFixture.Load(FixturePath(fixtureName));
        Assert.False(fx.Mmr, "fixture should be arithmetic-coded");
        Assert.False(fx.UseSkip, "skip not yet supported");

        Assembly asm = typeof(MqDecoder).Assembly;
        Type mqType = asm.GetType("Jbig2Decoder.Mq.MqDecoder", throwOnError: true)!;
        ConstructorInfo mqCtor = mqType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.GetParameters().Length == 3);
        object mq = mqCtor.Invoke([fx.ArithBytes, 0, fx.ArithBytes.Length]);

        Type bmpType    = asm.GetType("Jbig2Decoder.Image.Bitmap", throwOnError: true)!;
        ConstructorInfo bmpCtor    = bmpType.GetConstructors().Single(c => c.GetParameters().Length == 2);
        object output  = bmpCtor.Invoke([fx.Width, fx.Height]);

        Type paramsType = asm.GetType("Jbig2Decoder.Region.GenericRegionParams", throwOnError: true)!;
        object pBox = Activator.CreateInstance(paramsType)!;
        paramsType.GetField("GbTemplate")!.SetValue(pBox, fx.GbTemplate);
        paramsType.GetField("TpgdOn")!.SetValue(pBox, fx.TpgdOn);
        paramsType.GetField("UseSkip")!.SetValue(pBox, fx.UseSkip);
        paramsType.GetField("Gbat")!.SetValue(pBox, fx.Gbat);

        Type decType = asm.GetType("Jbig2Decoder.Region.GenericRegionDecoder", throwOnError: true)!;
        object decoder = Activator.CreateInstance(decType)!;

        var statsSize = (int)decType.GetMethod("StatsSizeFor", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [fx.GbTemplate])!;
        var gbStats = new byte[statsSize];

        MethodInfo decode = decType.GetMethod("Decode", BindingFlags.Public | BindingFlags.Instance)!;
        decode.Invoke(decoder, [pBox, mq, gbStats, output]);

        var actual = (byte[])bmpType.GetProperty("Data")!.GetValue(output)!;
        Assert.Equal(fx.BitmapStride, (int)bmpType.GetProperty("Stride")!.GetValue(output)!);

        for (var i = 0; i < fx.BitmapBytes.Length; i++)
        {
            if (actual[i] == fx.BitmapBytes[i]) continue;
            int row = i / fx.BitmapStride;
            int col = i % fx.BitmapStride;
            _out.WriteLine($"First divergence at byte {i} (row {row}, byte-in-row {col}, x≈{col * 8}-{col * 8 + 7})");
            _out.WriteLine($"  expected: 0x{fx.BitmapBytes[i]:X2}");
            _out.WriteLine($"  actual:   0x{actual[i]:X2}");

            int prevRowBase = row - 1 >= 0 ? (row - 1) * fx.BitmapStride : -1;
            if (prevRowBase >= 0)
                _out.WriteLine($"  prev row at same byte: 0x{fx.BitmapBytes[prevRowBase + col]:X2} (matches expected? {fx.BitmapBytes[prevRowBase + col] == actual[prevRowBase + col]})");
            Assert.Fail($"GenericRegionDecoder diverges from jbig2dec at byte {i} of fixture {fixtureName}");
        }
    }
}
