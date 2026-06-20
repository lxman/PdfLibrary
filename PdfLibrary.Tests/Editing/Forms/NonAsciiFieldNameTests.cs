using System.IO;
using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

/// <summary>
/// Verifies that field partial names stored as UTF-16BE text strings (BOM FE FF) are
/// decoded correctly when the form tree is read.  The bug was that GetStringValue used
/// PdfString.Value (Latin-1) instead of PdfString.GetText() (BOM-sniffed).
/// </summary>
public class NonAsciiFieldNameTests
{
    /// <summary>
    /// Hand-builds a minimal PDF where the /T entry for a text field is a UTF-16BE hex
    /// string so that the name "名前" survives the round-trip through PdfString.GetText().
    /// </summary>
    private static byte[] WithNonAsciiTextField(string name, string value)
    {
        // Encode the field name as a PDF text string (UTF-16BE with BOM FEFF)
        // We use PdfString.FromText and then ToPdfString() to get the hex encoding.
        PdfString nameStr = PdfString.FromText(name);
        string namePdfToken = nameStr.ToPdfString(); // e.g. <FEFF540D524D>

        PdfString valueStr = PdfString.FromText(value);
        string valuePdfToken = valueStr.ToPdfString();

        // Object layout:
        //   1: pages node
        //   2: catalog
        //   3: page
        //   4: field/widget (merged)
        //   5: AcroForm
        var offsets = new System.Collections.Generic.Dictionary<int, long>();

        using var ms = new MemoryStream();
        using var w = new StreamWriter(ms, Encoding.Latin1, leaveOpen: true);

        w.WriteLine("%PDF-1.7");
        w.Flush();

        void WriteObj(int n, string content)
        {
            w.Flush();
            offsets[n] = ms.Position;
            w.WriteLine($"{n} 0 obj");
            w.WriteLine(content);
            w.WriteLine("endobj");
            w.WriteLine();
        }

        WriteObj(1, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObj(2, "<< /Type /Catalog /Pages 1 0 R /AcroForm 5 0 R >>");
        WriteObj(3, "<< /Type /Page /Parent 1 0 R /MediaBox [0 0 612 792] /Annots [4 0 R] >>");

        WriteObj(4,
            $"<< /Type /Annot /Subtype /Widget /FT /Tx /T {namePdfToken} " +
            $"/V {valuePdfToken} " +
            $"/DA (/Helv 12 Tf 0 g) " +
            $"/Rect [72 700 372 720] >>");

        WriteObj(5, "<< /Fields [4 0 R] /NeedAppearances true >>");

        w.Flush();
        long xrefOffset = ms.Position;
        const int totalObjs = 5;

        w.WriteLine("xref");
        w.WriteLine($"0 {totalObjs + 1}");
        w.WriteLine("0000000000 65535 f ");
        for (int i = 1; i <= totalObjs; i++)
        {
            if (offsets.TryGetValue(i, out long off))
                w.WriteLine($"{off:D10} 00000 n ");
            else
                w.WriteLine("0000000000 65535 f ");
        }

        w.WriteLine("trailer");
        w.WriteLine($"<< /Size {totalObjs + 1} /Root 2 0 R >>");
        w.WriteLine("startxref");
        w.WriteLine(xrefOffset.ToString());
        w.WriteLine("%%EOF");
        w.Flush();

        return ms.ToArray();
    }

    [Fact]
    public void NonAsciiFieldName_FullNameAndLookup_Work()
    {
        const string fieldName = "名前";
        byte[] pdf = WithNonAsciiTextField(fieldName, "x");

        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        var forms = doc.Edit().Forms;

        PdfFormField? f = forms[fieldName];
        Assert.NotNull(f);
        Assert.Equal(fieldName, f!.FullName);
    }
}
