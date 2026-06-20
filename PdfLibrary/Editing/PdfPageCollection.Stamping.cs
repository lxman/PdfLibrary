using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing.Stamping;

namespace PdfLibrary.Editing;

public sealed partial class PdfPageCollection
{
    private readonly HashSet<int> _wrappedPages = [];

    public void Stamp(int index, Action<PdfStampBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        PdfDictionary page = PageAt(index);
        var builder = new PdfStampBuilder();
        configure(builder);
        if (builder.Author is null) return;
        ApplyStamp(page, builder);
    }

    private void ApplyStamp(PdfDictionary page, PdfStampBuilder builder)
    {
        (double pageW, double pageH) = PageSizePoints(page);
        double bboxW = builder.Width ?? pageW;
        double bboxH = builder.Height ?? pageH;

        PdfIndirectReference xobjRef = FormXObjectCompiler.CompileInto(_document, bboxW, bboxH, builder.Author!);
        string xobjName = PageContentComposer.RegisterXObject(_document, page, xobjRef);
        string? gsName = builder.OpacityValue < 1.0
            ? PageContentComposer.RegisterOpacity(_document, page, builder.OpacityValue)
            : null;

        builder.Placement.ScaleFactor = builder.ScaleValue;
        builder.Placement.RotateDeg = builder.RotateValue;
        IReadOnlyList<double[]> matrices = builder.Placement.ComputeMatrices(pageW, pageH, bboxW, bboxH);

        byte[] invocation = BuildInvocation(matrices, xobjName, gsName);
        PdfArray contents = PageContentComposer.EnsureContentsArray(_document, page);
        if (_wrappedPages.Add(page.ObjectNumber))
            PageContentComposer.WrapExisting(_document, contents);
        PageContentComposer.AddInvocation(_document, contents, invocation, builder.IsUnderlay);
    }

    private static byte[] BuildInvocation(IReadOnlyList<double[]> matrices, string xobjName, string? gsName)
    {
        var sb = new StringBuilder();
        foreach (double[] m in matrices)
        {
            sb.Append("q\n");
            if (gsName is not null) sb.Append($"/{gsName} gs\n");
            sb.Append($"{m[0]:F4} {m[1]:F4} {m[2]:F4} {m[3]:F4} {m[4]:F2} {m[5]:F2} cm\n");
            sb.Append($"/{xobjName} Do\n");
            sb.Append("Q\n");
        }
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static (double w, double h) PageSizePoints(PdfDictionary page)
    {
        if (page.Get(new PdfName("MediaBox")) is PdfArray mb && mb.Count == 4)
        {
            double x0 = mb[0].ToDouble(), y0 = mb[1].ToDouble(), x1 = mb[2].ToDouble(), y1 = mb[3].ToDouble();
            return (Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        }
        return (612, 792);
    }
}
