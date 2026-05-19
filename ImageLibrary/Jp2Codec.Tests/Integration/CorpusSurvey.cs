using System.Text;
using Jp2Codec.Codestream;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.Tests.Integration;

/// <summary>
/// One-off introspection tool: walks every .jp2 in the conformance corpus
/// and dumps a one-line summary per file (dimensions, components, NL,
/// transform, MCT, layers, code-block style, subsampling, tile count).
/// Flip <see cref="Run"/> to <c>true</c> to regenerate <c>corpus_survey.txt</c>.
/// </summary>
public class CorpusSurvey
{
    private static readonly bool Run = false;

    private static readonly string CorpusRoot =
        @"C:\Users\jorda\RiderProjects\PDF\ImageLibrary\TestImages\jp2_test";

    [Fact]
    public void EmitSurvey()
    {
        if (!Run) return;

        var sb = new StringBuilder();
        sb.AppendLine("file | WxH | C | bd | NL | xform | MCT | L | style | sub | tiles | notes");
        sb.AppendLine("-----|-----|---|----|----|-------|-----|---|-------|-----|-------|------");

        var files = Directory.EnumerateFiles(CorpusRoot, "*.jp2", SearchOption.AllDirectories)
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
        foreach (string path in files)
        {
            string rel = Path.GetRelativePath(CorpusRoot, path).Replace('\\', '/');
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var decoder = new Jp2StreamDecoder();
                MainHeader header = decoder.InspectMainHeader(bytes, out _);
                sb.AppendLine(DescribeHeader(rel, header));
            }
            catch (Exception ex)
            {
                sb.AppendLine($"{rel} | (parse error: {ex.GetType().Name} — {ex.Message.Split('\n')[0]})");
            }
        }

        string outPath = "C:/Users/jorda/RiderProjects/ImageLibraries/Jp2Codec.Tests/bin/Debug/net10.0/corpus_survey.txt";
        File.WriteAllText(outPath, sb.ToString());
    }

    private static string DescribeHeader(string path, MainHeader h)
    {
        SizSegment siz = h.Siz;
        CodSegment cod = h.Cod;
        string sub = string.Join(",",
            siz.Components.Select(c => $"{c.HorizontalSubsampling}x{c.VerticalSubsampling}"));
        bool uniformSub = siz.Components.All(c =>
            c.HorizontalSubsampling == siz.Components[0].HorizontalSubsampling &&
            c.VerticalSubsampling == siz.Components[0].VerticalSubsampling);
        string subTag = uniformSub ? $"{siz.Components[0].HorizontalSubsampling}x{siz.Components[0].VerticalSubsampling}" : sub;

        bool uniformBitDepth = siz.Components.All(c => c.BitDepth == siz.Components[0].BitDepth);
        string bdTag = uniformBitDepth ? siz.Components[0].BitDepth.ToString()
                                       : string.Join(",", siz.Components.Select(c => c.BitDepth.ToString()));

        int tileCount = NumTilesXY(siz);
        string transform = cod.WaveletTransform == WaveletTransform.Reversible5x3 ? "5/3" : "9/7";
        string mct = cod.UseMultipleComponentTransform ? "Y" : "N";
        string style = cod.CodeBlockStyle == CodeBlockStyle.None ? "-" : cod.CodeBlockStyle.ToString();

        var notes = new System.Collections.Generic.List<string>();
        if (cod.UseSopMarkers) notes.Add("SOP");
        if (cod.UseEphMarkers) notes.Add("EPH");
        if (cod.UseExplicitPrecincts) notes.Add("explicit-precincts");
        if (h.UnparsedMarkers.Count > 0)
        {
            notes.Add("unparsed:" + string.Join(",",
                h.UnparsedMarkers.Select(m => MarkerCode.Format(m))));
        }
        if (cod.ProgressionOrder != ProgressionOrder.Lrcp)
            notes.Add("prog=" + cod.ProgressionOrder);

        return $"{path} | {siz.ImageWidth}x{siz.ImageHeight} | C={siz.NumberOfComponents} | bd={bdTag} | NL={cod.DecompositionLevels} | {transform} | MCT={mct} | L={cod.NumberOfLayers} | {style} | sub={subTag} | tiles={tileCount} | {string.Join(", ", notes)}";
    }

    private static int NumTilesXY(SizSegment siz)
    {
        long xsiz = siz.ReferenceGridWidth;
        long xtosiz = siz.TileHorizontalOffset;
        long xtsiz = siz.TileWidth;
        long ysiz = siz.ReferenceGridHeight;
        long ytosiz = siz.TileVerticalOffset;
        long ytsiz = siz.TileHeight;
        int numX = (int)((xsiz - xtosiz + xtsiz - 1) / xtsiz);
        int numY = (int)((ysiz - ytosiz + ytsiz - 1) / ytsiz);
        return numX * numY;
    }
}
