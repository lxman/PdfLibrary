using System.Reflection;
using Jbig2Decoder.Mq;
using Xunit.Abstractions;

namespace Jbig2Decoder.Tests.Region;

public class SymbolDictionaryDecoderTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "SymbolDictionary", name);

    private readonly ITestOutputHelper _out;
    public SymbolDictionaryDecoderTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Arithmetic_NoRefAgg_042_15() => RunFixture("042_15.bin");

    [Fact]
    public void Huffman_NoRefAgg_042_11() => RunFixture("042_11_huffman.bin");

    private void RunFixture(string fixtureName)
    {
        SymbolDictionaryFixture fx = SymbolDictionaryFixture.Load(FixturePath(fixtureName));
        Assert.False(fx.SdRefAgg, "fixture must be no-refagg");

        Assembly asm = typeof(MqDecoder).Assembly;
        Type paramsType = asm.GetType("Jbig2Decoder.Region.SymbolDictionaryParams", throwOnError: true)!;
        object pBox = Activator.CreateInstance(paramsType)!;
        paramsType.GetField("SdHuff")!.SetValue(pBox, fx.SdHuff);
        paramsType.GetField("SdRefAgg")!.SetValue(pBox, fx.SdRefAgg);
        paramsType.GetField("SdTemplate")!.SetValue(pBox, fx.SdTemplate);
        paramsType.GetField("SdRTemplate")!.SetValue(pBox, fx.SdRTemplate);
        paramsType.GetField("Sdat")!.SetValue(pBox, fx.Sdat);
        paramsType.GetField("Sdrat")!.SetValue(pBox, fx.Sdrat);
        paramsType.GetField("SdNumInSyms")!.SetValue(pBox, fx.NumIn);
        paramsType.GetField("SdNumNewSyms")!.SetValue(pBox, fx.NumNew);
        paramsType.GetField("SdNumExSyms")!.SetValue(pBox, fx.NumEx);
        paramsType.GetField("SdHuffFlags")!.SetValue(pBox, fx.HuffmanFlags);

        Type decType = asm.GetType("Jbig2Decoder.Region.SymbolDictionaryDecoder", throwOnError: true)!;
        object decoder = Activator.CreateInstance(decType)!;
        MethodInfo decode = decType.GetMethod("Decode", BindingFlags.Public | BindingFlags.Instance)!;
        object result = decode.Invoke(decoder, [pBox, fx.ArithBytes, 0, fx.ArithBytes.Length])!;

        Type sdType = asm.GetType("Jbig2Decoder.Region.SymbolDictionary", throwOnError: true)!;
        var glyphs = (Array)sdType.GetProperty("Glyphs")!.GetValue(result)!;

        Assert.Equal(fx.NumEx, (uint)glyphs.Length);

        Type bmpType = asm.GetType("Jbig2Decoder.Image.Bitmap", throwOnError: true)!;
        PropertyInfo widthProp  = bmpType.GetProperty("Width")!;
        PropertyInfo heightProp = bmpType.GetProperty("Height")!;
        PropertyInfo dataProp   = bmpType.GetProperty("Data")!;

        for (var i = 0; i < fx.NumEx; i++)
        {
            object actual = glyphs.GetValue(i)!;
            var aw = (int)widthProp.GetValue(actual)!;
            var ah = (int)heightProp.GetValue(actual)!;
            var adata = (byte[])dataProp.GetValue(actual)!;

            SymbolBitmap exp = fx.ExSyms[i];
            if (aw != exp.Width || ah != exp.Height)
            {
                _out.WriteLine($"Glyph {i}: dims expected={exp.Width}x{exp.Height}, actual={aw}x{ah}");
                Assert.Fail($"Glyph {i} dimension mismatch in {fixtureName}");
            }

            // Compare meaningful pixels only. jbig2_image_new uses malloc (not
            // calloc), so its glyph bytes contain allocator garbage in the
            // (width % 8) padding region — comparing raw bytes is fragile.
            // Masking the last byte of each row gives a clean structural diff.
            for (var row = 0; row < exp.Height; row++)
            {
                for (var colByte = 0; colByte < exp.Stride; colByte++)
                {
                    int b = row * exp.Stride + colByte;
                    int firstPixelInByte = colByte * 8;
                    int lastPixelInByte = Math.Min(firstPixelInByte + 7, exp.Width - 1);
                    int meaningfulBits = lastPixelInByte - firstPixelInByte + 1;
                    var mask = (byte)(0xFF & ~((1 << (8 - meaningfulBits)) - 1));
                    var expMasked = (byte)(exp.Bytes[b] & mask);
                    var actMasked = (byte)(adata[b] & mask);
                    if (expMasked == actMasked) continue;
                    _out.WriteLine($"Glyph {i} byte {b} (row {row}, col-byte {colByte}): expected 0x{expMasked:X2}, actual 0x{actMasked:X2} (mask 0x{mask:X2})");
                    Assert.Fail($"Glyph {i} body mismatch in {fixtureName}");
                }
            }
        }
    }
}
