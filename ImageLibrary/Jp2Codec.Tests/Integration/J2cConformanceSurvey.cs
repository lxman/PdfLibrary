using Jp2Codec.Codestream;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.Tests.Integration;

/// <summary>
/// One-off: for every <c>conformance/*.j2c</c> file in the corpus, dump the
/// main-header summary and attempt a full decode. Flip <see cref="Run"/> on
/// to regenerate <c>j2c_survey.txt</c>; the output drives which features
/// (SOP/EPH, TERMALL, LAZY, non-LRCP progression, etc.) are worth adding
/// next.
/// </summary>
public class J2cConformanceSurvey
{
    private static readonly bool Run = true;
    private readonly ITestOutputHelper _output;

    private static readonly string CorpusRoot =
        @"C:\Users\jorda\RiderProjects\PDF\ImageLibrary\TestImages\jp2_test\conformance";

    public J2cConformanceSurvey(ITestOutputHelper output) => _output = output;

    [Fact]
    public void DescribeAndDecodeEachJ2c()
    {
        if (!Run) return;

        List<string> files = Directory.EnumerateFiles(CorpusRoot, "*.j2c")
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                             .ToList();
        int pass = 0, fail = 0;
        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            byte[] bytes = File.ReadAllBytes(path);
            string header;
            try
            {
                var decoder = new Jp2StreamDecoder();
                MainHeader mh = decoder.InspectMainHeader(bytes, out _);
                header = DescribeHeader(mh);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"{name,-22} HEADER-FAIL: {ex.GetType().Name} — {ex.Message.Split('\n')[0]}");
                fail++;
                continue;
            }

            string decode;
            try
            {
                Jp2DecodeResult result = new Jp2StreamDecoder().Decode(bytes);
                int totalSamples = result.ComponentData.Sum(c => c.Length);
                decode = $"OK ({totalSamples} samples)";
                pass++;
            }
            catch (Exception ex)
            {
                decode = $"DECODE-FAIL: {ex.GetType().Name} — {ex.Message.Split('\n')[0]}";
                fail++;
            }
            _output.WriteLine($"{name,-22} {header} | {decode}");
        }
        _output.WriteLine($"\nTotal: {pass} pass, {fail} fail of {files.Count}.");
    }

    private static string DescribeHeader(MainHeader mh)
    {
        SizSegment siz = mh.Siz;
        CodSegment cod = mh.Cod;
        string sub = string.Join(",", siz.Components.Select(c =>
            $"{c.HorizontalSubsampling}x{c.VerticalSubsampling}"));
        bool uniformSub = siz.Components.All(c =>
            c.HorizontalSubsampling == siz.Components[0].HorizontalSubsampling &&
            c.VerticalSubsampling == siz.Components[0].VerticalSubsampling);
        string subTag = uniformSub ? $"{siz.Components[0].HorizontalSubsampling}x{siz.Components[0].VerticalSubsampling}" : sub;

        bool uniformBitDepth = siz.Components.All(c => c.BitDepth == siz.Components[0].BitDepth);
        string bdTag = uniformBitDepth ? siz.Components[0].BitDepth.ToString()
                                       : string.Join(",", siz.Components.Select(c => c.BitDepth.ToString()));
        string xform = cod.WaveletTransform == WaveletTransform.Reversible5x3 ? "5/3" : "9/7";
        string style = cod.CodeBlockStyle == CodeBlockStyle.None ? "-" : cod.CodeBlockStyle.ToString();

        var notes = new List<string>();
        if (cod.UseSopMarkers) notes.Add("SOP");
        if (cod.UseEphMarkers) notes.Add("EPH");
        if (cod.UseExplicitPrecincts) notes.Add("explicit-precincts");
        if (cod.ProgressionOrder != ProgressionOrder.Lrcp) notes.Add($"prog={cod.ProgressionOrder}");
        if (mh.UnparsedMarkers.Count > 0)
            notes.Add("unparsed:" + string.Join(",", mh.UnparsedMarkers.Select(m => MarkerCode.Format(m))));

        return $"{siz.ImageWidth}x{siz.ImageHeight} C={siz.NumberOfComponents} bd={bdTag} NL={cod.DecompositionLevels} {xform} MCT={(cod.UseMultipleComponentTransform ? "Y" : "N")} L={cod.NumberOfLayers} style={style} sub={subTag} notes=[{string.Join(",", notes)}]";
    }
}
