using System.Reflection;
using Jbig2Decoder.Mq;
using Xunit.Abstractions;

namespace Jbig2Decoder.Tests.Region;

public class TextRegionDecoderTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "TextRegion", name);

    private readonly ITestOutputHelper _out;
    public TextRegionDecoderTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void TextRegion_042_15() => RunFixture("042_15.bin");

    [Fact]
    public void TextRegion_042_11_Huffman() => RunFixture("042_11.bin");

    private void RunFixture(string fixtureName)
    {
        TextRegionFixture fx = TextRegionFixture.Load(FixturePath(fixtureName));

        Assembly asm = typeof(MqDecoder).Assembly;
        Type bmpType   = asm.GetType("Jbig2Decoder.Image.Bitmap", throwOnError: true)!;
        ConstructorInfo bmp3Ctor  = bmpType.GetConstructors().Single(c => c.GetParameters().Length == 3);
        ConstructorInfo bmp2Ctor  = bmpType.GetConstructors().Single(c => c.GetParameters().Length == 2);
        Type sdType    = asm.GetType("Jbig2Decoder.Region.SymbolDictionary", throwOnError: true)!;
        ConstructorInfo sdCtor    = sdType.GetConstructors().Single();

        // Materialise dicts.
        Type bmpArrayType = bmpType.MakeArrayType();
        var dicts = (Array)Array.CreateInstance(sdType, fx.Dicts.Length);
        for (var d = 0; d < fx.Dicts.Length; d++)
        {
            var glyphs = (Array)Array.CreateInstance(bmpType, fx.Dicts[d].Length);
            for (var i = 0; i < fx.Dicts[d].Length; i++)
            {
                SymbolBitmap sb = fx.Dicts[d][i];
                glyphs.SetValue(bmp3Ctor.Invoke([sb.Width, sb.Height, sb.Bytes]), i);
            }
            dicts.SetValue(sdCtor.Invoke([glyphs]), d);
        }

        // Build params.
        Type paramsType = asm.GetType("Jbig2Decoder.Region.TextRegionParams", throwOnError: true)!;
        Type refCornerType = asm.GetType("Jbig2Decoder.Region.RefCorner", throwOnError: true)!;
        object pBox = Activator.CreateInstance(paramsType)!;
        paramsType.GetField("SbHuff")!.SetValue(pBox, fx.SbHuff);
        paramsType.GetField("SbRefine")!.SetValue(pBox, fx.SbRefine);
        paramsType.GetField("SbDefPixel")!.SetValue(pBox, fx.SbDefPixel);
        paramsType.GetField("SbCombOp")!.SetValue(pBox, fx.SbCombOp);
        paramsType.GetField("Transposed")!.SetValue(pBox, fx.Transposed);
        paramsType.GetField("RefCorner")!.SetValue(pBox, Enum.ToObject(refCornerType, fx.RefCorner));
        paramsType.GetField("SbDsOffset")!.SetValue(pBox, fx.SbDsOffset);
        paramsType.GetField("SbNumInstances")!.SetValue(pBox, fx.SbNumInstances);
        paramsType.GetField("LogSbStrips")!.SetValue(pBox, fx.LogSbStrips);
        paramsType.GetField("SbStrips")!.SetValue(pBox, fx.SbStrips);
        paramsType.GetField("SbRTemplate")!.SetValue(pBox, fx.SbRTemplate);
        paramsType.GetField("Sbrat")!.SetValue(pBox, fx.Sbrat);
        paramsType.GetField("Dicts")!.SetValue(pBox, dicts);
        paramsType.GetField("SbHuffFlags")!.SetValue(pBox, fx.HuffmanFlags);

        object output = bmp2Ctor.Invoke([fx.Width, fx.Height]);

        Type decType = asm.GetType("Jbig2Decoder.Region.TextRegionDecoder", throwOnError: true)!;
        object decoder = Activator.CreateInstance(decType)!;
        MethodInfo decode = decType.GetMethod("Decode", BindingFlags.Public | BindingFlags.Instance)!;
        decode.Invoke(decoder, [pBox, fx.ArithBytes, 0, fx.ArithBytes.Length, output]);

        var actual = (byte[])bmpType.GetProperty("Data")!.GetValue(output)!;

        for (var i = 0; i < fx.OutBytes.Length; i++)
        {
            if (actual[i] == fx.OutBytes[i]) continue;
            int row = i / fx.OutStride;
            int col = i % fx.OutStride;
            _out.WriteLine($"First divergence at byte {i} (row {row}, byte-in-row {col}, x≈{col * 8}-{col * 8 + 7})");
            _out.WriteLine($"  expected: 0x{fx.OutBytes[i]:X2}");
            _out.WriteLine($"  actual:   0x{actual[i]:X2}");
            Assert.Fail($"TextRegionDecoder diverges from jbig2dec at byte {i}");
        }
    }
}
