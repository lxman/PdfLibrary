using System.Reflection;
using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Tests.Region;

public class RefinementRegionDecoderTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Refinement", name);

    private readonly ITestOutputHelper _out;
    public RefinementRegionDecoderTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Template0_DefaultGrat_042_21() => RunFixture("042_21.bin");

    private void RunFixture(string fixtureName)
    {
        RefinementRegionFixture fx = RefinementRegionFixture.Load(FixturePath(fixtureName));

        Assembly asm = typeof(MqDecoder).Assembly;
        Type bmpType = asm.GetType("Jbig2Decoder.Image.Bitmap", throwOnError: true)!;
        ConstructorInfo bmpFromBytes = bmpType.GetConstructors().Single(c => c.GetParameters().Length == 3);
        object refBmp = bmpFromBytes.Invoke([fx.RefWidth, fx.RefHeight, fx.RefBytes]);

        Type mqType = asm.GetType("Jbig2Decoder.Mq.MqDecoder", throwOnError: true)!;
        ConstructorInfo mqCtor = mqType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.GetParameters().Length == 3);
        object mq = mqCtor.Invoke([fx.ArithBytes, 0, fx.ArithBytes.Length]);

        ConstructorInfo bmpCtor = bmpType.GetConstructors().Single(c => c.GetParameters().Length == 2);
        object output = bmpCtor.Invoke([fx.Width, fx.Height]);

        Type paramsType = asm.GetType("Jbig2Decoder.Region.RefinementRegionParams", throwOnError: true)!;
        object pBox = Activator.CreateInstance(paramsType)!;
        paramsType.GetField("GrTemplate")!.SetValue(pBox, fx.GrTemplate);
        paramsType.GetField("TpgrOn")!.SetValue(pBox, fx.TpgrOn);
        paramsType.GetField("Reference")!.SetValue(pBox, refBmp);
        paramsType.GetField("ReferenceDx")!.SetValue(pBox, fx.Dx);
        paramsType.GetField("ReferenceDy")!.SetValue(pBox, fx.Dy);
        paramsType.GetField("Grat")!.SetValue(pBox, fx.Grat);

        Type decType = asm.GetType("Jbig2Decoder.Region.RefinementRegionDecoder", throwOnError: true)!;
        object decoder = Activator.CreateInstance(decType)!;
        var statsSize = (int)decType.GetMethod("StatsSizeFor", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [fx.GrTemplate])!;
        var stats = new byte[statsSize];

        MethodInfo decode = decType.GetMethod("Decode", BindingFlags.Public | BindingFlags.Instance)!;
        decode.Invoke(decoder, [pBox, mq, stats, output]);

        var actual = (byte[])bmpType.GetProperty("Data")!.GetValue(output)!;
        for (var i = 0; i < fx.OutBytes.Length; i++)
        {
            if (actual[i] == fx.OutBytes[i]) continue;
            int row = i / fx.OutStride;
            int col = i % fx.OutStride;
            _out.WriteLine($"First divergence at byte {i} (row {row}, byte-in-row {col}, x≈{col * 8}-{col * 8 + 7})");
            _out.WriteLine($"  expected: 0x{fx.OutBytes[i]:X2}");
            _out.WriteLine($"  actual:   0x{actual[i]:X2}");
            Assert.Fail($"RefinementRegionDecoder diverges from jbig2dec at byte {i}");
        }
    }
}
