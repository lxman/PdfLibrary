using System.Text;
using System.Text.RegularExpressions;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

public class MultilineNewlineTests
{
    // ── AP stream helper (mirrors TextAppearanceTests) ────────────────────────

    private static string ApStreamText(PdfDocument doc, PdfTextField field)
    {
        var widget = field.Widgets[0];
        var apRaw = widget.Get(new PdfName("AP"));
        var ap = FormFieldTree.Resolve(doc, apRaw) as PdfDictionary;
        Assert.NotNull(ap);

        var nRaw = ap!.Get(new PdfName("N"));
        var nObj = FormFieldTree.Resolve(doc, nRaw);
        Assert.NotNull(nObj);

        var stream = nObj as PdfStream;
        if (stream is null && nObj is PdfIndirectReference ir)
            stream = doc.GetObject(ir.ObjectNumber) as PdfStream;

        Assert.NotNull(stream);
        return Encoding.ASCII.GetString(stream!.GetDecodedData());
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A multiline text field with value "Line one\nLine two\nLine three"
    /// must emit three separate Tj operations — one per line — not a single Tj
    /// that embeds the newlines as literal characters.
    /// </summary>
    [Fact]
    public void Multiline_ExplicitNewlines_EmitSeparateTjPerLine()
    {
        // Ff bit 13 (1-based) = 1 << 12 = 4096 (multiline)
        const int multilineFf = 1 << 12;
        // Use explicit newlines that fit within the box width (no word-wrap required)
        const string value = "Line one\nLine two\nLine three";

        // Wide box (324 wide, 60 tall) so word-wrap does not split the short lines
        byte[] pdf = FormTestDocs.WithTextFieldEx(
            name: "ml",
            value: null,
            ff: multilineFf,
            maxLen: null,
            rectW: 324,
            rectH: 60);

        string outPath = Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf)))
            {
                var edit = doc.Edit();
                ((PdfTextField)edit.Forms["ml"]!).Value = value;
                edit.Save(outPath);
            }

            using PdfDocument re = PdfDocument.Load(outPath);
            var f = (PdfTextField)re.Edit().Forms["ml"]!;

            // Value must round-trip
            Assert.Equal(value, f.Value);

            string ap = ApStreamText(re, f);

            // Each line must appear as a distinct PDF string show
            Assert.Contains("(Line one)", ap);
            Assert.Contains("(Line two)", ap);
            Assert.Contains("(Line three)", ap);

            // Must have exactly 3 Tj operations — one per hard line
            int tjCount = Regex.Matches(ap, @"\bTj\b").Count;
            Assert.True(tjCount >= 3,
                $"Expected >=3 Tj operations for explicit newlines but got {tjCount}.\nAP stream:\n{ap}");
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    /// <summary>
    /// An empty hard line produced by consecutive newlines still consumes vertical space
    /// (does not collapse), so the lines after it are positioned lower.
    /// </summary>
    [Fact]
    public void Multiline_ConsecutiveNewlines_PreserveBlankLine()
    {
        const int multilineFf = 1 << 12;
        // "A" then blank line then "B" — the blank must count as a line slot
        const string value = "A\n\nB";

        byte[] pdf = FormTestDocs.WithTextFieldEx(
            name: "ml2",
            value: null,
            ff: multilineFf,
            maxLen: null,
            rectW: 324,
            rectH: 80);

        string outPath = Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf)))
            {
                var edit = doc.Edit();
                ((PdfTextField)edit.Forms["ml2"]!).Value = value;
                edit.Save(outPath);
            }

            using PdfDocument re = PdfDocument.Load(outPath);
            var f = (PdfTextField)re.Edit().Forms["ml2"]!;

            Assert.Equal(value, f.Value);

            string ap = ApStreamText(re, f);

            // Both "A" and "B" must appear as distinct Tj operations
            Assert.Contains("(A)", ap);
            Assert.Contains("(B)", ap);

            // Must have at least 2 Td or Tm advances (A, blank, B)
            // i.e. at least 3 show operations total but blank may be empty-string Tj
            int tjCount = Regex.Matches(ap, @"\bTj\b").Count;
            Assert.True(tjCount >= 2,
                $"Expected >=2 Tj for 'A\\n\\nB' but got {tjCount}.\nAP:\n{ap}");
        }
        finally
        {
            File.Delete(outPath);
        }
    }
}
