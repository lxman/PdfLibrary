using System.Text;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ISO 32000-1 §7.8.2: a page's /Contents may be an array of streams that must be concatenated (with
/// white-space between) and parsed as ONE stream — a token/operator boundary can fall between streams.
/// Regression for the GWG2015 spec TOC, where "White objects" was dropped because its `[…]` array closed
/// one content stream and its `TJ` opened the next, so the operator lost its operand when each stream was
/// parsed on its own.
/// </summary>
public class MultiStreamContentTests
{
    // Builds a one-page PDF whose /Contents is an array of the given stream bodies, with a proper xref.
    private static byte[] BuildTwoStreamPage(string streamA, string streamB)
    {
        var objs = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents [4 0 R 5 0 R] /Resources << >> >>",
            $"<< /Length {streamA.Length} >>\nstream\n{streamA}\nendstream",
            $"<< /Length {streamB.Length} >>\nstream\n{streamB}\nendstream",
        };

        var sb = new StringBuilder();
        sb.Append("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var i = 0; i < objs.Count; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n");
        }
        int xref = sb.Length;
        sb.Append($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n");
        foreach (int off in offsets)
            sb.Append($"{off:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objs.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    [Fact]
    public void Operator_split_across_content_stream_boundary_is_executed()
    {
        // The rectangle op is split: operands end stream A, `re f` begins stream B. Parsed separately the
        // fill is lost; concatenated it paints one rectangle.
        byte[] pdf = BuildTwoStreamPage("50 50 100 75", "re\nf");
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        PdfPage page = doc.GetPage(0)!;

        var mock = new MockRenderTarget();
        page.Render(mock);

        Assert.Contains(mock.Operations, op => op.StartsWith("FillPath"));
    }
}
