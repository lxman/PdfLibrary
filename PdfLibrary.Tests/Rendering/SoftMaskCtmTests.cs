using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Regression: the soft-mask group's coordinate system is the Matrix entry concatenated with "the
/// current transformation matrix at the moment the soft mask is established in the graphics state
/// with the gs operator" (ISO 32000-1 §11.6.5.2 / 32000-2 §11.6.5.1) — NOT identity. Seeding the
/// mask renderer with an identity CTM only coincides with the spec when the gs executes at page
/// top level; inside a placed Form XObject the placement transform was dropped and the mask
/// rendered at patch-local coordinates. Found on the GWG V50 ALL X4 umbrella page (16.8–16.11):
/// every soft-masked object whose own paint rides the mask vanished, because the misplaced mask
/// read 0 over the op, while the same patches as single-patch fixture pages (gs at top level)
/// rendered fine.
/// </summary>
public class SoftMaskCtmTests
{
    private static byte[] BuildPdf()
    {
        // Form /Fm: sets /GS (whose SMask group /Msk paints a white 50x50 square at local
        // (10,10)) then fills a blue 50x50 square at the same local coords. The page places the
        // form at (100,400) via cm. Spec: the mask follows the form's placement (CTM at gs), so
        // its white square must record at page coords (110,410)-(160,460), exactly over the fill.
        const string msk = "1 1 1 rg 10 10 50 50 re f";
        byte[] mskBytes = System.Text.Encoding.Latin1.GetBytes(msk);
        const string fm = "/GS gs 0 0 1 rg 10 10 50 50 re f";
        byte[] fmBytes = System.Text.Encoding.Latin1.GetBytes(fm);
        const string pageContent = "q 1 0 0 1 100 400 cm /Fm Do Q";
        byte[] pcBytes = System.Text.Encoding.Latin1.GetBytes(pageContent);

        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, System.Text.Encoding.Latin1, leaveOpen: true) { NewLine = "\r\n" };
        void Write(string s) { w.Write(s); w.Flush(); }

        Write("%PDF-1.7\r\n");
        var off = new int[9];
        w.Flush(); off[1] = (int)ms.Position;
        Write("1 0 obj\r\n<< /Type /Catalog /Pages 2 0 R >>\r\nendobj\r\n");
        w.Flush(); off[2] = (int)ms.Position;
        Write("2 0 obj\r\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\r\nendobj\r\n");
        w.Flush(); off[3] = (int)ms.Position;
        Write("3 0 obj\r\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
              "/Resources << /XObject << /Fm 5 0 R >> >> >>\r\nendobj\r\n");
        w.Flush(); off[4] = (int)ms.Position;
        Write($"4 0 obj\r\n<< /Length {pcBytes.Length} >>\r\nstream\r\n");
        w.Flush(); ms.Write(pcBytes, 0, pcBytes.Length); Write("\r\nendstream\r\nendobj\r\n");
        w.Flush(); off[5] = (int)ms.Position;
        Write("5 0 obj\r\n<< /Type /XObject /Subtype /Form /BBox [0 0 200 200] " +
              "/Resources << /ExtGState << /GS 6 0 R >> >> " +
              $"/Length {fmBytes.Length} >>\r\nstream\r\n");
        w.Flush(); ms.Write(fmBytes, 0, fmBytes.Length); Write("\r\nendstream\r\nendobj\r\n");
        w.Flush(); off[6] = (int)ms.Position;
        Write("6 0 obj\r\n<< /Type /ExtGState /SMask << /Type /Mask /S /Luminosity /G 7 0 R >> >>\r\nendobj\r\n");
        w.Flush(); off[7] = (int)ms.Position;
        Write("7 0 obj\r\n<< /Type /XObject /Subtype /Form /BBox [0 0 200 200] " +
              "/Group << /S /Transparency /CS /DeviceRGB >> " +
              $"/Length {mskBytes.Length} >>\r\nstream\r\n");
        w.Flush(); ms.Write(mskBytes, 0, mskBytes.Length); Write("\r\nendstream\r\nendobj\r\n");
        w.Flush(); long xref = ms.Position;
        Write("xref\r\n0 8\r\n0000000000 65535 f\r\n");
        for (var i = 1; i <= 7; i++) Write($"{off[i]:D10} 00000 n\r\n");
        Write("trailer\r\n<< /Size 8 /Root 1 0 R >>\r\nstartxref\r\n");
        Write($"{xref}\r\n%%EOF\r\n");
        w.Flush();
        return ms.ToArray();
    }

    private static (double minX, double minY, double maxX, double maxY) SegmentBounds(IEnumerable<PathSegment> segs)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Add(double x, double y)
        {
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        foreach (PathSegment s in segs)
            switch (s)
            {
                case MoveToSegment m: Add(m.X, m.Y); break;
                case LineToSegment l: Add(l.X, l.Y); break;
                case CurveToSegment c: Add(c.X1, c.Y1); Add(c.X2, c.Y2); Add(c.X3, c.Y3); break;
            }
        return (minX, minY, maxX, maxY);
    }

    [Fact]
    public void SoftMask_group_follows_ctm_at_gs()
    {
        using var ms = new MemoryStream(BuildPdf());
        using PdfDocument doc = PdfDocument.Load(ms);
        PdfPage page = doc.GetPage(0)!;
        PageDrawList list = RecordingRenderTarget.Record(page, 1.0);

        SoftMaskPushCommand? push = null;
        void Find(PageDrawList l)
        {
            foreach (DrawCommand c in l.Commands)
            {
                if (c is SoftMaskPushCommand p) push = p;
                if (c is GroupCommand g) Find(g.Content);
            }
        }
        Find(list);
        Assert.NotNull(push);

        FillCommand? maskFill = push!.Mask.Commands.OfType<FillCommand>().FirstOrDefault();
        Assert.NotNull(maskFill);

        // The white mask square is at form-local (10,10)-(60,60); the form is placed at
        // (100,400). §11.6.5.2: the mask's coordinate system carries that placement, so the
        // recorded (CTM-baked) segments must land at page coords (110,410)-(160,460). The
        // identity-CTM bug recorded them at (10,10)-(60,60) instead.
        (double minX, double minY, double maxX, double maxY) = SegmentBounds(maskFill!.Segments);
        Assert.Equal(110, minX, 1);
        Assert.Equal(410, minY, 1);
        Assert.Equal(160, maxX, 1);
        Assert.Equal(460, maxY, 1);
    }
}
